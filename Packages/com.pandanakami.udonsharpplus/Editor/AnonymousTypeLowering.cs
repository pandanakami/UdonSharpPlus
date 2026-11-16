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
                    members.Add(SyntaxFactory.ParseMemberDeclaration(infos[k].FullCode())
                        .WithLeadingTrivia(SyntaxFactory.Space)
                        .WithTrailingTrivia(k == infos.Count - 1 ? trailing : SyntaxFactory.TriviaList()));
                result[group.Key] = Reparse(unit.WithMembers(SyntaxFactory.List(members)), tree);
            }
            return result;
        }

        private const string DistinctMarker = "(distinct)";

        /// <summary>
        /// List・LINQ の、要素を Equals で比べるメソッドの呼び出しを、生成したクラス（className。値で比べる __Eq を持つ）のヘルパーの呼び出しにしたコード（#138）。
        /// 匿名型とタプル（#82）で共用する。書き換えられない形（比較子つき・ToDictionary のキーなど）は null。
        /// helpers には、使ったヘルパーの名前を足す（クラスのコードに、使ったものだけを入れる）
        /// </summary>
        internal static string ListComparisonCode(InvocationExpressionSyntax node, IMethodSymbol method, SemanticModel model, string className,
            ISet<string> helpers, Func<SyntaxNode, string> visit, ref int counter)
        {
            if (method.Name == "ToDictionary" || method.Name == "GroupBy")
                return null;
            var args = node.ArgumentList.Arguments.Select(a => a.Expression).ToList();
            ExpressionSyntax receiver;
            if (method.ReducedFrom != null || !method.IsStatic)
            {
                if (!(node.Expression is MemberAccessExpressionSyntax access))
                    return null;
                receiver = access.Expression;
            }
            else
            {
                if (args.Count == 0)
                    return null;
                receiver = args[0];
                args.RemoveAt(0);
            }
            bool linq = method.ContainingType.Name == "Enumerable";
            string helper;
            if (linq)
                helper = method.Name == "Distinct" && args.Count == 0 ? DistinctMarker : method.Name == "Contains" && args.Count == 1 ? "__Contains" : null;
            else if (method.Name == "Contains" || method.Name == "Remove")
                helper = args.Count == 1 ? "__" + method.Name : null;
            else
                helper = args.Count >= 1 && args.Count <= 3 ? "__" + method.Name + args.Count : null;   // IndexOf・LastIndexOf
            if (helper == null)
                return null;
            string list = visit(receiver);
            if (helper == DistinctMarker)
            {
                // Distinct()：LINQ の段のまま、値で比べる。「x => __Anon_0.__Eq(x, x)」の Where を印にして、List・LINQ の段（CollectionLowering）が
                // 「見たことのある値と __Eq で比べる」段にする（前の段のラムダを呼ぶ回数・止まり方は、C# の Distinct と同じ）
                int id = counter++;
                return $"global::System.Linq.Enumerable.Where({list}, __de{id} => {className}.__Eq(__de{id}, __de{id}))";
            }
            bool isList = model.GetTypeInfo(receiver).Type is INamedTypeSymbol rt && rt.Name == "List" && rt.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
            if (linq && !isList && helper == "__Contains")
            {
                // LINQ の結果・配列への Contains：C# と同じく、見つかった所で止める（前の段のラムダを、必要な分だけ呼ぶ）。
                // 探す値は 1 回だけ評価して（Repeat の引数）、外側のラムダの引数として持つ
                int id = counter++;
                const string linqName = "global::System.Linq.Enumerable";
                return $"{linqName}.Any({linqName}.Repeat<{className}>({visit(args[0])}, 1), __ct{id} => {linqName}.Any({list}, __ce{id} => {className}.__Eq(__ce{id}, __ct{id})))";
            }
            helpers.Add(helper);
            string call = $"{className}.{helper}({string.Join(", ", new[] { list }.Concat(args.Select(visit)))})";
            // 範囲つきの IndexOf・LastIndexOf は int[] を返す（範囲が合わなければ空）。[0] を読んで、呼んだ行で止める
            bool ranged = (helper.StartsWith("__IndexOf", StringComparison.Ordinal) || helper.StartsWith("__LastIndexOf", StringComparison.Ordinal)) && !helper.EndsWith("1", StringComparison.Ordinal);
            return ranged ? call + "[0]" : call;
        }

        /// <summary>匿名型・タプルをキーにする GroupBy のエラー（#140）</summary>
        internal static string GroupKeyMessage(string kindJa, string kindEn) =>
            Loc.T($"'GroupBy' : {kindJa}をキーにする GroupBy は未対応です（C# はキーを値（Equals）で比べますが、ここでは参照の比較になります）。キーは、数値・string などのメンバーにしてください",
                $"'GroupBy' : GroupBy keyed by {kindEn} is not supported (C# compares keys by value (Equals), but here they would be compared by reference). Use a member such as a number or string as the key");

        /// <summary>GroupBy のキーのエラーを出す場所：キーのラムダ（複数行のつないだ呼び出しでも、GroupBy の行になる）</summary>
        internal static SyntaxNode GroupKeyNode(InvocationExpressionSyntax node, IMethodSymbol m)
        {
            int index = m.ReducedFrom != null ? 0 : 1;   // static の形（Enumerable.GroupBy(list, f)）は 2 つめ
            var args = node.ArgumentList.Arguments;
            return args.Count > index ? (SyntaxNode)args[index].Expression : node;
        }

        /// <summary>要素を Equals で比べるメソッド（List の Contains・IndexOf・LastIndexOf・Remove、LINQ の Distinct・Contains・ToDictionary のキー）か。element は比べる要素の型</summary>
        internal static bool ComparesByEquals(IMethodSymbol m, out ITypeSymbol element)
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
            // ToDictionary・GroupBy（#140）：キーの型（TKey は 2 つ目の型引数）
            if (owner.Name == "Enumerable" && (m.Name == "ToDictionary" || m.Name == "GroupBy") && m.TypeArguments.Length >= 2)
            {
                element = m.TypeArguments[1];
                return true;
            }
            return false;
        }

        /// <summary>リストを値で比べるヘルパーのコード（生成するクラスの中に置く。使ったものだけ）</summary>
        internal static string ListHelpersCode(IEnumerable<string> helpers, string className)
        {
            string list = "global::System.Collections.Generic.List<" + className + ">";
            var sb = new StringBuilder();
            foreach (string helper in helpers)
                sb.Append(AnonInfo.ListHelper(helper, className, list)).Append(' ');
            return sb.ToString();
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
            public readonly SortedSet<string> ListHelpers = new SortedSet<string>(StringComparer.Ordinal);   // 使っている、リストを値で比べるヘルパー（#138）

            /// <summary>クラスのコード（使っているリストのヘルパーを、閉じかっこの前に足す）</summary>
            public string FullCode()
            {
                if (ListHelpers.Count == 0)
                    return Code;
                string list = "global::System.Collections.Generic.List<" + Name + ">";
                var sb = new StringBuilder(Code.Substring(0, Code.Length - 1));
                foreach (string helper in ListHelpers)
                    sb.Append(ListHelper(helper, Name, list)).Append(' ');
                return sb.Append('}').ToString();
            }

            /// <summary>
            /// List&lt;匿名型&gt; を、C# と同じく値（__Eq：null どうしは等しい）で比べるメソッド（#138）
            /// </summary>
            internal static string ListHelper(string helper, string n, string list)
            {
                const string scan = "if (__Eq(l[i], x)) return i; ";
                switch (helper)
                {
                    case "__Contains":
                        return $"public static bool __Contains({list} l, {n} x) {{ int c = l.Count; for (int i = 0; i < c; i++) {{ if (__Eq(l[i], x)) return true; }} return false; }}";
                    case "__IndexOf1":
                        return $"public static int __IndexOf1({list} l, {n} x) {{ int c = l.Count; for (int i = 0; i < c; i++) {{ {scan}}} return -1; }}";
                    // 範囲つきの形は int[] を返す：範囲が合えば { 結果 }、合わなければ空（呼び出し側が [0] を読むので、呼んだ行で実行時例外になる）。
                    // 範囲の決まりは、List<T>（.NET Framework / Mono）と同じ
                    case "__IndexOf2":
                        return $"public static int[] __IndexOf2({list} l, {n} x, int s) {{ int c = l.Count; if (s < 0 || s > c) return new int[0]; for (int i = s; i < c; i++) {{ if (__Eq(l[i], x)) return new int[] {{ i }}; }} return new int[] {{ -1 }}; }}";
                    case "__IndexOf3":
                        return $"public static int[] __IndexOf3({list} l, {n} x, int s, int k) {{ int c = l.Count; if (s < 0 || s > c || k < 0 || s > c - k) return new int[0]; int e = s + k; "
                               + "for (int i = s; i < e; i++) { if (__Eq(l[i], x)) return new int[] { i }; } return new int[] { -1 }; }";
                    case "__LastIndexOf1":
                        return $"public static int __LastIndexOf1({list} l, {n} x) {{ for (int i = l.Count - 1; i >= 0; i--) {{ {scan}}} return -1; }}";
                    case "__LastIndexOf2":
                        return $"public static int[] __LastIndexOf2({list} l, {n} x, int s) {{ int c = l.Count; if (s >= c) return new int[0]; if (c == 0) return new int[] {{ -1 }}; if (s < 0) return new int[0]; "
                               + "for (int i = s; i >= 0; i--) { if (__Eq(l[i], x)) return new int[] { i }; } return new int[] { -1 }; }";
                    case "__LastIndexOf3":
                        return $"public static int[] __LastIndexOf3({list} l, {n} x, int s, int k) {{ int c = l.Count; if (c != 0 && (s < 0 || k < 0)) return new int[0]; if (c == 0) return new int[] {{ -1 }}; if (s >= c || k > s + 1) return new int[0]; "
                               + "int e = s - k; for (int i = s; i > e; i--) { if (__Eq(l[i], x)) return new int[] { i }; } return new int[] { -1 }; }";
                    case "__Remove":
                        return $"public static bool __Remove({list} l, {n} x) {{ int c = l.Count; for (int i = 0; i < c; i++) {{ if (__Eq(l[i], x)) {{ l.RemoveAt(i); return true; }} }} return false; }}";
                    default:
                        return "";
                }
            }
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
                        error = Loc.T($"'{p.Name}' : 匿名型のメンバーに、{why}は使えません", $"'{p.Name}' : {why} cannot be used as a member of an anonymous type");
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
                        return Loc.T("型引数（ジェネリクスのコピーのあとの段で、型引数が決まったものだけ使えます）", "a type parameter (only allowed in the stage after generic copies, once the type argument is known)");
                    case IPointerTypeSymbol _:
                        return Loc.T("ポインター", "a pointer");
                    case INamedTypeSymbol n:
                        if (n.TypeKind == TypeKind.Error)
                            return Loc.T("解決できない型", "an unresolved type");
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
                    if (named.IsTupleType)
                        return $"{expression}.ToString()";   // タプルの段（#82）が __ToString にする
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
                        info.ToStringError = Loc.T($"メンバー '{p.Name}' の型（{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}）の文字列を、C# と同じにできないため", $"the type of member '{p.Name}' ({p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}) cannot be turned into the same text as C#");
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
                        info.EqualsError = Loc.T($"メンバー '{p.Name}' がデリゲートで、比べ方が C# と違うため", $"member '{p.Name}' is a delegate and would be compared differently from C#");
                        break;
                    }
                    compares.Add(p.Type is INamedTypeSymbol a && a.IsAnonymousType && Get(a, out _) is AnonInfo inner
                        ? $"{inner.Name}.__Eq({Ident(p.Name)}, b.{Ident(p.Name)})"
                        : p.Type.IsTupleType ? $"{Ident(p.Name)}.Equals(b.{Ident(p.Name)})"   // タプルの段（#82）が、値の比較にする
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

            private static bool IsAnonymous(ITypeSymbol type) => type is INamedTypeSymbol n && n.IsAnonymousType;

            /// <summary>
            /// 匿名型の式が、匿名型のまま使われていないところ（object への変換）。文字列の連結・補間なら __Str にし、
            /// 参照の比較（==・!=）は object[] でも同じなのでそのまま。それ以外はエラー
            /// </summary>
            public override SyntaxNode Visit(SyntaxNode node)
            {
                // a.b の b（名前の部分）は、a.b 全体で見る（名前の部分にも、外側の変換が出る。#82 で見つけた）
                if (node is ExpressionSyntax e && !(e.Parent is MemberAccessExpressionSyntax ma && ma.Name == e) && !(e.Parent is MemberBindingExpressionSyntax))
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
                                Error(e, Loc.T($"匿名型の文字列への変換は、{anon.ToStringError}、使えません", $"Converting an anonymous type to a string cannot be used because {anon.ToStringError}"));
                                return node;
                            }
                            return Parse($"{anon.Name}.__Str({Text(base.Visit(node))})", node);
                        }
                        // かっこの中の式は、かっこ全体で見る（かっこの中の式にも、外側の変換が出る）
                        if (!(e.Parent is ParenthesizedExpressionSyntax) && info.ConvertedType != null
                            && !SymbolEqualityComparer.Default.Equals(info.Type, info.ConvertedType) && !IsReferenceComparison(e))
                        {
                            Error(e, Loc.T("匿名型を object・インタフェースとして使うことは未対応です（p.ToString() で文字列にするか、メンバーを渡してください）", "Using an anonymous type as object or as an interface is not supported (turn it into a string with p.ToString(), or pass its members)"));
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

            /// <summary>
            /// list.Contains(x)・IndexOf・LastIndexOf・Remove、seq.Contains(x)・seq.Distinct() → __Anon_0.__Contains(list, x) など（#138）。
            /// 受け側と引数は、元の順に 1 回ずつ評価する。書き換えられない形（比較子つき・デリゲートのメンバーがある匿名型など）は null
            /// </summary>
            private int _containsCount;

            private SyntaxNode ListComparison(InvocationExpressionSyntax node, IMethodSymbol method, INamedTypeSymbol element)
            {
                AnonInfo info = _shared.Get(element, out _);
                if (info == null || info.EqualsError != null)
                    return null;
                string code = ListComparisonCode(node, method, _model, info.Name, info.ListHelpers, n => Text(Visit(n)), ref _containsCount);
                return code == null ? null : Parse(code, node);
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
                    // 匿名型の要素：生成するクラスの、値で比べるヘルパーの呼び出しにする（#138）
                    if (anonElement.IsAnonymousType && ListComparison(node, cmp, anonElement) is SyntaxNode compared)
                        return compared;
                    Error(cmp.Name == "GroupBy" ? GroupKeyNode(node, cmp) : node, cmp.Name == "GroupBy" ? GroupKeyMessage("匿名型", "an anonymous type") : cmp.Name == "ToDictionary"
                        ? Loc.T("'ToDictionary' : 匿名型をキーにする Dictionary は未対応です（C# はキーを値（Equals）で比べますが、ここでは参照の比較になります）。キーは、数値・string などのメンバーにしてください", "'ToDictionary' : A Dictionary keyed by an anonymous type is not supported (C# compares keys by value (Equals), but here they would be compared by reference). Use a member such as a number or string as the key")
                        : Loc.T($"'{cmp.Name}' : 匿名型の要素を値（Equals）で比べるこの形は未対応です（リストの比較は参照になります）。Exists / FindIndex / Any / Where などのラムダで比べてください", $"'{cmp.Name}' : This form, which compares anonymous type elements by value (Equals), is not supported (the list comparison would be by reference). Compare in a lambda with Exists / FindIndex / Any / Where instead"));
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
                            Error(node, Loc.T($"匿名型の ToString は、{info.ToStringError}、使えません", $"ToString on an anonymous type cannot be used because {info.ToStringError}"));
                            return node;
                        }
                        return Parse($"{Text(Visit(access.Expression))}.__ToString()", node);
                    }
                    if (method.Name == "Equals" && count == 1)
                    {
                        if (info.EqualsError != null)
                        {
                            Error(node, Loc.T($"匿名型の Equals は、{info.EqualsError}、使えません", $"Equals on an anonymous type cannot be used because {info.EqualsError}"));
                            return node;
                        }
                        ExpressionSyntax argument = node.ArgumentList.Arguments[0].Expression;
                        _allowed.Add(argument);
                        return Parse($"{Text(Visit(access.Expression))}.__Equals({Text(Visit(argument))})", node);
                    }
                    Error(node, Loc.T($"'{method.Name}' : 匿名型の {method.Name} は未対応です（ToString・Equals・メンバーの読み出しが使えます）", $"'{method.Name}' : {method.Name} on an anonymous type is not supported (ToString, Equals and reading members are supported)"));
                    return node;
                }
                return base.VisitInvocationExpression(node);
            }
        }
    }
}
