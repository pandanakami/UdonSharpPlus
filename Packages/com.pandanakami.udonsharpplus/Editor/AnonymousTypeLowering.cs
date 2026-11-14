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
    /// 匿名型（new { Name = "a", Score = 1 }。Redmine #70）。UdonSharp は対応していないので、メンバーの並び（名前と型）ごとに
    /// 普通のクラスを 1 つ作り、new { ... } をそのクラスの new にする。あとは普通のクラスの段（ClassLowering）が扱う。
    ///
    ///   new { Name = "a", Score = 1 }  →  new __Anon_0("a", 1)
    ///   public class __Anon_0 { public string Name; public int Score; public __Anon_0(string Name, int Score) { ... }
    ///                           public string __ToString() ...; public bool __Equals(object o) ... }   （最初に使ったファイルの末尾に 1 行で置く）
    ///
    /// - var で受ける・メンバーを読む・ラムダの戻り値にする・入れ子の匿名型・メンバー名の省略（new { x.Name }）は、型の推論でそのまま動く
    /// - 匿名型は C# では値で比べ、ToString で「{ Name = a, Score = 1 }」と出す。普通のクラスは ToString・Equals を override できないので、
    ///   p.ToString() → p.__ToString()、p.Equals(q) → p.__Equals(q)、文字列の連結・補間（"x" + p、$"{p}"）→ __Anon_0.__Str(p) に書き換える
    /// - object・インタフェースにするほかの変換（Debug.Log(p)、object o = p など）・GetHashCode・GetType は、エラーにする
    ///   （object[] のままだと、C# と違う結果になるため）
    /// - 2 回動く。ジェネリクス（#59）の前（匿名型を型引数にするジェネリックメソッドの呼び出し Id(new { A = 1 }) のため。型引数を使う匿名型は
    ///   このときは書き換えない）と、ジェネリクスのコピーのあと、デリゲート（#69）の前（型引数が決まった匿名型）
    /// - 行は増やさない（生成するクラスは 1 行）
    /// </summary>
    internal static class AnonymousTypeLowering
    {
        /// <summary>生成するクラスの名前の先頭（__Anon_0 など）</summary>
        internal const string GeneratedPrefix = "__Anon_";

        public static bool MayHaveAnonymousTypes(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is AnonymousObjectCreationExpressionSyntax));

        /// <summary>
        /// counter は、生成したクラスの数（名前の番号。2 回目は 1 回目の続き）。beforeGenerics が true なら、型引数を使う匿名型は書き換えずに残す
        /// </summary>
        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references,
            ref int counter, bool beforeGenerics)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_AnonymousPrepass",
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
            foreach (var group in shared.Infos.Where(i => i.Code != null).GroupBy(i => i.HomeTree))
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
                    members.Add(SyntaxFactory.ParseMemberDeclaration(infos[k].Code)
                        .WithLeadingTrivia(SyntaxFactory.Space)
                        .WithTrailingTrivia(k == infos.Count - 1 ? trailing : SyntaxFactory.TriviaList()));
                result[group.Key] = Reparse(unit.WithMembers(SyntaxFactory.List(members)), tree);
            }
            return result;
        }

        private static SyntaxTree Reparse(SyntaxNode root, SyntaxTree original) =>
            CSharpSyntaxTree.ParseText(root.ToFullString(), (CSharpParseOptions)original.Options, original.FilePath, Encoding.UTF8);

        /// <summary>匿名型 1 つ（メンバーの名前と型の並びが同じものは 1 つ）</summary>
        private sealed class AnonInfo
        {
            public string Name;
            public int HomeTree;
            public string Code;
            public string ToStringError;   // ToString・文字列の連結に使えない理由（null なら使える）
            public string EqualsError;     // Equals に使えない理由
        }

        private sealed class Shared
        {
            public int CurrentTree;
            private readonly int _start;

            public Shared(int start) { _start = start; }

            public readonly List<AnonInfo> Infos = new List<AnonInfo>();
            private readonly Dictionary<string, AnonInfo> _byKey = new Dictionary<string, AnonInfo>(StringComparer.Ordinal);
            private readonly Dictionary<ITypeSymbol, string> _unsupported = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

            private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;
            private static readonly SymbolDisplayFormat QualifiedNoGenerics = SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

            /// <summary>匿名型に対応するクラス。作れなければ null と理由</summary>
            public AnonInfo Get(INamedTypeSymbol anon, out string error)
            {
                error = null;
                var props = anon.GetMembers().OfType<IPropertySymbol>().ToList();
                foreach (IPropertySymbol p in props)
                {
                    string why = Unsupported(p.Type);
                    if (why != null)
                    {
                        error = $"'{p.Name}' : 匿名型のメンバーに、{why}は使えません";
                        return null;
                    }
                }

                // 入れ子の匿名型は、先に作る（名前を決めるため）
                var types = props.Select(p => TypeText(p.Type)).ToList();
                string key = string.Join(";", props.Select((p, i) => p.Name + ":" + types[i]));
                if (_byKey.TryGetValue(key, out AnonInfo existing))
                    return existing;

                var info = new AnonInfo { Name = GeneratedPrefix + (_start + Infos.Count), HomeTree = CurrentTree };
                _byKey[key] = info;
                Infos.Add(info);
                Generate(info, props, types);
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
                        foreach (ITypeSymbol arg in n.TypeArguments)
                        {
                            string why = Unsupported(arg);
                            if (why != null)
                                return why;
                        }
                        if (n.IsAnonymousType)
                            foreach (IPropertySymbol p in n.GetMembers().OfType<IPropertySymbol>())
                            {
                                string why = Unsupported(p.Type);
                                if (why != null)
                                    return why;
                            }
                        return null;
                    default:
                        return null;
                }
            }

            /// <summary>生成するコードに書く型（匿名型は生成するクラスの名前にする）</summary>
            public string TypeText(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                if (type is INamedTypeSymbol named)
                {
                    if (named.IsAnonymousType)
                        return Get(named, out _)?.Name ?? "object";
                    if (named.IsGenericType && named.TypeArguments.Any(ContainsAnonymous))
                        return named.OriginalDefinition.ToDisplayString(QualifiedNoGenerics) + "<" + string.Join(", ", named.TypeArguments.Select(TypeText)) + ">";
                }
                return StripGlobalAlias(type.ToDisplayString(Qualified));
            }

            /// <summary>
            /// グローバル名前空間の型（global::Holder）は、global:: を外す。global:: の直後に型の名前だけが来る形（別名つきの名前）は、
            /// インタフェースの段が書き換えるときに型の変換で例外になるため。生成するクラスもグローバル名前空間なので、名前だけで同じ型を指す
            /// </summary>
            private static string StripGlobalAlias(string text) =>
                System.Text.RegularExpressions.Regex.Replace(text, @"global::([A-Za-z_]\w*)(?![\w.])", "$1");

            private static bool ContainsAnonymous(ITypeSymbol type) =>
                type is IArrayTypeSymbol a ? ContainsAnonymous(a.ElementType)
                : type is INamedTypeSymbol n && (n.IsAnonymousType || n.IsGenericType && n.TypeArguments.Any(ContainsAnonymous));

            /// <summary>
            /// ToString の中の、メンバーの値の書き方。C# の ToString と同じ結果になるものだけ（そうでなければ null）：
            /// 数値・bool・char・string・外部の構造体（Vector3 など）は連結、匿名型は __Str
            /// </summary>
            private string ToStringOf(ITypeSymbol type, string expression)
            {
                if (type is INamedTypeSymbol named)
                {
                    if (named.IsAnonymousType)
                        return Get(named, out _) is AnonInfo inner && inner.ToStringError == null ? $"{inner.Name}.__Str({expression})" : null;
                    if (named.TypeKind == TypeKind.Struct && !named.Locations.Any(l => l.IsInSource) && named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
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
                    case SpecialType.System_Decimal:
                    case SpecialType.System_String:
                        return expression;
                    default:
                        return null;
                }
            }

            private void Generate(AnonInfo info, List<IPropertySymbol> props, List<string> types)
            {
                string name = info.Name;
                var sb = new StringBuilder();
                sb.Append($"public class {name} {{ ");
                for (int i = 0; i < props.Count; i++)
                    sb.Append($"public {types[i]} {Ident(props[i].Name)}; ");
                sb.Append($"public {name}({string.Join(", ", props.Select((p, i) => $"{types[i]} {Ident(p.Name)}"))}) {{ ");
                foreach (IPropertySymbol p in props)
                    sb.Append($"this.{Ident(p.Name)} = {Ident(p.Name)}; ");
                sb.Append("} ");

                // ToString：{ Name = a, Score = 1 }
                var parts = new List<string>();
                foreach (IPropertySymbol p in props)
                {
                    string value = ToStringOf(p.Type, Ident(p.Name));
                    if (value == null)
                    {
                        info.ToStringError = $"メンバー '{p.Name}' の型（{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}）の文字列を、C# と同じにできないため";
                        break;
                    }
                    parts.Add($"\"{(parts.Count == 0 ? "" : ", ")}{p.Name} = \" + {value}");
                }
                if (info.ToStringError == null)
                {
                    // 最初に呼び出しを置く（普通のクラスの段は、this のフィールドを読む前に呼び出しがあるメソッドを、呼び出した行で null かを調べるようにする。
                    // null の匿名型の ToString が、呼び出した行で止まる）
                    string body = props.Count == 0 ? "\"{ }\"" : "__r + " + string.Join(" + ", parts) + " + \" }\"";
                    string first = props.Count == 0 ? "string __r = \"\".ToString();" : "string __r = \"{ \".ToString();";
                    sb.Append($"public string __ToString() {{ {first} return {body}; }} ");
                    sb.Append($"public static string __Str({name} a) {{ return a == null ? \"\" : a.__ToString(); }} ");
                }

                // Equals：C# と同じく、メンバーごとに値で比べる（匿名型のメンバーは入れ子で比べる。デリゲートは比べ方が違うので使えない）
                var compares = new List<string>();
                foreach (IPropertySymbol p in props)
                {
                    if (p.Type is INamedTypeSymbol d && d.TypeKind == TypeKind.Delegate)
                    {
                        info.EqualsError = $"メンバー '{p.Name}' がデリゲートで、比べ方が C# と違うため";
                        break;
                    }
                    compares.Add(p.Type is INamedTypeSymbol a && a.IsAnonymousType && Get(a, out _) is AnonInfo inner
                        ? $"{inner.Name}.__Eq({Ident(p.Name)}, b.{Ident(p.Name)})"
                        : $"__E({Ident(p.Name)}, b.{Ident(p.Name)})");
                }
                if (info.EqualsError == null)
                {
                    sb.Append($"public bool __Equals(object o) {{ {name} b = o as {name}; if (b == null) return false; return {(compares.Count == 0 ? "true" : string.Join(" && ", compares))}; }} ");
                    sb.Append($"public static bool __Eq({name} a, {name} b) {{ if (a == null) return b == null; return a.__Equals(b); }} ");
                    sb.Append("public static bool __E(object x, object y) { if (x == null) return y == null; return x.Equals(y); } ");
                }
                sb.Append("}");
                info.Code = sb.ToString();
            }

            /// <summary>
            /// 生成するクラスのフィールド名。C# のキーワードと同じ名前（@class など）は、普通のクラスの段が @ を落としてしまうので、別の名前にする
            /// （読み出し側も、Rewriter が同じ名前に直す）
            /// </summary>
            internal static string Ident(string name) =>
                SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "__kw_" + name : name;
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly Shared _shared;
            private readonly SemanticModel _model;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly Stack<string> _owners = new Stack<string>();   // 普通のクラスの中なら、そのクラスの型タグ（使われないクラスのエラーは捨てる）
            private readonly HashSet<SyntaxNode> _allowed = new HashSet<SyntaxNode>();   // object への変換を、書き換えて扱うところ
            private readonly bool _defer;   // ジェネリクスの前：型引数を使う匿名型は、あとの回に回す

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

            /// <summary>要素を Equals で比べるメソッド（List の Contains・IndexOf・LastIndexOf・Remove、LINQ の Distinct・Contains）か。element は比べる要素の型</summary>
            private static bool ComparesByEquals(IMethodSymbol m, out ITypeSymbol element)
            {
                element = null;
                INamedTypeSymbol owner = m.ContainingType;
                if (owner == null)
                    return false;
                if (owner.Name == "List" && owner.IsGenericType && owner.TypeArguments.Length == 1
                    && (m.Name == "Contains" || m.Name == "IndexOf" || m.Name == "LastIndexOf" || m.Name == "Remove"))
                {
                    element = owner.TypeArguments[0];
                    return true;
                }
                if (owner.Name == "Enumerable" && m.TypeArguments.Length == 1 && (m.Name == "Distinct" || m.Name == "Contains"))
                {
                    element = m.TypeArguments[0];
                    return true;
                }
                // ToDictionary：キーの型（TKey は 2 つ目の型引数）
                if (owner.Name == "Enumerable" && m.Name == "ToDictionary" && m.TypeArguments.Length >= 2)
                {
                    element = m.TypeArguments[1];
                    return true;
                }
                return false;
            }

            private static bool IsAnonymous(ITypeSymbol type) => type is INamedTypeSymbol n && n.IsAnonymousType;

            /// <summary>
            /// 匿名型の式が、匿名型のまま使われていないところ（object への変換）。文字列の連結・補間なら __Str にし、
            /// 参照の比較（==・!=）は object[] でも同じなのでそのまま。それ以外はエラー
            /// </summary>
            public override SyntaxNode Visit(SyntaxNode node)
            {
                if (node is ExpressionSyntax e)
                {
                    TypeInfo info = _model.GetTypeInfo(e);
                    if (IsAnonymous(info.Type) && !_allowed.Contains(e))
                    {
                        var type = (INamedTypeSymbol)info.Type;
                        // 文字列の連結・補間は、演算子の引数の変換が型の情報に出ないので、書き方から見つける
                        if (IsStringContext(e))
                        {
                            AnonInfo anon = _shared.Get(type, out string error);
                            if (anon == null)
                            {
                                if (_defer)
                                    return base.Visit(node);
                                Error(e, error);
                                return node;
                            }
                            if (anon.ToStringError != null)
                            {
                                Error(e, $"匿名型の文字列への変換は、{anon.ToStringError}、使えません");
                                return node;
                            }
                            return Parse($"{anon.Name}.__Str({Text(base.Visit(node))})", node);
                        }
                        // かっこの中の式は、かっこ全体で見る（かっこの中の式にも、外側の変換が出る）
                        if (!(e.Parent is ParenthesizedExpressionSyntax) && info.ConvertedType != null
                            && !SymbolEqualityComparer.Default.Equals(info.Type, info.ConvertedType) && !IsReferenceComparison(e))
                        {
                            Error(e, "匿名型を object・インタフェースとして使うことは未対応です（p.ToString() で文字列にするか、メンバーを渡してください）");
                            return node;
                        }
                    }
                }
                return base.Visit(node);
            }

            private bool IsReferenceComparison(ExpressionSyntax e)
            {
                SyntaxNode parent = e.Parent;
                while (parent is ParenthesizedExpressionSyntax)
                    parent = parent.Parent;
                return parent is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression))
                    || parent is IsPatternExpressionSyntax;
            }

            /// <summary>"x" + p、$"{p}"、s += p のように、文字列にして使うところ</summary>
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

            /// <summary>キーワードと同じ名前のメンバー（p.@class）は、生成するクラスのフィールドの名前にする</summary>
            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node);
                if (_model.GetSymbolInfo(node).Symbol is IPropertySymbol prop && prop.ContainingType != null && prop.ContainingType.IsAnonymousType
                    && Shared.Ident(prop.Name) != prop.Name)
                {
                    Changed = true;
                    return visited.WithName(SyntaxFactory.IdentifierName(Shared.Ident(prop.Name)).WithTriviaFrom(visited.Name));
                }
                return visited;
            }

            public override SyntaxNode VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
            {
                if (!(_model.GetTypeInfo(node).Type is INamedTypeSymbol type) || !type.IsAnonymousType)
                    return base.VisitAnonymousObjectCreationExpression(node);
                AnonInfo info = _shared.Get(type, out string error);
                if (info == null)
                {
                    if (_defer)
                        return base.VisitAnonymousObjectCreationExpression(node);
                    Error(node, error);
                    return node;
                }
                IEnumerable<string> args = node.Initializers.Select(i => Text(Visit(i.Expression)));
                return Parse($"new {info.Name}({string.Join(", ", args)})", node);
            }

            /// <summary>p.ToString()・p.Equals(q)。そのほかの object のメソッド（GetHashCode・GetType など）はエラー</summary>
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // List<匿名型> の Contains / IndexOf / LastIndexOf / Remove と、LINQ の Distinct / Contains：
                // C# は値（Equals）で比べるが、リストの比較は参照なので結果が変わる。黙って違う結果にせず、エラーにする
                if (_model.GetSymbolInfo(node).Symbol is IMethodSymbol cmp && ComparesByEquals(cmp, out ITypeSymbol element)
                    && element is INamedTypeSymbol anonElement
                    && (anonElement.IsAnonymousType && (!_defer || _shared.Get(anonElement, out _) != null)
                        // ジェネリクスのコピーのあと：型引数が匿名型だった場所（List<T>.Contains(x) の T）は、生成したクラスになっている
                        || !_defer && anonElement.Name.StartsWith(GeneratedPrefix, System.StringComparison.Ordinal)))
                {
                    Error(node, cmp.Name == "ToDictionary"
                        ? "'ToDictionary' : 匿名型をキーにする Dictionary は未対応です（C# はキーを値（Equals）で比べますが、ここでは参照の比較になります）。キーは、数値・string などのメンバーにしてください"
                        : $"'{cmp.Name}' : 匿名型の要素を値（Equals）で比べるメソッドは未対応です（リストの比較は参照になります）。Exists / FindIndex / Any / Where などで、メンバーを比べてください");
                    return node;
                }
                if (node.Expression is MemberAccessExpressionSyntax access
                    && _model.GetTypeInfo(access.Expression).Type is INamedTypeSymbol receiverType && receiverType.IsAnonymousType
                    && _model.GetSymbolInfo(node).Symbol is IMethodSymbol method && method.ContainingType.SpecialType == SpecialType.System_Object)
                {
                    AnonInfo info = _shared.Get(receiverType, out string error);
                    if (info == null)
                    {
                        if (_defer)
                            return base.VisitInvocationExpression(node);
                        Error(node, error);
                        return node;
                    }
                    _allowed.Add(access.Expression);
                    int count = node.ArgumentList.Arguments.Count;
                    if (method.Name == "ToString" && count == 0)
                    {
                        if (info.ToStringError != null)
                        {
                            Error(node, $"匿名型の ToString は、{info.ToStringError}、使えません");
                            return node;
                        }
                        return Parse($"{Text(Visit(access.Expression))}.__ToString()", node);
                    }
                    if (method.Name == "Equals" && count == 1)
                    {
                        if (info.EqualsError != null)
                        {
                            Error(node, $"匿名型の Equals は、{info.EqualsError}、使えません");
                            return node;
                        }
                        ExpressionSyntax argument = node.ArgumentList.Arguments[0].Expression;
                        _allowed.Add(argument);
                        return Parse($"{Text(Visit(access.Expression))}.__Equals({Text(Visit(argument))})", node);
                    }
                    Error(node, $"'{method.Name}' : 匿名型の {method.Name} は未対応です（ToString・Equals・メンバーの読み出しが使えます）");
                    return node;
                }
                return base.VisitInvocationExpression(node);
            }
        }
    }
}
