using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// タプル（Redmine #82）。UdonSharp はタプルに対応していないので、要素の型の並びごとの普通のクラスに書き換える（匿名型 #70 と同じ形）。
    ///
    ///   (int, string) p = (1, "a");  int n = p.Item1;      →  __Tuple_0 p = new __Tuple_0(1, "a");  int n = p.Item1;
    ///   var (x, y) = Get();                                 →  __Tuple_0 __td0 = Get(); int x = __td0.Item1; string y = __td0.Item2;
    ///   public class __Tuple_0 { public int Item1; public string Item2; public __Tuple_0(int Item1, string Item2) { ... } ... }
    ///
    /// - 型（(int, string)・ValueTuple&lt;int, string&gt;）、タプルの式、要素の読み出し（Item1・名前付き）、分解（文として書いたもの・foreach）、
    ///   ==・!=・Equals・ToString・文字列の連結を書き換える。要素の名前は型の一部ではない（C# と同じ）ので、クラスは型の並びだけで決まる
    /// - タプルは C# では値型。ここでは参照（object[]）になるので、要素への代入（t.Item1 = 5）はエラーにする
    ///   （代入・引数で渡すたびにコピーされる C# と、動きが変わるため）。丸ごとの代入・作り直しは使える
    /// - 既定値（default・初期化子のないフィールド）は、C# と同じく「各要素が既定値のタプル」にする（null にしない）
    /// - object・インタフェースにする変換、GetHashCode・CompareTo、要素の型を変える変換、タプルをキーにする Dictionary、switch・パターンはエラー
    /// - 匿名型と同じく 2 回動く（ジェネリクスの前と、ジェネリクスのコピーのあと。型引数を使うタプルは 2 回目）。匿名型の段のあとに置く
    ///   （匿名型を要素にするタプルが、生成済みのクラスの名前で書けるように）
    /// - 行は増やさない（生成するクラスは 1 行。分解は元の文と同じ行に並べる）
    /// </summary>
    internal static class TupleLowering
    {
        /// <summary>生成するクラスの名前の先頭（__Tuple_0 など）</summary>
        internal const string GeneratedPrefix = "__Tuple_";

        public static bool MayHaveTuples(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n =>
                n is TupleTypeSyntax || n is TupleExpressionSyntax || n is ParenthesizedVariableDesignationSyntax
                || n is ForEachVariableStatementSyntax
                || n is GenericNameSyntax g && g.Identifier.ValueText == "ValueTuple"));

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references,
            ref int counter, bool beforeGenerics)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_TuplePrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var shared = new Shared(counter);
            var result = new List<SyntaxTree>(trees.Count);
            for (int i = 0; i < trees.Count; i++)
            {
                shared.CurrentTree = i;
                SemanticModel model = compilation.GetSemanticModel(trees[i]);
                var rewriter = new Rewriter(shared, model, diagnostics, beforeGenerics);
                SyntaxNode newRoot = rewriter.Visit(trees[i].GetRoot());
                result.Add(rewriter.Changed ? Reparse(newRoot, trees[i]) : trees[i]);
            }

            counter += shared.Infos.Count;

            // 生成するクラスは、最初に使ったファイルの最後のメンバーのうしろ（名前空間の外）に、同じ行で置く
            foreach (var group in shared.Infos.GroupBy(i => i.HomeTree))
            {
                SyntaxTree tree = result[group.Key];
                if (!(tree.GetRoot() is CompilationUnitSyntax unit) || unit.Members.Count == 0)
                    continue;
                var members = unit.Members.ToList();
                MemberDeclarationSyntax last = members[members.Count - 1];
                SyntaxTriviaList trailing = last.GetTrailingTrivia();
                members[members.Count - 1] = last.WithTrailingTrivia(SyntaxFactory.TriviaList());
                var infos = group.ToList();
                for (int k = 0; k < infos.Count; k++)
                    members.Add(SyntaxFactory.ParseMemberDeclaration(infos[k].FullCode())
                        .WithLeadingTrivia(SyntaxFactory.Space)
                        .WithTrailingTrivia(k == infos.Count - 1 ? trailing : SyntaxFactory.TriviaList()));
                result[group.Key] = Reparse(unit.WithMembers(SyntaxFactory.List(members)), tree);
            }
            return result;
        }

        private static SyntaxTree Reparse(SyntaxNode root, SyntaxTree original) =>
            CSharpSyntaxTree.ParseText(root.ToFullString(), (CSharpParseOptions)original.Options, original.FilePath, Encoding.UTF8);

        /// <summary>タプルの型 1 つ（要素の型の並びが同じものは 1 つ。要素の名前は見ない）</summary>
        private sealed class TupleInfo
        {
            public string Name;
            public int HomeTree;
            public string Code;            // 閉じかっこの手前まで
            public string ToStringError;   // ToString・文字列の連結に使えない理由（null なら使える）
            public string EqualsError;     // Equals に使えない理由
            public string OperatorCode;    // == のコード（使ったときだけ入れる。== のない型の要素があると、コンパイルできないため）
            public string ArrayCode;       // new (int, string)[n] のコード（使ったときだけ）
            public bool UsesOperator;
            public bool UsesArray;
            public readonly SortedSet<string> ListHelpers = new SortedSet<string>(StringComparer.Ordinal);

            public string FullCode() =>
                Code + (UsesOperator ? OperatorCode + " " : "") + (UsesArray ? ArrayCode + " " : "")
                + AnonymousTypeLowering.ListHelpersCode(ListHelpers, Name) + "}";
        }

        private sealed class Shared
        {
            public int CurrentTree;
            private readonly int _start;

            public Shared(int start) { _start = start; }

            public readonly List<TupleInfo> Infos = new List<TupleInfo>();
            private readonly Dictionary<string, TupleInfo> _byKey = new Dictionary<string, TupleInfo>(StringComparer.Ordinal);

            private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;
            private static readonly SymbolDisplayFormat QualifiedNoGenerics = SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

            /// <summary>タプルの型に対応するクラス。作れなければ null と理由</summary>
            public TupleInfo Get(INamedTypeSymbol tuple, out string error)
            {
                error = null;
                var elements = tuple.TupleElements;
                if (elements.IsDefaultOrEmpty || elements.Length > 7)
                {
                    error = "要素が 8 個以上のタプルは未対応です";
                    return null;
                }
                foreach (IFieldSymbol e in elements)
                {
                    string why = Unsupported(e.Type);
                    if (why != null)
                    {
                        error = $"タプルの要素に、{why}は使えません";
                        return null;
                    }
                }

                var types = elements.Select(e => TypeText(e.Type)).ToList();
                string key = string.Join(";", types);
                if (_byKey.TryGetValue(key, out TupleInfo existing))
                    return existing;

                var info = new TupleInfo { Name = GeneratedPrefix + (_start + Infos.Count), HomeTree = CurrentTree };
                _byKey[key] = info;
                Infos.Add(info);
                Generate(info, elements.Select(e => e.Type).ToList(), types);
                return info;
            }

            private static string Unsupported(ITypeSymbol type)
            {
                switch (type)
                {
                    case IArrayTypeSymbol a:
                        return Unsupported(a.ElementType);
                    case ITypeParameterSymbol _:
                        return "型引数（ジェネリクスのコピーのあとの段で、型引数が決まったものだけ使えます）";
                    case IPointerTypeSymbol _:
                        return "ポインター";
                    case INamedTypeSymbol n:
                        if (n.TypeKind == TypeKind.Error)
                            return "解決できない型";
                        if (n.IsAnonymousType)
                            return "型引数を使う匿名型";   // 型の決まった匿名型は、匿名型の段が先にクラスにしている
                        if (n.IsTupleType)
                        {
                            if (n.TupleElements.Length > 7)
                                return "要素が 8 個以上のタプル";
                            foreach (IFieldSymbol e in n.TupleElements)
                            {
                                string why = Unsupported(e.Type);
                                if (why != null)
                                    return why;
                            }
                            return null;
                        }
                        foreach (ITypeSymbol arg in n.TypeArguments)
                        {
                            string why = Unsupported(arg);
                            if (why != null)
                                return why;
                        }
                        return null;
                    default:
                        return null;
                }
            }

            /// <summary>生成するコードに書く型（タプルは生成するクラスの名前にする）</summary>
            public string TypeText(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                if (type is INamedTypeSymbol named)
                {
                    if (named.IsTupleType)
                        return Get(named, out _)?.Name ?? "object";
                    if (named.IsGenericType && named.TypeArguments.Any(ContainsTuple))
                        return named.OriginalDefinition.ToDisplayString(QualifiedNoGenerics) + "<" + string.Join(", ", named.TypeArguments.Select(TypeText)) + ">";
                }
                return type.ToDisplayString(Qualified);
            }

            private static bool ContainsTuple(ITypeSymbol type) =>
                type is IArrayTypeSymbol a ? ContainsTuple(a.ElementType)
                : type is INamedTypeSymbol n && (n.IsTupleType || n.IsGenericType && n.TypeArguments.Any(ContainsTuple));

            /// <summary>既定値の式（タプルは、各要素が既定値のタプル）</summary>
            public string DefaultText(ITypeSymbol type) =>
                type is INamedTypeSymbol n && n.IsTupleType && Get(n, out _) is TupleInfo info ? $"{info.Name}.__Default()" : $"default({TypeText(type)})";

            private static bool IsGenerated(ITypeSymbol type, string prefix) =>
                type is INamedTypeSymbol n && n.Name.StartsWith(prefix, StringComparison.Ordinal);

            /// <summary>ToString の中の、要素の値の書き方。C# の ToString と同じ結果になるものだけ（そうでなければ null）</summary>
            private string ToStringOf(ITypeSymbol type, string expression)
            {
                if (type is INamedTypeSymbol named)
                {
                    if (named.IsTupleType)
                        return Get(named, out _) is TupleInfo inner && inner.ToStringError == null ? $"{inner.Name}.__Str({expression})" : null;
                    // 匿名型の段が生成したクラス（ToString が使えないものは、そのクラスに __Str がなく、コンパイルできないので、ここでは使えない扱いにしない）
                    if (IsGenerated(named, AnonymousTypeLowering.GeneratedPrefix))
                        return named.GetMembers("__Str").Any() ? $"{named.Name}.__Str({expression})" : null;
                    if (named.TypeKind == TypeKind.Struct && !named.Locations.Any(l => l.IsInSource) && named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                        && named.SpecialType == SpecialType.None)
                        return expression;
                }
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Char:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_String:
                        return expression;
                    default:
                        return null;
                }
            }

            private void Generate(TupleInfo info, List<ITypeSymbol> elementTypes, List<string> types)
            {
                string name = info.Name;
                int count = types.Count;
                var items = Enumerable.Range(1, count).Select(i => "Item" + i).ToList();
                var sb = new StringBuilder();
                sb.Append($"public class {name} {{ ");
                for (int i = 0; i < count; i++)
                    sb.Append($"public {types[i]} {items[i]}; ");
                sb.Append($"public {name}({string.Join(", ", items.Select((it, i) => $"{types[i]} {it}"))}) {{ ");
                foreach (string it in items)
                    sb.Append($"this.{it} = {it}; ");
                sb.Append("} ");

                // 既定値：各要素が既定値のタプル（C# の default((int, string)) と同じ）
                sb.Append($"public static {name} __Default() {{ return new {name}({string.Join(", ", elementTypes.Select(DefaultText))}); }} ");
                // LINQ の FirstOrDefault などで、要素がなかったとき（書き換え後は null が返る）は、既定値のタプルにする
                sb.Append($"public static {name} __OrDefault({name} a) {{ return a == null ? __Default() : a; }} ");

                // ToString：(1, a)
                var parts = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string value = ToStringOf(elementTypes[i], items[i]);
                    if (value == null)
                    {
                        info.ToStringError = $"{i + 1} 番目の要素の型（{elementTypes[i].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}）の文字列を、C# と同じにできないため";
                        break;
                    }
                    parts.Add((i == 0 ? "" : "\", \" + ") + value);
                }
                if (info.ToStringError == null)
                {
                    // 最初に呼び出しを置く（null のタプルの ToString が、呼び出した行で止まる。匿名型と同じ）
                    sb.Append($"public string __ToString() {{ string __r = \"(\".ToString(); return __r + {string.Join(" + ", parts)} + \")\"; }} ");
                    sb.Append($"public static string __Str({name} a) {{ return a == null ? \"\" : a.__ToString(); }} ");
                }

                // Equals：要素ごとに値で比べる（タプル・匿名型の要素は入れ子で比べる。デリゲートは比べ方が違うので使えない）
                var compares = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    ITypeSymbol t = elementTypes[i];
                    if (t is INamedTypeSymbol d && d.TypeKind == TypeKind.Delegate)
                    {
                        info.EqualsError = $"{i + 1} 番目の要素がデリゲートで、比べ方が C# と違うため";
                        break;
                    }
                    compares.Add(t is INamedTypeSymbol nt && nt.IsTupleType && Get(nt, out _) is TupleInfo inner
                        ? $"{inner.Name}.__Eq({items[i]}, b.{items[i]})"
                        : IsGenerated(t, AnonymousTypeLowering.GeneratedPrefix) && ((INamedTypeSymbol)t).GetMembers("__Eq").Any()
                            ? $"{((INamedTypeSymbol)t).Name}.__Eq({items[i]}, b.{items[i]})"
                            : $"__E({items[i]}, b.{items[i]})");
                }
                if (info.EqualsError == null)
                {
                    sb.Append($"public bool __Equals(object o) {{ {name} b = o as {name}; if (b == null) return false; return {string.Join(" && ", compares)}; }} ");
                    sb.Append($"public static bool __Eq({name} a, {name} b) {{ if (a == null) return b == null; return a.__Equals(b); }} ");
                    sb.Append("public static bool __E(object x, object y) { if (x == null) return y == null; return x.Equals(y); } ");
                }

                // ==：要素ごとに == で比べる（C# のタプルの == と同じ。タプルの要素は入れ子で比べる）
                var ops = new List<string>();
                for (int i = 0; i < count; i++)
                    ops.Add(elementTypes[i] is INamedTypeSymbol nt && nt.IsTupleType && Get(nt, out _) is TupleInfo inner
                        ? $"{inner.Name}.__Op(a.{items[i]}, b.{items[i]})"
                        : $"a.{items[i]} == b.{items[i]}");
                info.OperatorCode = $"public static bool __Op({name} a, {name} b) {{ return {string.Join(" && ", ops)}; }}";
                info.ArrayCode = $"public static {name}[] __Array(int n) {{ {name}[] r = new {name}[n]; for (int i = 0; i < n; i++) {{ r[i] = __Default(); }} return r; }}";
                info.Code = sb.ToString();
            }

            /// <summary>入れ子のタプルの == も使うことにする（__Op は、要素のタプルの __Op を呼ぶ）</summary>
            public void UseOperator(INamedTypeSymbol tuple)
            {
                TupleInfo info = Get(tuple, out _);
                if (info == null || info.UsesOperator)
                    return;
                info.UsesOperator = true;
                foreach (IFieldSymbol e in tuple.TupleElements)
                    if (e.Type is INamedTypeSymbol n && n.IsTupleType)
                        UseOperator(n);
            }
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly Shared _shared;
            private readonly SemanticModel _model;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly Stack<string> _owners = new Stack<string>();   // 普通のクラスの中なら、そのクラスの型タグ（使われないクラスのエラーは捨てる）
            private readonly HashSet<SyntaxNode> _allowed = new HashSet<SyntaxNode>();   // object への変換を、書き換えて扱うところ
            private readonly bool _defer;   // ジェネリクスの前：型引数を使うタプルは、あとの回に回す
            private int _tempCount;
            private int _containsCount;

            public bool Changed { get; private set; }

            public Rewriter(Shared shared, SemanticModel model, InterfaceLowering.LoweringDiagnostics diagnostics, bool defer)
            {
                _shared = shared;
                _model = model;
                _diagnostics = diagnostics;
                _defer = defer;
            }

            private void Error(SyntaxNode node, string message) =>
                _diagnostics.Error(node.GetLocation(), message, _owners.Count > 0 ? _owners.Peek() : null);

            private ExpressionSyntax Parse(string code, SyntaxNode original)
            {
                Changed = true;
                return SourceMap.KeepLineCount(SyntaxFactory.ParseExpression(code).WithTriviaFrom(original), original);
            }

            private static string Text(SyntaxNode node) => node.ToString();

            private static bool IsTuple(ITypeSymbol type) => type is INamedTypeSymbol n && n.IsTupleType;

            /// <summary>タプルのクラス。作れないとき：型引数待ち（1 回目）なら deferred = true、そうでなければエラーを出す。どちらも null</summary>
            private TupleInfo InfoOf(ITypeSymbol type, SyntaxNode at, out bool deferred)
            {
                deferred = false;
                TupleInfo info = _shared.Get((INamedTypeSymbol)type, out string error);
                if (info != null)
                    return info;
                if (_defer)
                    deferred = true;
                else
                    Error(at, error);
                return null;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var symbol = _model.GetDeclaredSymbol(node) as INamedTypeSymbol;
                bool plain = symbol != null && symbol.ContainingType == null && ClassLowering.IsPlainClass(symbol);
                if (plain)
                    _owners.Push(ClassLowering.TagOf(symbol));
                try
                {
                    return base.VisitClassDeclaration(node);
                }
                finally
                {
                    if (plain)
                        _owners.Pop();
                }
            }

            // ---- 式の中での使われ方（文字列・object への変換・要素の型を変える変換） -----------------

            public override SyntaxNode Visit(SyntaxNode node)
            {
                // a.b の b（名前の部分）は、a.b 全体で見る（名前の部分にも、外側の変換が出る）
                if (node is ExpressionSyntax e && !(e is TupleExpressionSyntax)
                    && !(e.Parent is MemberAccessExpressionSyntax ma && ma.Name == e) && !(e.Parent is MemberBindingExpressionSyntax))
                {
                    TypeInfo info = _model.GetTypeInfo(e);
                    if (IsTuple(info.Type) && !_allowed.Contains(e))
                    {
                        if (IsStringContext(e))
                        {
                            TupleInfo tuple = InfoOf(info.Type, e, out bool deferred);
                            if (tuple == null)
                                return deferred ? base.Visit(node) : node;
                            if (tuple.ToStringError != null)
                            {
                                Error(e, $"タプルの文字列への変換は、{tuple.ToStringError}、使えません");
                                return node;
                            }
                            return Parse($"{tuple.Name}.__Str({Text(base.Visit(node))})", node);
                        }
                        // かっこの中の式は、かっこ全体で見る
                        if (!(e.Parent is ParenthesizedExpressionSyntax) && info.ConvertedType != null
                            && !SymbolEqualityComparer.Default.Equals(info.Type, info.ConvertedType) && !SameClass(info.Type, info.ConvertedType))
                        {
                            Error(e, IsTuple(info.ConvertedType)
                                ? "要素の型を変えるタプルの変換は未対応です（要素を取り出して、タプルを作り直してください）"
                                : "タプルを object・インタフェースとして使うことは未対応です（t.ToString() で文字列にするか、要素を渡してください）");
                            return node;
                        }
                    }
                }
                return base.Visit(node);
            }

            /// <summary>名前だけが違うタプル（(int a, int b) と (int, int)）は、同じクラス</summary>
            private bool SameClass(ITypeSymbol a, ITypeSymbol b)
            {
                if (!IsTuple(a) || !IsTuple(b))
                    return false;
                TupleInfo x = _shared.Get((INamedTypeSymbol)a, out _), y = _shared.Get((INamedTypeSymbol)b, out _);
                return x != null && x == y || x == null && y == null && _defer;
            }

            private bool IsStringContext(ExpressionSyntax e)
            {
                switch (e.Parent)
                {
                    case InterpolationSyntax _:
                        return true;
                    case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression):
                        return _model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_String;
                    case AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.AddAssignmentExpression) && a.Right == e:
                        return _model.GetTypeInfo(a.Left).Type?.SpecialType == SpecialType.System_String;
                    default:
                        return false;
                }
            }

            // ---- 型 ------------------------------------------------------------------

            public override SyntaxNode VisitTupleType(TupleTypeSyntax node) => ReplaceType(node) ?? base.VisitTupleType(node);

            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                // Dictionary<(int, int), V>：キーを値で比べられない
                if (node.Identifier.ValueText == "Dictionary" && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol dict
                    && dict.TypeArguments.Length == 2 && IsTuple(dict.TypeArguments[0])
                    && dict.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
                {
                    Error(node, KeyMessage);
                    return node;
                }
                if (node.Parent is QualifiedNameSyntax q && q.Right == node)
                    return base.VisitGenericName(node);   // System.ValueTuple<…> は、外側の名前で置き換える
                return ReplaceType(node) ?? base.VisitGenericName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node) => ReplaceType(node) ?? base.VisitQualifiedName(node);

            public override SyntaxNode VisitNullableType(NullableTypeSyntax node)
            {
                if (IsTuple(_model.GetTypeInfo(node.ElementType).Type))
                {
                    Error(node, "null 許容のタプル（(int, string)?）は未対応です");
                    return node;
                }
                return base.VisitNullableType(node);
            }

            private const string KeyMessage =
                "タプルをキーにする Dictionary は未対応です（C# はキーを値で比べますが、ここでは参照の比較になります）。キーは、数値・string などにしてください";

            /// <summary>タプルの型の表記（(int, string)・ValueTuple&lt;int, string&gt;）→ 生成するクラスの名前。タプルでなければ null</summary>
            private SyntaxNode ReplaceType(TypeSyntax node)
            {
                if (!(_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type) || !type.IsTupleType)
                    return null;
                TupleInfo info = InfoOf(type, node, out bool deferred);
                if (info == null)
                    return deferred ? null : node;
                Changed = true;
                return SourceMap.KeepLineCount(SyntaxFactory.ParseTypeName(info.Name).WithTriviaFrom(node), node);
            }

            // ---- 作る ------------------------------------------------------------------

            public override SyntaxNode VisitTupleExpression(TupleExpressionSyntax node)
            {
                TypeInfo typeInfo = _model.GetTypeInfo(node);
                ITypeSymbol type = IsTuple(typeInfo.ConvertedType) ? typeInfo.ConvertedType : typeInfo.Type;
                // "x" + (1, 2)・$"{(1, 2)}"：文字列にする
                if (IsTuple(typeInfo.Type) && IsStringContext(node))
                {
                    TupleInfo str = InfoOf(typeInfo.Type, node, out bool strDeferred);
                    if (str == null)
                        return strDeferred ? base.VisitTupleExpression(node) : node;
                    if (str.ToStringError != null)
                    {
                        Error(node, $"タプルの文字列への変換は、{str.ToStringError}、使えません");
                        return node;
                    }
                    return Parse($"{str.Name}.__Str(new {str.Name}({string.Join(", ", node.Arguments.Select(x => Text(Visit(x.Expression))))}))", node);
                }
                // Debug.Log((1, 2)) など：タプルの式を object・インタフェースの場所に書いた
                if (IsTuple(typeInfo.Type) && typeInfo.ConvertedType != null && !IsTuple(typeInfo.ConvertedType) && typeInfo.ConvertedType.TypeKind != TypeKind.Error
                    && !_allowed.Contains(node) && !(node.Parent is ParenthesizedExpressionSyntax))
                {
                    Error(node, "タプルを object・インタフェースとして使うことは未対応です（t.ToString() で文字列にするか、要素を渡してください）");
                    return node;
                }
                if (!IsTuple(type))
                {
                    if (typeInfo.ConvertedType != null && typeInfo.ConvertedType.TypeKind != TypeKind.Error && typeInfo.Type != null)
                        Error(node, "タプルを object・インタフェースとして使うことは未対応です（t.ToString() で文字列にするか、要素を渡してください）");
                    else
                        Error(node, "型の決まらないタプル（null だけの要素など）は未対応です。要素の型が分かるように書いてください（(string)null など）");
                    return node;
                }
                TupleInfo info = InfoOf(type, node, out bool deferred);
                if (info == null)
                    return deferred ? base.VisitTupleExpression(node) : node;
                return NewTuple(info, node);
            }

            /// <summary>
            /// (1, x) → new __Tuple_0(1, x)。要素の行を変えない（複数行のタプルの中のエラー・実行時例外が、その要素の行に出るように）。
            /// 要素の名前（x: 1）は外す
            /// </summary>
            private ExpressionSyntax NewTuple(TupleInfo info, TupleExpressionSyntax node)
            {
                var args = node.Arguments.Select(a => SyntaxFactory.Argument(((ExpressionSyntax)Visit(a.Expression)).WithTriviaFrom(a.Expression))
                    .WithLeadingTrivia(a.GetLeadingTrivia()).WithTrailingTrivia(a.GetTrailingTrivia()));
                var list = SyntaxFactory.ArgumentList(node.OpenParenToken, SyntaxFactory.SeparatedList(args, node.Arguments.GetSeparators()), node.CloseParenToken);
                Changed = true;
                return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.ParseTypeName(info.Name), list, null).WithTriviaFrom(node);
            }

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                // new ValueTuple<int, string>(1, "a")・new (int, string)()
                if (!(_model.GetTypeInfo(node).Type is INamedTypeSymbol type) || !type.IsTupleType)
                    return base.VisitObjectCreationExpression(node);
                TupleInfo info = InfoOf(type, node, out bool deferred);
                if (info == null)
                    return deferred ? base.VisitObjectCreationExpression(node) : node;
                var args = node.ArgumentList?.Arguments.Select(a => Text(Visit(a.Expression))).ToList() ?? new List<string>();
                if (args.Count == 0)
                    return Parse($"{info.Name}.__Default()", node);
                if (args.Count != type.TupleElements.Length || node.Initializer != null)
                {
                    Error(node, "この形のタプルの new は未対応です（(1, \"a\") のように書いてください）");
                    return node;
                }
                return Parse($"new {info.Name}({string.Join(", ", args)})", node);
            }

            public override SyntaxNode VisitDefaultExpression(DefaultExpressionSyntax node)
            {
                ITypeSymbol type = _model.GetTypeInfo(node).Type;
                if (IsTuple(type))
                {
                    TupleInfo info = InfoOf(type, node, out bool deferred);
                    if (info == null)
                        return deferred ? base.VisitDefaultExpression(node) : node;
                    return Parse($"{info.Name}.__Default()", node);
                }
                // ジェネリクスのコピーのあと：default(T) の T がタプルだった場所は、default(__Tuple_0)（null）になっている
                if (!_defer && type is INamedTypeSymbol n && n.Name.StartsWith(GeneratedPrefix, StringComparison.Ordinal))
                    return Parse($"{n.Name}.__Default()", node);
                return base.VisitDefaultExpression(node);
            }

            public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.DefaultLiteralExpression) && IsTuple(_model.GetTypeInfo(node).ConvertedType))
                {
                    TupleInfo info = InfoOf(_model.GetTypeInfo(node).ConvertedType, node, out bool deferred);
                    if (info == null)
                        return node;
                    return Parse($"{info.Name}.__Default()", node);
                }
                return base.VisitLiteralExpression(node);
            }

            /// <summary>new (int, string)[n]：要素は、C# と同じく既定値のタプル（null にしない）</summary>
            public override SyntaxNode VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
            {
                if (node.Initializer == null && node.Type.RankSpecifiers.Count == 1 && node.Type.RankSpecifiers[0].Sizes.Count == 1
                    && _model.GetTypeInfo(node).Type is IArrayTypeSymbol array && IsTuple(array.ElementType))
                {
                    TupleInfo info = InfoOf(array.ElementType, node, out bool deferred);
                    if (info == null)
                        return deferred ? base.VisitArrayCreationExpression(node) : node;
                    info.UsesArray = true;
                    return Parse($"{info.Name}.__Array({Text(Visit(node.Type.RankSpecifiers[0].Sizes[0]))})", node);
                }
                return base.VisitArrayCreationExpression(node);
            }

            /// <summary>初期化子のないタプルのフィールド：C# と同じく、各要素が既定値のタプルにする（null にしない）</summary>
            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
                if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.StaticKeyword)))
                    return visited;
                var variables = visited.Declaration.Variables;
                bool changed = false;
                for (int i = 0; i < node.Declaration.Variables.Count; i++)
                {
                    VariableDeclaratorSyntax original = node.Declaration.Variables[i];
                    if (original.Initializer != null || !(_model.GetDeclaredSymbol(original) is IFieldSymbol field) || !IsTuple(field.Type))
                        continue;
                    TupleInfo info = _shared.Get((INamedTypeSymbol)field.Type, out _);
                    if (info == null)
                        continue;
                    VariableDeclaratorSyntax current = variables[i];
                    variables = variables.Replace(current, current.WithIdentifier(current.Identifier.WithTrailingTrivia())
                        .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression($"{info.Name}.__Default()"))
                            .WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(current.Identifier.TrailingTrivia)));
                    changed = true;
                }
                if (!changed)
                    return visited;
                Changed = true;
                return visited.WithDeclaration(visited.Declaration.WithVariables(variables));
            }

            // ---- 要素を読む ------------------------------------------------------------

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                // list[0].Item1 = 2 は C# ではエラー（CS1612）で、シンボルは候補にだけ出る
                SymbolInfo symbolInfo = _model.GetSymbolInfo(node);
                if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is IFieldSymbol field && field.ContainingType != null && field.ContainingType.IsTupleType)
                {
                    if (IsWritten(node))
                    {
                        Error(node, $"'{node.Name}' : タプルの要素への代入は未対応です（C# ではタプルは値型で、代入のたびにコピーされますが、ここでは参照になるため）。タプルを作り直してください（t = (5, t.Item2) など）");
                        return node;
                    }
                    string item = (field.CorrespondingTupleField ?? field).Name;
                    var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);
                    if (item == visited.Name.Identifier.ValueText)
                        return visited;
                    Changed = true;
                    return visited.WithName(SyntaxFactory.IdentifierName(item).WithTriviaFrom(visited.Name));
                }
                return base.VisitMemberAccessExpression(node);
            }

            private static bool IsWritten(ExpressionSyntax e)
            {
                SyntaxNode parent = e.Parent;
                while (parent is ParenthesizedExpressionSyntax p)
                {
                    e = p;
                    parent = p.Parent;
                }
                switch (parent)
                {
                    case AssignmentExpressionSyntax a:
                        return a.Left == e;
                    case PrefixUnaryExpressionSyntax u:
                        return u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression);
                    case PostfixUnaryExpressionSyntax u:
                        return u.IsKind(SyntaxKind.PostIncrementExpression) || u.IsKind(SyntaxKind.PostDecrementExpression);
                    case ArgumentSyntax arg:
                        return arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
                    default:
                        return false;
                }
            }

            // ---- ==・!= ----------------------------------------------------------------

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))
                {
                    ITypeSymbol left = TupleTypeOf(node.Left), right = TupleTypeOf(node.Right);
                    bool leftLiteral = node.Left is TupleExpressionSyntax, rightLiteral = node.Right is TupleExpressionSyntax;
                    if (left != null || right != null || leftLiteral || rightLiteral)
                    {
                        // 片方がタプルの式なら、もう片方の型で作る（(1, null) のように、式だけでは型が決まらないもの・名前だけ違うもの）
                        ITypeSymbol target = !leftLiteral && left != null ? left : !rightLiteral && right != null ? right : left ?? right;
                        if (target == null)
                        {
                            Error(node, "型の決まらないタプル（null だけの要素など）との == は未対応です。要素の型が分かるように書いてください");
                            return node;
                        }
                        TupleInfo a = InfoOf(target, node, out bool deferred);
                        if (a == null)
                            return deferred ? base.VisitBinaryExpression(node) : node;
                        string Side(ExpressionSyntax side, ITypeSymbol type)
                        {
                            if (side is TupleExpressionSyntax literal)
                                return LiteralAs(literal, (INamedTypeSymbol)target);
                            TupleInfo b = type == null ? null : _shared.Get((INamedTypeSymbol)type, out _);
                            if (b != a)
                                return null;
                            _allowed.Add(side);
                            return Text(Visit(side));
                        }
                        string l = Side(node.Left, left), r = Side(node.Right, right);
                        if (l == null || r == null)
                        {
                            Error(node, "要素の型が違うタプルの == は未対応です（要素ごとに比べてください）");
                            return node;
                        }
                        _shared.UseOperator((INamedTypeSymbol)target);
                        string call = $"{a.Name}.__Op({l}, {r})";
                        return Parse(node.IsKind(SyntaxKind.EqualsExpression) ? call : "!" + call, node);
                    }
                }
                return base.VisitBinaryExpression(node);
            }

            /// <summary>
            /// タプルの式を、target の型のタプルとして作るコード（== の相手の型に合わせる）。要素が target の要素の型に暗黙に変換できなければ null
            /// </summary>
            private string LiteralAs(TupleExpressionSyntax literal, INamedTypeSymbol target)
            {
                TupleInfo info = _shared.Get(target, out _);
                var elements = target.TupleElements;
                if (info == null || literal.Arguments.Count != elements.Length)
                    return null;
                var args = new List<string>();
                for (int i = 0; i < elements.Length; i++)
                {
                    ExpressionSyntax arg = literal.Arguments[i].Expression;
                    ITypeSymbol want = elements[i].Type;
                    if (arg is TupleExpressionSyntax inner && want is INamedTypeSymbol wt && wt.IsTupleType)
                    {
                        string nested = LiteralAs(inner, wt);
                        if (nested == null)
                            return null;
                        args.Add(nested);
                        continue;
                    }
                    ITypeSymbol have = _model.GetTypeInfo(arg).Type;
                    bool ok = have == null
                        ? arg.IsKind(SyntaxKind.NullLiteralExpression) && !want.IsValueType || arg.IsKind(SyntaxKind.DefaultLiteralExpression)
                        : _model.Compilation.ClassifyConversion(have, want) is var c && c.IsImplicit && (c.IsIdentity || c.IsReference || c.IsNumeric || c.IsNullLiteral);
                    // 要素の型が違うとき（int と long など）は、C# は広い方に合わせて比べる。ここでは左の型に変換するので、同じ型か、相手の型に広げられるものだけ
                    if (!ok || have != null && have.IsValueType && !SymbolEqualityComparer.Default.Equals(have, want) && !want.IsValueType)
                        return null;
                    if (have != null && !SymbolEqualityComparer.Default.Equals(have, want) && have.IsValueType && want.IsValueType
                        && _model.Compilation.ClassifyConversion(want, have).IsImplicit)
                        return null;   // 相手の方が狭い（long の式と int の要素）：C# は広げて比べるので、狭める変換では同じにならない
                    args.Add(Text(Visit(arg)));
                }
                return $"new {info.Name}({string.Join(", ", args)})";
            }

            /// <summary>== の辺のタプルの型（タプルの式は、要素から決まる型）。タプルでなければ null</summary>
            private ITypeSymbol TupleTypeOf(ExpressionSyntax e)
            {
                TypeInfo info = _model.GetTypeInfo(e);
                return IsTuple(info.Type) ? info.Type : e is TupleExpressionSyntax && IsTuple(info.ConvertedType) ? info.ConvertedType : null;
            }

            // ---- メソッド（ToString・Equals、List・LINQ の値の比較） -----------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var method = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;

                // List<タプル> の Contains / IndexOf / LastIndexOf / Remove と、LINQ の Distinct / Contains：C# と同じく値で比べる（#138 と同じ仕組み）
                if (method != null && AnonymousTypeLowering.ComparesByEquals(method, out ITypeSymbol element) && element is INamedTypeSymbol el
                    && (el.IsTupleType || !_defer && el.Name.StartsWith(GeneratedPrefix, StringComparison.Ordinal)))
                {
                    if (el.IsTupleType)
                    {
                        TupleInfo info = _shared.Get(el, out _);
                        if (info == null && _defer)
                            return base.VisitInvocationExpression(node);
                        if (info != null && info.EqualsError == null
                            && AnonymousTypeLowering.ListComparisonCode(node, method, _model, info.Name, info.ListHelpers, n => Text(Visit(n)), ref _containsCount) is string code)
                            return Parse(code, node);
                    }
                    Error(node, method.Name == "ToDictionary"
                        ? KeyMessage
                        : $"'{method.Name}' : タプルの要素を値（Equals）で比べるこの形は未対応です（リストの比較は参照になります）。Exists / FindIndex / Any / Where などのラムダで比べてください");
                    return node;
                }

                // FirstOrDefault・LastOrDefault・SingleOrDefault・ElementAtOrDefault：要素がなければ、C# と同じく既定値のタプル（null にしない）
                if (method != null && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == "System.Linq.Enumerable"
                    && method.Name.EndsWith("OrDefault", StringComparison.Ordinal) && IsTuple(method.ReturnType) && method.Parameters.All(pa => !IsTuple(pa.Type)))
                {
                    TupleInfo info = InfoOf(method.ReturnType, node, out bool deferred);
                    if (info == null)
                        return deferred ? base.VisitInvocationExpression(node) : node;
                    return Parse($"{info.Name}.__OrDefault({Text(base.VisitInvocationExpression(node))})", node);
                }

                // List<タプル>.Sort()・BinarySearch：タプルの大小の比較（IComparable）は未対応
                if (method != null && method.ContainingType != null && method.ContainingType.Name == "List" && method.ContainingType.TypeArguments.Length == 1
                    && IsTuple(method.ContainingType.TypeArguments[0]) && (method.Name == "Sort" && method.Parameters.Length == 0 || method.Name == "BinarySearch"))
                {
                    Error(node, $"'{method.Name}' : タプルの大小の比較は未対応です（list.Sort((a, b) => ...) のように、比べ方を渡してください）");
                    return node;
                }

                if (method != null && node.Expression is MemberAccessExpressionSyntax access && IsTuple(_model.GetTypeInfo(access.Expression).Type)
                    && (method.ContainingType.IsTupleType || method.ContainingType.SpecialType == SpecialType.System_Object || method.ContainingType.SpecialType == SpecialType.System_ValueType))
                {
                    ITypeSymbol receiverType = _model.GetTypeInfo(access.Expression).Type;
                    TupleInfo info = InfoOf(receiverType, node, out bool deferred);
                    if (info == null)
                        return deferred ? base.VisitInvocationExpression(node) : node;
                    _allowed.Add(access.Expression);
                    int count = node.ArgumentList.Arguments.Count;
                    if (method.Name == "ToString" && count == 0)
                    {
                        if (info.ToStringError != null)
                        {
                            Error(node, $"タプルの ToString は、{info.ToStringError}、使えません");
                            return node;
                        }
                        return Parse($"{Text(Visit(access.Expression))}.__ToString()", node);
                    }
                    if (method.Name == "Equals" && count == 1)
                    {
                        if (info.EqualsError != null)
                        {
                            Error(node, $"タプルの Equals は、{info.EqualsError}、使えません");
                            return node;
                        }
                        ExpressionSyntax argument = node.ArgumentList.Arguments[0].Expression;
                        _allowed.Add(argument);
                        return Parse($"{Text(Visit(access.Expression))}.__Equals({Text(Visit(argument))})", node);
                    }
                    Error(node, $"'{method.Name}' : タプルの {method.Name} は未対応です（ToString・Equals・==・要素の読み出しが使えます）");
                    return node;
                }
                return base.VisitInvocationExpression(node);
            }

            // ---- switch・パターン -------------------------------------------------------

            public override SyntaxNode VisitSwitchStatement(SwitchStatementSyntax node)
            {
                if (node.Expression is TupleExpressionSyntax || IsTuple(_model.GetTypeInfo(node.Expression).Type))
                {
                    Error(node.Expression, "タプルの switch は未対応です（if で、要素ごとに比べてください）");
                    return node;
                }
                return base.VisitSwitchStatement(node);
            }

            public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
            {
                if (node.Expression is TupleExpressionSyntax || IsTuple(_model.GetTypeInfo(node.Expression).Type))
                {
                    Error(node, "タプルのパターン（is）は未対応です（要素ごとに比べてください）");
                    return node;
                }
                return base.VisitIsPatternExpression(node);
            }

            public override SyntaxNode VisitSwitchExpression(SwitchExpressionSyntax node)
            {
                if (node.GoverningExpression is TupleExpressionSyntax || IsTuple(_model.GetTypeInfo(node.GoverningExpression).Type))
                {
                    Error(node.GoverningExpression, "タプルの switch は未対応です（if で、要素ごとに比べてください）");
                    return node;
                }
                return base.VisitSwitchExpression(node);
            }

            // ---- 分解 ------------------------------------------------------------------

            private static bool IsDeconstructionTarget(ExpressionSyntax left) =>
                left is TupleExpressionSyntax
                || left is DeclarationExpressionSyntax d && d.Designation is ParenthesizedVariableDesignationSyntax;

            private static bool IsDeconstruction(StatementSyntax statement) =>
                statement is ExpressionStatementSyntax es && es.Expression is AssignmentExpressionSyntax a
                && a.IsKind(SyntaxKind.SimpleAssignmentExpression) && IsDeconstructionTarget(a.Left);

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                if (!node.Statements.Any(IsDeconstruction))
                    return base.VisitBlock(node);
                // 分解の文は、同じ行に並べた複数の文にする（宣言した変数が、ブロックの中でそのまま見えるように、ブロックにはしない）
                var statements = new List<StatementSyntax>();
                foreach (StatementSyntax statement in node.Statements)
                {
                    if (IsDeconstruction(statement))
                        statements.AddRange(Deconstruct((ExpressionStatementSyntax)statement) ?? new List<StatementSyntax> { statement });
                    else
                        statements.Add((StatementSyntax)Visit(statement));
                }
                Changed = true;
                return node.WithStatements(SyntaxFactory.List(statements));
            }

            public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
            {
                if (!node.Statements.Any(IsDeconstruction))
                    return base.VisitSwitchSection(node);
                var statements = new List<StatementSyntax>();
                foreach (StatementSyntax statement in node.Statements)
                {
                    if (IsDeconstruction(statement))
                        statements.AddRange(Deconstruct((ExpressionStatementSyntax)statement) ?? new List<StatementSyntax> { statement });
                    else
                        statements.Add((StatementSyntax)Visit(statement));
                }
                Changed = true;
                return node.WithLabels(VisitList(node.Labels)).WithStatements(SyntaxFactory.List(statements));
            }

            /// <summary>ブロックの直下でない分解の文（if (c) (a, b) = t;）：ブロックにする</summary>
            public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
            {
                if (!IsDeconstruction(node))
                    return base.VisitExpressionStatement(node);
                List<StatementSyntax> parts = Deconstruct(node);
                if (parts == null)
                    return node;
                SyntaxTriviaList leading = parts[0].GetLeadingTrivia(), trailing = parts[parts.Count - 1].GetTrailingTrivia();
                parts[0] = parts[0].WithLeadingTrivia(SyntaxFactory.Space);
                parts[parts.Count - 1] = parts[parts.Count - 1].WithTrailingTrivia(SyntaxFactory.Space);
                Changed = true;
                return SyntaxFactory.Block(parts).WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
            }

            /// <summary>文でない場所の分解（式の値として使う・for の初期化など）はエラー</summary>
            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.SimpleAssignmentExpression) && IsDeconstructionTarget(node.Left))
                {
                    Error(node, "タプルの分解は、1 つの文として書いてください（式の中・for の初期化・式の本体のラムダの中では未対応です）");
                    return node;
                }
                return base.VisitAssignmentExpression(node);
            }

            /// <summary>
            /// var (x, y) = e; ／ (a, b) = e; → __Tuple_0 __td0 = e; int x = __td0.Item1; string y = __td0.Item2;（同じ行に並べる）。
            /// 右辺は 1 回だけ評価し、全部を取り出してから代入する（(a, b) = (b, a) で入れ替わる）。だめなら null（エラーは出し済み）
            /// </summary>
            private List<StatementSyntax> Deconstruct(ExpressionStatementSyntax statement)
            {
                var assign = (AssignmentExpressionSyntax)statement.Expression;
                TypeInfo info = _model.GetTypeInfo(assign.Right);
                ITypeSymbol type = IsTuple(info.Type) ? info.Type : IsTuple(info.ConvertedType) ? info.ConvertedType : null;
                if (type == null)
                {
                    Error(assign, "タプル以外の分解（Deconstruct メソッド・KeyValuePair など）は未対応です");
                    return null;
                }
                TupleInfo tuple = InfoOf(type, assign, out bool deferred);
                if (tuple == null)
                {
                    if (deferred)
                        Error(assign, "型引数を使うタプルの分解は未対応です（要素を Item1 などで読んでください）");
                    return null;
                }
                var sb = new StringBuilder();
                string temp = $"__td{_tempCount++}";
                // C# と同じく、受け側の場所（添字・受け側のオブジェクト）を左から先に評価してから、右辺を評価する
                var pre = new StringBuilder();
                _pre = pre;
                bool ok;
                try
                {
                    ok = Targets(assign.Left, (INamedTypeSymbol)type, temp, sb);
                }
                finally
                {
                    _pre = null;
                }
                if (!ok)
                    return null;
                sb.Insert(0, $"{pre}{tuple.Name} {temp} = {Text(Visit(assign.Right))}; ");

                var block = (BlockSyntax)SyntaxFactory.ParseStatement("{ " + sb + "}");
                var parts = block.Statements.Select(s => s.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia()).ToList();
                // 行の数を保つ：元の文の中の改行のうち、右辺に残らなかった分を、最後に足す
                int missing = Lines(statement.ToString()) - Lines(string.Concat(parts.Select(p => p.ToFullString())));
                var trailing = new List<SyntaxTrivia>();
                for (int i = 0; i < missing; i++)
                    trailing.Add(SyntaxFactory.EndOfLine("\n"));
                trailing.AddRange(statement.GetTrailingTrivia());
                parts[0] = parts[0].WithLeadingTrivia(statement.GetLeadingTrivia());
                parts[parts.Count - 1] = parts[parts.Count - 1].WithTrailingTrivia(trailing);
                return parts;
            }

            private static int Lines(string text) => text.Count(c => c == '\n');

            /// <summary>分解の受け側（入れ子も）を、source（タプルの式）の要素の代入・宣言の文にして足す</summary>
            private bool Targets(ExpressionSyntax left, INamedTypeSymbol type, string source, StringBuilder sb)
            {
                var elements = type.TupleElements;
                // var (x, y)：全部 var の宣言
                if (left is DeclarationExpressionSyntax declaration && declaration.Designation is ParenthesizedVariableDesignationSyntax all)
                    return Designations(all, type, source, sb);
                if (!(left is TupleExpressionSyntax targets) || targets.Arguments.Count != elements.Length)
                {
                    Error(left, "この形の分解は未対応です");
                    return false;
                }
                for (int i = 0; i < elements.Length; i++)
                {
                    ExpressionSyntax target = targets.Arguments[i].Expression;
                    string item = $"{source}.Item{i + 1}";
                    switch (target)
                    {
                        case DeclarationExpressionSyntax d when d.Designation is SingleVariableDesignationSyntax single:
                            sb.Append($"{(d.Type.IsVar ? _shared.TypeText(elements[i].Type) : Text(Visit(d.Type)))} {single.Identifier.Text} = {item}; ");
                            break;
                        case DeclarationExpressionSyntax d when d.Designation is DiscardDesignationSyntax:
                            break;
                        case DeclarationExpressionSyntax d when d.Designation is ParenthesizedVariableDesignationSyntax nested && elements[i].Type is INamedTypeSymbol nt && nt.IsTupleType:
                            if (!Designations(nested, nt, item, sb))
                                return false;
                            break;
                        case TupleExpressionSyntax nestedTuple when elements[i].Type is INamedTypeSymbol nt && nt.IsTupleType:
                            if (!Targets(nestedTuple, nt, item, sb))
                                return false;
                            break;
                        case IdentifierNameSyntax id when id.Identifier.ValueText == "_" && IsDiscard(id):
                            break;   // 捨てる（_）
                        default:
                            if (target is DeclarationExpressionSyntax || target is TupleExpressionSyntax)
                            {
                                Error(target, "この形の分解は未対応です");
                                return false;
                            }
                            sb.Append($"{Location(target)} = {item}; ");
                            break;
                    }
                }
                return true;
            }

            private StringBuilder _pre;   // 分解の受け側の場所を、右辺より先に評価する文（Deconstruct の中だけ）

            /// <summary>
            /// 分解の受け側（a[i]・obj.f）の場所。添字・受け側のオブジェクトに副作用があり得るなら、一時変数に入れる文を _pre に足して、その変数で書く
            /// </summary>
            private string Location(ExpressionSyntax target)
            {
                string Hoist(ExpressionSyntax e)
                {
                    if (_pre == null || IsSimple(e))
                        return Text(Visit(e));
                    string name = $"__tl{_tempCount++}";
                    _pre.Append($"var {name} = {Text(Visit(e))}; ");
                    return name;
                }
                switch (target)
                {
                    case ElementAccessExpressionSyntax element:
                        return $"{Hoist(element.Expression)}[{string.Join(", ", element.ArgumentList.Arguments.Select(a => Hoist(a.Expression)))}]";
                    case MemberAccessExpressionSyntax member when !(member.Expression is ThisExpressionSyntax) && !(_model.GetSymbolInfo(member.Expression).Symbol is INamedTypeSymbol):
                        return $"{Hoist(member.Expression)}.{member.Name}";
                    default:
                        return Text(Visit(target));
                }
            }

            /// <summary>評価しても副作用がなく、何回読んでも同じ式（ローカル変数・引数・this・リテラル）</summary>
            private bool IsSimple(ExpressionSyntax e) =>
                e is LiteralExpressionSyntax || e is ThisExpressionSyntax
                || e is IdentifierNameSyntax && _model.GetSymbolInfo(e).Symbol is ISymbol sym && (sym is ILocalSymbol || sym is IParameterSymbol);

            private bool IsDiscard(IdentifierNameSyntax id)
            {
                ISymbol symbol = _model.GetSymbolInfo(id).Symbol;
                return !(symbol is ILocalSymbol || symbol is IParameterSymbol || symbol is IFieldSymbol || symbol is IPropertySymbol);
            }

            private bool Designations(ParenthesizedVariableDesignationSyntax designation, INamedTypeSymbol type, string source, StringBuilder sb)
            {
                var elements = type.TupleElements;
                if (designation.Variables.Count != elements.Length)
                {
                    Error(designation, "この形の分解は未対応です");
                    return false;
                }
                for (int i = 0; i < elements.Length; i++)
                {
                    string item = $"{source}.Item{i + 1}";
                    switch (designation.Variables[i])
                    {
                        case SingleVariableDesignationSyntax single:
                            sb.Append($"{_shared.TypeText(elements[i].Type)} {single.Identifier.Text} = {item}; ");
                            break;
                        case DiscardDesignationSyntax _:
                            break;
                        case ParenthesizedVariableDesignationSyntax nested when elements[i].Type is INamedTypeSymbol nt && nt.IsTupleType:
                            if (!Designations(nested, nt, item, sb))
                                return false;
                            break;
                        default:
                            Error(designation, "この形の分解は未対応です");
                            return false;
                    }
                }
                return true;
            }

            /// <summary>foreach (var (k, v) in list) → foreach (__Tuple_0 __td0 in list) { int k = __td0.Item1; ... 本体 }</summary>
            public override SyntaxNode VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
            {
                ITypeSymbol element = _model.GetForEachStatementInfo(node).ElementType;
                if (!IsTuple(element))
                {
                    Error(node.Variable, "タプル以外の分解（Deconstruct メソッド・KeyValuePair など）は未対応です（foreach (var kv in dict) で、kv.Key／kv.Value を使ってください）");
                    return node;
                }
                TupleInfo tuple = InfoOf(element, node.Variable, out bool deferred);
                if (tuple == null)
                {
                    if (deferred)
                        Error(node.Variable, "型引数を使うタプルの分解は未対応です（要素を Item1 などで読んでください）");
                    return node;
                }
                var sb = new StringBuilder();
                string temp = $"__td{_tempCount++}";
                if (!Targets(node.Variable, (INamedTypeSymbol)element, temp, sb))
                    return node;
                var declarations = ((BlockSyntax)SyntaxFactory.ParseStatement("{ " + sb + "}")).Statements
                    .Select(s => s.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia()).ToList();

                var body = (StatementSyntax)Visit(node.Statement);
                BlockSyntax block;
                if (body is BlockSyntax existing)
                {
                    // 宣言は { のすぐあとに、同じ行で置く
                    var statements = existing.Statements.ToList();
                    if (statements.Count > 0)
                    {
                        SyntaxTriviaList lead = statements[0].GetLeadingTrivia();
                        statements[0] = statements[0].WithLeadingTrivia(SyntaxFactory.Space);
                        declarations[0] = declarations[0].WithLeadingTrivia(lead);
                    }
                    block = existing.WithStatements(SyntaxFactory.List(declarations.Concat(statements)));
                }
                else
                {
                    SyntaxTriviaList lead = body.GetLeadingTrivia(), trail = body.GetTrailingTrivia();
                    declarations.Add(body.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space));
                    block = SyntaxFactory.Block(declarations).WithLeadingTrivia(lead).WithTrailingTrivia(trail);
                }
                Changed = true;
                // 行の数を保つ：変数の並び（var (k, v)）の中の改行は、in の前に足す
                int missing = Lines(node.Variable.ToFullString());
                string pad = new string('\n', missing);
                ForEachStatementSyntax result = SyntaxFactory.ForEachStatement(
                    SyntaxFactory.ParseTypeName(tuple.Name).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.Identifier(temp).WithTrailingTrivia(SyntaxFactory.Whitespace(" " + pad)),
                    (ExpressionSyntax)Visit(node.Expression),
                    block)
                    .WithForEachKeyword(node.ForEachKeyword).WithOpenParenToken(node.OpenParenToken)
                    .WithInKeyword(node.InKeyword).WithCloseParenToken(node.CloseParenToken);
                return result;
            }
        }
    }
}
