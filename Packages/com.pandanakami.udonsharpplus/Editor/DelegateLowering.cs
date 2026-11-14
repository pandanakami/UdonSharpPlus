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
    /// ソースで宣言したデリゲート（delegate void Hoge(int n);）を、UdonSharp が扱える形へ書き換える（Redmine #69 / #119）。
    ///
    /// デリゲートの値は object[]。1 つのメソッドごとに 3 つの要素「呼ぶ相手の UdonSharpBehaviour・中継メソッドの名前・外の変数の環境の一覧
    /// （ラムダが使う外の変数を置いた object[] を並べたもの。使わなければ null。#121）」を並べる
    /// （+= でつなぐ（#122）と、この 3 つが続けて並ぶ。C# のデリゲートと同じく、値は書き換えない）。
    ///
    ///   - 型 Hoge → object[]（変数・フィールド・引数・戻り値・配列）
    ///   - メソッド名から作る（h = F;）→ new object[] { this, "中継メソッドの名前", null }
    ///     作る側の UdonSharpBehaviour に、中継メソッド（引数なしの public メソッド。U# は名前を変えない）を置く：
    ///       public void __dlg_K__F__C() { __dlg_K__ret = F(__dlg_K__p0); }
    ///     引数・戻り値は、デリゲート型ごとに決まった名前のフィールド（__dlg_K__p0 / __dlg_K__ret）で受け渡す
    ///   - 呼び出し（h(x)、h.Invoke(x)）→ __dlg_K.__invoke(h, x)
    ///     __dlg_K はデリゲートの宣言の隣に置く static クラス。中で SetProgramVariable / SendCustomEvent / GetProgramVariable する
    ///     （普通のクラスの static メソッドなので、次の段（ClassLowering）が、使う UdonSharpBehaviour にコピーを入れる）
    ///
    /// この段はインタフェース・普通のクラスの段より前に動く。生成するコードは元の型（普通のクラス・インタフェース）のままの C# で、
    /// それを後の段が書き換える。行は増やさない（生成メンバーは閉じ括弧の行に置く）。
    /// </summary>
    internal static class DelegateLowering
    {
        private const string ValueType = "object[]";
        private const string UsbType = InterfaceLowering.UsbType;

        internal sealed class DelegateInfo
        {
            public INamedTypeSymbol Symbol;
            public string Key;              // 完全名を名前に使える形にしたもの
            public string Unsupported;      // 対応しない理由（null なら対応）
            public string HelperClass => "__dlg_" + Key;
            public string HelperFull;       // global::名前空間.__dlg_K
            public string ParamField(int i) => $"__dlg_{Key}__p{i}";
            public string RetField => $"__dlg_{Key}__ret";
            public string EnvField => $"__dlg_{Key}__env";
            public IMethodSymbol Invoke => Symbol.DelegateInvokeMethod;
        }

        /// <summary>ソースのデリゲートの宣言か、ローカル関数（#123。UdonSharp が未対応なので、メソッドに移す）があるか</summary>
        public static bool MayHaveDelegates(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is DelegateDeclarationSyntax || n is LocalFunctionStatementSyntax));

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_DelegatePrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var delegates = CollectDelegates(compilation, trees);
            if (delegates.Count == 0 && !trees.Any(t => t.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Any()))
                return trees;

            var shared = new Shared(delegates, diagnostics);
            var result = new List<SyntaxTree>(trees.Count);
            for (int i = 0; i < trees.Count; i++)
            {
                SemanticModel model = compilation.GetSemanticModel(trees[i]);
                var rewriter = new Rewriter(shared, model, i);
                SyntaxNode newRoot = rewriter.Visit(trees[i].GetRoot());
                result.Add(rewriter.Changed ? Reparse(newRoot, trees[i]) : trees[i]);
            }

            return AddGeneratedMembers(result, trees, shared);
        }

        private static SyntaxTree Reparse(SyntaxNode root, SyntaxTree original) =>
            CSharpSyntaxTree.ParseText(root.ToFullString(), (CSharpParseOptions)original.Options, original.FilePath, Encoding.UTF8);

        // ------------------------------------------------------------------

        private static Dictionary<INamedTypeSymbol, DelegateInfo> CollectDelegates(CSharpCompilation compilation, List<SyntaxTree> trees)
        {
            var result = new Dictionary<INamedTypeSymbol, DelegateInfo>(SymbolEqualityComparer.Default);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (DelegateDeclarationSyntax decl in tree.GetRoot().DescendantNodes(n => !(n is BlockSyntax)).OfType<DelegateDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol) || result.ContainsKey(symbol))
                        continue;
                    INamespaceSymbol ns = symbol.ContainingNamespace;
                    var info = new DelegateInfo
                    {
                        Symbol = symbol,
                        Key = InterfaceNaming.Sanitize(ClassLowering.TagOf(symbol)),
                        Unsupported = UnsupportedReason(symbol),
                    };
                    info.HelperFull = "global::" + (ns == null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString() + ".") + info.HelperClass;
                    result.Add(symbol, info);
                }
            }
            return result;
        }

        private static string UnsupportedReason(INamedTypeSymbol symbol)
        {
            if (symbol.IsGenericType)
                return "ジェネリックのデリゲートは、まだ未対応です";
            IMethodSymbol invoke = symbol.DelegateInvokeMethod;
            if (invoke == null)
                return "このデリゲートは未対応です";
            if (invoke.ReturnsByRef || invoke.ReturnsByRefReadonly)
                return "ref で返すデリゲートは未対応です";
            if (invoke.Parameters.Any(p => p.RefKind != RefKind.None))
                return "ref / out / in の引数を持つデリゲートは未対応です";
            if (invoke.Parameters.Any(p => p.IsParams || p.HasExplicitDefaultValue))
                return "params・既定値のある引数を持つデリゲートは未対応です";
            return null;
        }

        /// <summary>書き換えの全体で共有するもの（中継メソッドの一覧など）</summary>
        private sealed class Shared
        {
            public readonly Dictionary<INamedTypeSymbol, DelegateInfo> Delegates;
            public readonly InterfaceLowering.LoweringDiagnostics Diagnostics;

            /// <summary>UdonSharpBehaviour ごとの生成メンバーと、使うデリゲート型</summary>
            public readonly Dictionary<INamedTypeSymbol, HostInfo> Hosts = new Dictionary<INamedTypeSymbol, HostInfo>(SymbolEqualityComparer.Default);

            public HostInfo HostOf(INamedTypeSymbol host)
            {
                if (!Hosts.TryGetValue(host, out HostInfo info))
                    Hosts[host] = info = new HostInfo();
                return info;
            }

            public Shared(Dictionary<INamedTypeSymbol, DelegateInfo> delegates, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                Delegates = delegates;
                Diagnostics = diagnostics;
            }

            public DelegateInfo Find(ITypeSymbol type) =>
                type is INamedTypeSymbol named && Delegates.TryGetValue(named.OriginalDefinition, out var info) ? info : null;

            /// <summary>生成するコードに書く型（ソースのデリゲート型は object[]）</summary>
            public string TypeText(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                if (Find(type) != null)
                    return ValueType;
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            /// <summary>
            /// 呼び出しの static クラス（1 行）：
            ///   public static class __dlg_K { public static R __invoke(object[] __d, T0 __a0) { ... } }
            /// </summary>
            public string HelperClassCode(DelegateInfo info)
            {
                IMethodSymbol invoke = info.Invoke;
                bool hasReturn = !invoke.ReturnsVoid;
                string ret = hasReturn ? TypeText(invoke.ReturnType) : "void";
                var ps = invoke.Parameters;
                var sb = new StringBuilder();
                // __invoke の引数・ローカル変数は、呼ぶ側のプログラムのヒープの変数。SendCustomEvent の間に同じプログラムで
                // __invoke がまた動くと上書きされる（#128）。[RecursiveMethod] を付け、SendCustomEvent をユーザーのメソッド（__send）
                // 経由で呼ぶと、UdonSharp がその前後で値をスタックに積んで戻す
                sb.Append($"public static class {info.HelperClass} {{ ");
                sb.Append($"private static void __send({UsbType} __u, string __name) {{ __u.SendCustomEvent(__name); }} ");
                sb.Append($"[global::UdonSharp.RecursiveMethod] public static {ret} __invoke({ValueType} __d");
                for (int i = 0; i < ps.Length; i++)
                    sb.Append($", {TypeText(ps[i].Type)} __a{i}");
                sb.Append(") { ");
                // null なら __d.Length で実行時例外になる（C# の NullReferenceException に当たる）
                sb.Append("int __n = __d.Length; ");
                if (hasReturn)
                    sb.Append($"{ret} __r = default({ret}); ");
                sb.Append("for (int __i = 0; __i < __n; __i += 3) { ");
                sb.Append($"{UsbType} __u = ({UsbType})__d[__i]; ");
                for (int i = 0; i < ps.Length; i++)
                    sb.Append($"__u.SetProgramVariable(\"{info.ParamField(i)}\", __a{i}); ");
                // 外の変数（#121）。使わないラムダ・メソッドでは null なので渡さない（中継メソッドも読まない）
                sb.Append($"object __e = __d[__i + 2]; if (__e != null) __u.SetProgramVariable(\"{info.EnvField}\", __e); ");
                sb.Append("__send(__u, (string)__d[__i + 1]); ");
                if (hasReturn)
                    sb.Append($"__r = ({ret})__u.GetProgramVariable(\"{info.RetField}\"); ");
                sb.Append("} ");
                if (hasReturn)
                    sb.Append("return __r; ");
                sb.Append("} ");
                sb.Append(CombineCode).Append(' ').Append(RemoveCode);
                sb.Append(" }");
                return sb.ToString();
            }

            /// <summary>
            /// a + b（#122）：組を並べた新しい配列。C# と同じく、元の値は書き換えない。どちらかが null なら、もう片方
            /// </summary>
            private const string CombineCode =
                "public static object[] __combine(object[] __a, object[] __b) { " +
                "if (__a == null) return __b; if (__b == null) return __a; " +
                "int __na = __a.Length; int __nb = __b.Length; object[] __r = new object[__na + __nb]; " +
                "for (int __i = 0; __i < __na; __i++) __r[__i] = __a[__i]; " +
                "for (int __i = 0; __i < __nb; __i++) __r[__na + __i] = __b[__i]; " +
                "return __r; }";

            /// <summary>
            /// a - b（#122）：C# の Delegate.Remove と同じく、b の組の並びが a の中に続けて現れる最後の場所を外す。
            /// 組は、呼ぶ相手・メソッドの名前・外の変数の環境（一覧の中の各 object[] の参照）がすべて同じなら同じとみなす。全部外れたら null、見つからなければ a
            /// </summary>
            private const string RemoveCode =
                "public static object[] __remove(object[] __a, object[] __b) { " +
                "if (__a == null) return null; if (__b == null) return __a; " +
                "int __na = __a.Length; int __nb = __b.Length; " +
                "for (int __i = __na - __nb; __i >= 0; __i -= 3) { " +
                "bool __same = true; " +
                "for (int __j = 0; __j < __nb; __j += 3) { " +
                "if (__a[__i + __j] != __b[__j] || (string)__a[__i + __j + 1] != (string)__b[__j + 1] || !__sameEnv(__a[__i + __j + 2], __b[__j + 2])) { __same = false; break; } } " +
                "if (!__same) continue; " +
                "if (__na == __nb) return null; " +
                "object[] __r = new object[__na - __nb]; " +
                "for (int __k = 0; __k < __i; __k++) __r[__k] = __a[__k]; " +
                "for (int __k = __i + __nb; __k < __na; __k++) __r[__k - __nb] = __a[__k]; " +
                "return __r; } " +
                "return __a; } " +
                // 外の変数の環境の一覧は、作るたびに新しい配列になる。C# と同じく、同じ範囲の環境（中の object[] が同じもの）なら同じとみなす
                "private static bool __sameEnv(object __x, object __y) { " +
                "if (__x == __y) return true; if (__x == null || __y == null) return false; " +
                "object[] __ex = (object[])__x; object[] __ey = (object[])__y; if (__ex.Length != __ey.Length) return false; " +
                "for (int __i = 0; __i < __ex.Length; __i++) if (__ex[__i] != __ey[__i]) return false; " +
                "return true; }";

            /// <summary>中継メソッドが使う、引数・戻り値のフィールド（1 行ずつ）</summary>
            public IEnumerable<string> FieldCode(DelegateInfo info)
            {
                IMethodSymbol invoke = info.Invoke;
                for (int i = 0; i < invoke.Parameters.Length; i++)
                    yield return $"protected {TypeText(invoke.Parameters[i].Type)} {info.ParamField(i)};";
                if (!invoke.ReturnsVoid)
                    yield return $"protected {TypeText(invoke.ReturnType)} {info.RetField};";
                yield return $"protected {ValueType} {info.EnvField};";
            }
        }

        /// <summary>クラスの宣言の場所（tree の番号・名前・名前の行）。書き換えても行は変わらないので、書き換え後の tree でも引ける</summary>
        private readonly struct DeclRef : IEquatable<DeclRef>
        {
            public readonly int Tree;
            public readonly string Name;
            public readonly int Line;

            public DeclRef(int tree, ClassDeclarationSyntax decl)
            {
                Tree = tree;
                Name = decl.Identifier.ValueText;
                Line = decl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line;
            }

            public bool Equals(DeclRef other) => Tree == other.Tree && Name == other.Name && Line == other.Line;
            public override bool Equals(object obj) => obj is DeclRef other && Equals(other);
            public override int GetHashCode() => (Tree * 397) ^ (Name.GetHashCode() * 31) ^ Line;
        }

        /// <summary>UdonSharpBehaviour ごとの生成メンバー</summary>
        private sealed class HostInfo
        {
            public readonly HashSet<DelegateInfo> Delegates = new HashSet<DelegateInfo>();
            /// <summary>名前 → コードと、置く宣言（作った場所の宣言。partial のほかのファイルの using に頼らないため）</summary>
            public readonly Dictionary<string, (string Code, DeclRef Decl)> Members = new Dictionary<string, (string, DeclRef)>();
            public int LambdaCount;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 中継メソッド・ラムダの本体は、作った場所のクラスの宣言の、閉じ括弧の行に置く。
        /// 引数・戻り値のフィールドは、UdonSharpBehaviour の最初の宣言（partial なら tree の順で最初）に置く。
        /// 同じデリゲート型を使う基底があれば、基底の側にだけ置く（派生は継承したものを使う）
        /// </summary>
        private static List<SyntaxTree> AddGeneratedMembers(List<SyntaxTree> rewritten, List<SyntaxTree> originals, Shared shared)
        {
            if (shared.Hosts.Count == 0)
                return rewritten;

            var result = new List<SyntaxTree>(rewritten);
            var byDecl = new Dictionary<DeclRef, List<string>>();
            void Add(DeclRef decl, IEnumerable<string> code)
            {
                if (!byDecl.TryGetValue(decl, out var list))
                    byDecl[decl] = list = new List<string>();
                list.AddRange(code);
            }

            foreach (var pair in shared.Hosts)
            {
                INamedTypeSymbol host = pair.Key;
                var fields = new List<string>();
                foreach (DelegateInfo info in pair.Value.Delegates.OrderBy(d => d.Key, StringComparer.Ordinal))
                {
                    bool inherited = false;
                    for (INamedTypeSymbol b = host.BaseType; b != null && !inherited; b = b.BaseType)
                        inherited = shared.Hosts.TryGetValue(b, out var baseHost) && baseHost.Delegates.Contains(info);
                    if (!inherited)
                        fields.AddRange(shared.FieldCode(info));
                }

                if (fields.Count > 0)
                {
                    var first = host.DeclaringSyntaxReferences
                        .Select(r => (Tree: originals.IndexOf(r.SyntaxTree), Node: r.GetSyntax() as ClassDeclarationSyntax))
                        .Where(r => r.Tree >= 0 && r.Node != null)
                        .OrderBy(r => r.Tree).ThenBy(r => r.Node.SpanStart)
                        .FirstOrDefault();
                    if (first.Node != null)
                        Add(new DeclRef(first.Tree, first.Node), fields);
                }
                foreach (var m in pair.Value.Members.OrderBy(m => m.Key, StringComparer.Ordinal))
                    Add(m.Value.Decl, new[] { m.Value.Code });
            }

            foreach (var group in byDecl.GroupBy(d => d.Key.Tree))
            {
                SyntaxTree tree = result[group.Key];
                SyntaxNode root = tree.GetRoot();
                var targets = new Dictionary<ClassDeclarationSyntax, List<string>>();
                foreach (var pair in group)
                {
                    ClassDeclarationSyntax target = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c =>
                        c.Identifier.ValueText == pair.Key.Name && c.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line == pair.Key.Line);
                    if (target != null)
                        targets[target] = pair.Value;
                }
                if (targets.Count == 0)
                    continue;
                root = root.ReplaceNodes(targets.Keys, (o, n) => GeneratedMembers.AppendOnClosingLine(n, targets[o]));
                result[group.Key] = Reparse(root, tree);
            }
            return result;
        }

        // ------------------------------------------------------------------

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly Shared _shared;
            private readonly SemanticModel _model;
            private readonly Stack<string> _owners = new Stack<string>();   // 普通のクラスの中なら、そのクラスの型タグ（使われないクラスのエラーは捨てる）

            public bool Changed { get; private set; }

            private readonly int _treeIndex;

            /// <summary>ラムダの本体を書き換えている間：元の文 → 位置の印（書き換えた文の前に付ける）</summary>
            private Dictionary<SyntaxNode, string> _statementMarkers;

            public Rewriter(Shared shared, SemanticModel model, int treeIndex)
            {
                _shared = shared;
                _model = model;
                _treeIndex = treeIndex;
                AnalyzeClosures(model.SyntaxTree.GetRoot());
            }

            // ---- クロージャ（#121）の下調べ -------------------------------------
            //
            // デリゲートに入れるラムダが使う外のローカル変数・引数は、C# と同じく、宣言した範囲（スコープ）ごとの object[]（環境）の
            // 要素にする。環境は、その範囲に入るたびに作る（ブロックの先頭・foreach の 1 回ごと・メソッド／ラムダの本体の先頭）。
            // 元のメソッドもラムダも、その変数を ((T)__envN[i]) で読み書きする。
            // 環境の変数の名前（__envN）は、ファイルの中で一意にする。ラムダの本体のメソッドでは、受け取った環境の一覧（__env）から
            // 同じ名前のローカル変数に取り出すので、どこでも同じ名前で書ける

            private sealed class Scope
            {
                public SyntaxNode Node;     // Block・ForEachStatement・MethodDeclaration（引数）・ラムダ／匿名メソッド／ローカル関数（引数）
                public string Name;         // __envN
                public readonly List<ISymbol> Vars = new List<ISymbol>();
            }

            /// <summary>
            /// ローカル関数（#123）。UdonSharp は未対応なので、同じクラスのメソッドに移す（Hoisted）。外の変数は、使う環境を
            /// 環境と同じ名前の引数で受け取る（private R __lf_F__t_n(object[] __env0, ..., 元の引数)）。呼び出しはその環境を渡す
            /// </summary>
            private sealed class LocalFunc
            {
                public LocalFunctionStatementSyntax Node;
                public IMethodSymbol Symbol;
                public string Hoisted;
                public bool IsStatic;
                public bool Recursive;
            }

            private readonly Dictionary<ISymbol, (Scope Scope, int Index)> _captured = new Dictionary<ISymbol, (Scope, int)>(SymbolEqualityComparer.Default);
            private readonly Dictionary<SyntaxNode, Scope> _scopes = new Dictionary<SyntaxNode, Scope>();
            /// <summary>
            /// デリゲートに入れるラムダ・匿名メソッドと、ローカル関数 → 外から受け取る環境
            /// （中のラムダ・呼ぶローカル関数が使うものも含む）
            /// </summary>
            private readonly Dictionary<SyntaxNode, List<Scope>> _needs = new Dictionary<SyntaxNode, List<Scope>>();
            private readonly HashSet<SyntaxNode> _funcErrors = new HashSet<SyntaxNode>();
            private readonly Dictionary<ISymbol, LocalFunc> _localFuncs = new Dictionary<ISymbol, LocalFunc>(SymbolEqualityComparer.Default);
            private int _envCount;

            private static SyntaxNode BodyOf(SyntaxNode function) =>
                function is AnonymousFunctionExpressionSyntax a ? (SyntaxNode)a.Body
                : function is LocalFunctionStatementSyntax l ? (SyntaxNode)l.Body ?? l.ExpressionBody : null;

            private LocalFunc LocalFuncOf(ISymbol symbol) =>
                symbol is IMethodSymbol m && m.MethodKind == MethodKind.LocalFunction && _localFuncs.TryGetValue(m.OriginalDefinition, out var f) ? f : null;

            private void AnalyzeClosures(SyntaxNode root)
            {
                var functions = new List<SyntaxNode>();
                int localCount = 0;
                foreach (SyntaxNode n in root.DescendantNodes())
                {
                    if (n is AnonymousFunctionExpressionSyntax a)
                    {
                        DelegateInfo info = _shared.Find(_model.GetTypeInfo(a).ConvertedType);
                        if (info != null && info.Unsupported == null && HostOf(a, out _) != null)
                            functions.Add(a);
                    }
                    else if (n is LocalFunctionStatementSyntax l && _model.GetDeclaredSymbol(l) is IMethodSymbol symbol)
                    {
                        functions.Add(l);
                        ISymbol member = symbol.ContainingSymbol;
                        while (member is IMethodSymbol mm && (mm.MethodKind == MethodKind.LocalFunction || mm.MethodKind == MethodKind.AnonymousFunction))
                            member = mm.ContainingSymbol;
                        _localFuncs[symbol] = new LocalFunc
                        {
                            Node = l,
                            Symbol = symbol,
                            Hoisted = $"__lf_{symbol.Name}__{_treeIndex}_{localCount++}",
                            IsStatic = symbol.IsStatic || member.IsStatic,
                        };
                    }
                }
                var functionSet = new HashSet<SyntaxNode>(functions);

                var calls = new Dictionary<SyntaxNode, List<LocalFunc>>();
                var invokesDelegate = new HashSet<SyntaxNode>();
                foreach (SyntaxNode f in functions)
                {
                    var direct = new List<Scope>();
                    var called = new List<LocalFunc>();
                    SyntaxNode body = BodyOf(f);
                    if (body == null)
                        continue;
                    foreach (IdentifierNameSyntax id in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                    {
                        ISymbol symbol = _model.GetSymbolInfo(id).Symbol;
                        if (LocalFuncOf(symbol) is LocalFunc callee)
                        {
                            if (!called.Contains(callee))
                                called.Add(callee);
                            continue;
                        }
                        if (!(symbol is ILocalSymbol || symbol is IParameterSymbol p && !p.IsThis))
                            continue;
                        if (symbol is ILocalSymbol constant && constant.IsConst)
                            continue;   // 定数は値をそのまま書く
                        if (symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == f.SyntaxTree && f.Span.Contains(r.Span)))
                            continue;
                        Scope scope = ScopeOf(symbol, functionSet, out string error);
                        if (scope == null)
                        {
                            if (_funcErrors.Add(f))
                                Error(id, $"'{symbol.Name}' : {error}");
                            continue;
                        }
                        if (!_captured.ContainsKey(symbol))
                        {
                            _captured[symbol] = (scope, scope.Vars.Count);
                            scope.Vars.Add(symbol);
                        }
                        if (!direct.Contains(scope))
                            direct.Add(scope);
                    }
                    if (body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(inv => DelegateOf(inv.Expression) != null
                        || inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Invoke" && DelegateOf(ma.Expression) != null))
                        invokesDelegate.Add(f);
                    _needs[f] = direct;
                    calls[f] = called;
                }

                // 呼ぶローカル関数が使う環境も受け取る（その関数の中で宣言したものは除く）。再帰があるので、増えなくなるまで繰り返す
                bool grown = true;
                while (grown)
                {
                    grown = false;
                    foreach (var pair in calls)
                        foreach (LocalFunc callee in pair.Value)
                            if (_needs.TryGetValue(callee.Node, out var calleeNeeds))
                                foreach (Scope s in calleeNeeds.ToList())
                                    if (!pair.Key.Span.Contains(s.Node.Span) && !_needs[pair.Key].Contains(s))
                                    {
                                        _needs[pair.Key].Add(s);
                                        grown = true;
                                    }
                }

                // 自分を（ほかのローカル関数を通しても）呼ぶローカル関数・デリゲートを呼ぶローカル関数は [RecursiveMethod] にする
                foreach (LocalFunc lf in _localFuncs.Values)
                {
                    var seen = new HashSet<SyntaxNode>();
                    var stack = new Stack<SyntaxNode>();
                    stack.Push(lf.Node);
                    while (stack.Count > 0 && !lf.Recursive)
                    {
                        SyntaxNode current = stack.Pop();
                        if (!calls.TryGetValue(current, out var next))
                            continue;
                        foreach (LocalFunc callee in next)
                        {
                            if (callee == lf)
                                lf.Recursive = true;
                            else if (seen.Add(callee.Node))
                                stack.Push(callee.Node);
                        }
                    }
                    if (invokesDelegate.Contains(lf.Node))
                        lf.Recursive = true;
                }
            }

            /// <summary>外の変数を置く環境（宣言した範囲）。置けない変数なら null と理由</summary>
            private Scope ScopeOf(ISymbol symbol, HashSet<SyntaxNode> functions, out string error)
            {
                const string user = "デリゲートに入れるラムダ・ローカル関数で使うことは未対応です";
                error = null;
                SyntaxNode decl = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                SyntaxNode node = null;
                if (symbol is IParameterSymbol)
                {
                    SyntaxNode owner = !(decl is ParameterSyntax ps) ? null : ps.Parent is ParameterListSyntax pl ? pl.Parent : ps.Parent;
                    if (owner is MethodDeclarationSyntax || owner != null && functions.Contains(owner))
                        node = owner;
                    else if (owner is LambdaExpressionSyntax)
                        error = $"List・LINQ のメソッドに渡すラムダの引数を、{user}。ローカル変数に入れてから使ってください";
                }
                else if (symbol is ILocalSymbol)
                {
                    if (decl is VariableDeclaratorSyntax v && v.Parent?.Parent is LocalDeclarationStatementSyntax lds
                        && !lds.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) && lds.Parent is BlockSyntax block)
                        node = block;
                    else if (decl is ForEachStatementSyntax fe)
                        node = fe;
                    else
                        error = $"この場所で宣言した変数（for の変数・switch の中・out var・パターン・catch の変数など）を、{user}。ブロックの中で別のローカル変数に入れてから使ってください";
                }
                else
                {
                    error = $"この変数を、{user}";
                }
                if (node == null)
                {
                    error = error ?? $"この引数を、{user}。ローカル変数に入れてから使ってください";
                    return null;
                }
                if (!_scopes.TryGetValue(node, out Scope scope))
                    _scopes[node] = scope = new Scope { Node = node, Name = "__env" + _envCount++ };
                return scope;
            }

            private bool TryCaptured(SyntaxNode node, out ISymbol symbol, out (Scope Scope, int Index) slot)
            {
                slot = default;
                symbol = node is VariableDeclaratorSyntax v ? _model.GetDeclaredSymbol(v) : _model.GetSymbolInfo(node).Symbol;
                return symbol != null && _captured.TryGetValue(symbol, out slot);
            }

            private static ITypeSymbol VariableType(ISymbol symbol) =>
                symbol is ILocalSymbol l ? l.Type : ((IParameterSymbol)symbol).Type;

            private static string Ident(string name) =>
                SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

            /// <summary>環境を作る文（と、引数・foreach の変数を入れる文）</summary>
            private static string CreateEnv(Scope scope, Func<ISymbol, string> initial = null)
            {
                var sb = new StringBuilder($"object[] {scope.Name} = new object[{scope.Vars.Count}];");
                if (initial != null)
                    for (int i = 0; i < scope.Vars.Count; i++)
                        sb.Append($" {scope.Name}[{i}] = {initial(scope.Vars[i])};");
                return sb.ToString();
            }

            public override SyntaxNode Visit(SyntaxNode node)
            {
                SyntaxNode visited = base.Visit(node);
                if (_statementMarkers != null && node != null && visited != null && _statementMarkers.TryGetValue(node, out string marker))
                    visited = visited.WithLeadingTrivia(visited.GetLeadingTrivia().Insert(0, SyntaxFactory.Comment(marker)));
                return visited;
            }

            /// <summary>作った場所の、クラスの宣言</summary>
            private DeclRef DeclOf(SyntaxNode node) => new DeclRef(_treeIndex, node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().First());

            private void Error(SyntaxNode node, string message) =>
                _shared.Diagnostics.Error(node.GetLocation(), message, _owners.Count > 0 ? _owners.Peek() : null);

            private ExpressionSyntax Parse(string code, SyntaxNode original)
            {
                Changed = true;
                return SourceMap.KeepLineCount(SyntaxFactory.ParseExpression(code).WithTriviaFrom(original), original);
            }

            private DelegateInfo DelegateOf(ExpressionSyntax e) => _shared.Find(_model.GetTypeInfo(e).Type);

            private static string Text(SyntaxNode node) => node.ToString();

            // ---- 宣言 ---------------------------------------------------------

            public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
            {
                var visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node);
                return visited.WithMembers(AppendHelperClasses(node.Members, visited.Members));
            }

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);
                return visited.WithMembers(AppendHelperClasses(node.Members, visited.Members));
            }

            /// <summary>
            /// デリゲートの宣言のある、名前空間の直下のメンバー（デリゲートの宣言そのもの、または入れ子のデリゲートを持つクラス）の
            /// うしろに、呼び出しの static クラスを同じ行で置く
            /// </summary>
            private SyntaxList<MemberDeclarationSyntax> AppendHelperClasses(SyntaxList<MemberDeclarationSyntax> originals, SyntaxList<MemberDeclarationSyntax> visited)
            {
                var result = new List<MemberDeclarationSyntax>();
                bool any = false;
                for (int i = 0; i < visited.Count; i++)
                {
                    MemberDeclarationSyntax member = visited[i];
                    MemberDeclarationSyntax original = i < originals.Count ? originals[i] : null;
                    var infos = new List<DelegateInfo>();
                    if (original is DelegateDeclarationSyntax || original is TypeDeclarationSyntax)
                    {
                        IEnumerable<DelegateDeclarationSyntax> decls = original is DelegateDeclarationSyntax d
                            ? new[] { d }
                            : original.DescendantNodes(n => !(n is BlockSyntax)).OfType<DelegateDeclarationSyntax>();
                        foreach (DelegateDeclarationSyntax decl in decls)
                            if (_model.GetDeclaredSymbol(decl) is INamedTypeSymbol s && _shared.Delegates.TryGetValue(s, out var info) && info.Unsupported == null)
                                infos.Add(info);
                    }
                    if (infos.Count == 0)
                    {
                        result.Add(member);
                        continue;
                    }

                    any = true;
                    SyntaxTriviaList trailing = member.GetTrailingTrivia();
                    result.Add(member.WithTrailingTrivia(SyntaxFactory.TriviaList()));
                    for (int k = 0; k < infos.Count; k++)
                    {
                        MemberDeclarationSyntax helper = SyntaxFactory.ParseMemberDeclaration(_shared.HelperClassCode(infos[k]))
                            .WithLeadingTrivia(SyntaxFactory.Space)
                            .WithTrailingTrivia(k == infos.Count - 1 ? trailing : SyntaxFactory.TriviaList());
                        result.Add(helper);
                    }
                }
                if (!any)
                    return visited;
                Changed = true;
                return SyntaxFactory.List(result);
            }

            public override SyntaxNode VisitDelegateDeclaration(DelegateDeclarationSyntax node) => node;

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) => VisitType(node, n => base.VisitClassDeclaration(n));
            public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node) => VisitType(node, n => base.VisitStructDeclaration(n));

            private SyntaxNode VisitType<T>(T node, Func<T, SyntaxNode> visitBase) where T : TypeDeclarationSyntax
            {
                var symbol = _model.GetDeclaredSymbol(node) as INamedTypeSymbol;
                bool plain = symbol != null && symbol.ContainingType == null && ClassLowering.IsPlainClass(symbol);
                if (plain)
                    _owners.Push(ClassLowering.TagOf(symbol));
                try
                {
                    // 消す宣言はモデルで決めるので、書き換え前のノードで調べる（メンバーの並びは書き換えても変わらない）
                    var remove = new HashSet<int>(node.Members.Select((m, i) => (m, i))
                        .Where(x => x.m is DelegateDeclarationSyntax d && IsSupported(d)).Select(x => x.i));
                    var visited = (TypeDeclarationSyntax)visitBase(node);
                    return remove.Count == 0 ? visited : RemoveNestedDelegates(visited, remove);
                }
                finally
                {
                    if (plain)
                        _owners.Pop();
                }
            }

            /// <summary>
            /// 型の中のデリゲートの宣言を消す（U# は入れ子の型に対応していないため。呼び出しの static クラスは名前空間の直下に置く）。
            /// 宣言の前後の trivia（字下げ・改行・コメント）は次のメンバー（なければ閉じ括弧）に移し、行をずらさない
            /// </summary>
            private TypeDeclarationSyntax RemoveNestedDelegates(TypeDeclarationSyntax node, HashSet<int> remove)
            {
                var members = new List<MemberDeclarationSyntax>();
                var pending = new List<SyntaxTrivia>();
                for (int i = 0; i < node.Members.Count; i++)
                {
                    MemberDeclarationSyntax m = node.Members[i];
                    if (remove.Contains(i) && m is DelegateDeclarationSyntax d)
                    {
                        pending.AddRange(d.GetLeadingTrivia());
                        pending.AddRange(BlankTrivia(d));
                        pending.AddRange(d.GetTrailingTrivia());
                        continue;
                    }
                    members.Add(pending.Count > 0 ? m.WithLeadingTrivia(pending.Concat(m.GetLeadingTrivia())) : m);
                    pending.Clear();
                }
                Changed = true;
                TypeDeclarationSyntax result = node.WithMembers(SyntaxFactory.List(members));
                if (pending.Count > 0)
                    result = result.WithCloseBraceToken(result.CloseBraceToken.WithLeadingTrivia(pending.Concat(result.CloseBraceToken.LeadingTrivia)));
                return result;
            }

            /// <summary>消す宣言の中の改行（複数行の宣言でも行をずらさない）</summary>
            private static IEnumerable<SyntaxTrivia> BlankTrivia(SyntaxNode node)
            {
                int breaks = node.ToString().Count(c => c == '\n');
                for (int i = 0; i < breaks; i++)
                    yield return SyntaxFactory.EndOfLine("\n");
            }

            private bool IsSupported(DelegateDeclarationSyntax d) =>
                _model.GetDeclaredSymbol(d) is INamedTypeSymbol s && _shared.Delegates.TryGetValue(s, out var info) && info.Unsupported == null;

            // ---- フィールド --------------------------------------------------

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                ITypeSymbol type = _model.GetTypeInfo(node.Declaration.Type).Type;
                while (type is IArrayTypeSymbol array)
                    type = array.ElementType;
                if (_shared.Find(type) != null
                    && node.AttributeLists.SelectMany(l => l.Attributes).Any(a => IsSerializationAttribute(_model.GetSymbolInfo(a).Symbol?.ContainingType)))
                {
                    foreach (VariableDeclaratorSyntax v in node.Declaration.Variables)
                        Error(v, $"'{v.Identifier.ValueText}' : デリゲート型のフィールドは、シリアライズ・同期できません（[SerializeField] / [UdonSynced] は付けられません）");
                    return node;
                }

                var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
                // UdonSharpBehaviour のデリゲート型のフィールドは、public でもシリアライズさせない（null で始めるため。#127）
                if (_shared.Find(type) != null && _model.GetDeclaredSymbol(node.Declaration.Variables[0]) is IFieldSymbol field
                    && InterfaceLowering.IsUdonSharpBehaviour(field.ContainingType))
                {
                    FieldDeclarationSyntax result = SerializationFields.MakeNonSerialized(visited);
                    if (!ReferenceEquals(result, visited))
                        Changed = true;
                    return result;
                }
                return visited;
            }

            /// <summary>[NetworkCallable] のメソッドの引数に、デリゲートは使えない（ほかのクライアントには、呼ぶ相手を送れないため）</summary>
            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var visited = (MethodDeclarationSyntax)VisitMethodDeclarationCore(node);
                // ラムダが使う引数（#121）：本体の先頭で環境を作り、引数の値を入れる
                if (!_scopes.TryGetValue(node, out Scope scope) || visited.Body == null && visited.ExpressionBody == null)
                    return visited;
                string prologue = CreateEnv(scope, s => Ident(s.Name));
                if (visited.Body != null)
                    return visited.WithBody(WithPrologue(visited.Body, prologue));

                // 式形式（=> x）は { return x; } にする。行は変えない
                bool isVoid = _model.GetDeclaredSymbol(node) is IMethodSymbol m && m.ReturnsVoid;
                ArrowExpressionClauseSyntax arrow = visited.ExpressionBody;
                var block = (BlockSyntax)SyntaxFactory.ParseStatement($"{{ {prologue} {(isVoid ? "" : "return ")}{arrow.Expression.ToFullString()}; }}");
                block = SourceMap.KeepLineCount(block.WithoutTrivia(), node.ExpressionBody);
                block = block.WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia)
                    .WithTrailingTrivia(block.GetTrailingTrivia().AddRange(visited.SemicolonToken.TrailingTrivia));
                Changed = true;
                return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
            }

            private SyntaxNode VisitMethodDeclarationCore(MethodDeclarationSyntax node)
            {
                if (_model.GetDeclaredSymbol(node) is IMethodSymbol method
                    && method.GetAttributes().Any(a => a.AttributeClass?.Name == "NetworkCallableAttribute"))
                {
                    foreach (IParameterSymbol p in method.Parameters)
                    {
                        ITypeSymbol t = p.Type;
                        while (t is IArrayTypeSymbol a)
                            t = a.ElementType;
                        if (_shared.Find(t) != null)
                            Error(node.ParameterList.Parameters[p.Ordinal], $"'{method.Name}' : [NetworkCallable] のメソッドの引数に、デリゲートは使えません（ほかのクライアントには送れません）");
                    }
                }
                return base.VisitMethodDeclaration(node);
            }

            public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
            {
                if (_shared.Find(_model.GetTypeInfo(node.Declaration.Type).Type) != null)
                {
                    Error(node, "event は未対応です。デリゲート型のフィールドにしてください");
                    return node;
                }
                return base.VisitEventFieldDeclaration(node);
            }

            public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
            {
                if (_shared.Find(_model.GetTypeInfo(node.Type).Type) != null)
                {
                    Error(node, "event は未対応です。デリゲート型のフィールドにしてください");
                    return node;
                }
                return base.VisitEventDeclaration(node);
            }

            private static bool IsSerializationAttribute(INamedTypeSymbol attribute)
            {
                string name = attribute?.ToDisplayString();
                return name == "UnityEngine.SerializeField" || name == "UnityEngine.SerializeReference" || name == "UdonSharp.UdonSyncedAttribute";
            }

            // ---- 型の参照 ----------------------------------------------------

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Parent is QualifiedNameSyntax q && q.Right == node || node.Parent is AliasQualifiedNameSyntax
                    || node.Parent is MemberAccessExpressionSyntax m && m.Name == node)
                    return base.VisitIdentifierName(node);
                return CapturedRead(node) ?? MethodGroup(node) ?? LocalFunctionReference(node) ?? TypeReference(node) ?? base.VisitIdentifierName(node);
            }

            // ---- 外の変数の読み書き（#121） -----------------------------------------

            /// <summary>環境に置いた変数の読み出し：x → ((T)__envN[i])。ラムダの中の定数は値にする</summary>
            private SyntaxNode CapturedRead(IdentifierNameSyntax node)
            {
                if (node.Parent is ArgumentSyntax nameArg && nameArg.Parent?.Parent is InvocationExpressionSyntax inv
                    && inv.Expression is IdentifierNameSyntax nameofId && nameofId.Identifier.ValueText == "nameof")
                    return null;

                ISymbol symbol = _model.GetSymbolInfo(node).Symbol;
                if (symbol is ILocalSymbol constant && constant.IsConst && constant.HasConstantValue
                    && node.Ancestors().Any(l => _needs.ContainsKey(l)
                        && !constant.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == l.SyntaxTree && l.Span.Contains(r.Span))))
                {
                    string value = SymbolDisplay.FormatPrimitive(constant.ConstantValue, true, false) ?? "null";
                    return Parse($"(({_shared.TypeText(constant.Type)})({value}))", node);
                }

                if (symbol == null || !_captured.TryGetValue(symbol, out var slot))
                    return null;
                ITypeSymbol type = VariableType(symbol);
                if (node.Parent is ArgumentSyntax a && !a.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    Error(node, $"'{symbol.Name}' : デリゲートに入れるラムダで使う変数は、ref / out / in で渡せません");
                    return node;
                }
                if (type.IsValueType && IsMemberWrite(node))
                {
                    Error(node, $"'{symbol.Name}' : デリゲートに入れるラムダで使う構造体の変数の、フィールド・プロパティへの代入は未対応です。変数ごと代入してください");
                    return node;
                }
                return Parse($"(({_shared.TypeText(type)}){slot.Scope.Name}[{slot.Index}])", node);
            }

            /// <summary>v.x = ...、v.x++ など、変数のメンバーに書く式の受け側か</summary>
            private static bool IsMemberWrite(ExpressionSyntax node)
            {
                ExpressionSyntax e = node;
                if (!(e.Parent is MemberAccessExpressionSyntax))
                    return false;
                while (e.Parent is MemberAccessExpressionSyntax ma && ma.Expression == e)
                    e = ma;
                while (e.Parent is ParenthesizedExpressionSyntax p)
                    e = p;
                switch (e.Parent)
                {
                    case AssignmentExpressionSyntax asg:
                        return asg.Left == e;
                    case PrefixUnaryExpressionSyntax pre:
                        return pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression);
                    case PostfixUnaryExpressionSyntax _:
                        return true;
                    case ArgumentSyntax arg:
                        return !arg.RefKindKeyword.IsKind(SyntaxKind.None);
                    default:
                        return false;
                }
            }

            private static ExpressionSyntax Unparen(ExpressionSyntax e)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                return e;
            }

            /// <summary>環境に置いた変数への書き込み（target = value）。式の値を使うところでは、値を T に戻す</summary>
            private SyntaxNode CapturedWrite(ExpressionSyntax node, ISymbol symbol, (Scope Scope, int Index) slot, string value)
            {
                string type = _shared.TypeText(VariableType(symbol));
                string assign = $"{slot.Scope.Name}[{slot.Index}] = ({type})({value})";
                return Parse(InterfaceLowering.IsStatementExpression(node, _model) ? assign : $"(({type})({assign}))", node);
            }

            private static string CompoundOperator(SyntaxKind kind)
            {
                switch (kind)
                {
                    case SyntaxKind.AddAssignmentExpression: return "+";
                    case SyntaxKind.SubtractAssignmentExpression: return "-";
                    case SyntaxKind.MultiplyAssignmentExpression: return "*";
                    case SyntaxKind.DivideAssignmentExpression: return "/";
                    case SyntaxKind.ModuloAssignmentExpression: return "%";
                    case SyntaxKind.AndAssignmentExpression: return "&";
                    case SyntaxKind.OrAssignmentExpression: return "|";
                    case SyntaxKind.ExclusiveOrAssignmentExpression: return "^";
                    case SyntaxKind.LeftShiftAssignmentExpression: return "<<";
                    case SyntaxKind.RightShiftAssignmentExpression: return ">>";
                    case SyntaxKind.CoalesceAssignmentExpression: return "??";
                    default: return null;
                }
            }

            public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            {
                if ((node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression))
                    && Unparen(node.Operand) is IdentifierNameSyntax id && TryCaptured(id, out ISymbol symbol, out var slot))
                {
                    string type = _shared.TypeText(VariableType(symbol));
                    string sign = node.IsKind(SyntaxKind.PreIncrementExpression) ? "+" : "-";
                    return CapturedWrite(node, symbol, slot, $"(({type}){slot.Scope.Name}[{slot.Index}]) {sign} 1");
                }
                return base.VisitPrefixUnaryExpression(node);
            }

            public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
            {
                if ((node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression))
                    && Unparen(node.Operand) is IdentifierNameSyntax id && TryCaptured(id, out ISymbol symbol, out var slot))
                {
                    string type = _shared.TypeText(VariableType(symbol));
                    bool inc = node.IsKind(SyntaxKind.PostIncrementExpression);
                    string target = $"{slot.Scope.Name}[{slot.Index}]";
                    string assign = $"{target} = ({type})((({type}){target}) {(inc ? "+" : "-")} 1)";
                    // 値を使うところでは、足したあとの値から戻す（x++ の値は足す前の値）
                    return Parse(InterfaceLowering.IsStatementExpression(node, _model) ? assign
                        : $"(({type})((({type})({assign})) {(inc ? "-" : "+")} 1))", node);
                }
                return base.VisitPostfixUnaryExpression(node);
            }

            /// <summary>環境に置いた変数の宣言を、要素への代入にする（int a = 1, b = 2; → __env0[0] = (int)(1); int b = 2;）</summary>
            private List<StatementSyntax> SplitDeclaration(LocalDeclarationStatementSyntax node)
            {
                var parts = new List<string>();
                string typeText = null;
                foreach (VariableDeclaratorSyntax v in node.Declaration.Variables)
                {
                    ExpressionSyntax init = v.Initializer?.Value;
                    if (TryCaptured(v, out ISymbol symbol, out var slot))
                    {
                        string type = _shared.TypeText(VariableType(symbol));
                        string value = init == null ? $"default({type})"
                            : init is InitializerExpressionSyntax ? $"new {type} {Text(Visit(init))}"
                            : Text(Visit(init));
                        parts.Add($"{slot.Scope.Name}[{slot.Index}] = ({type})({value});");
                    }
                    else
                    {
                        typeText = typeText ?? Text(Visit(node.Declaration.Type));
                        parts.Add($"{typeText} {v.Identifier.Text}{(init != null ? " = " + Text(Visit(init)) : "")};");
                    }
                }

                var result = parts.Select((p, i) => SyntaxFactory.ParseStatement(p).WithLeadingTrivia(i == 0 ? SyntaxFactory.TriviaList() : SyntaxFactory.TriviaList(SyntaxFactory.Space))).ToList();
                SyntaxTriviaList leading = node.GetLeadingTrivia();
                if (_statementMarkers != null && _statementMarkers.TryGetValue(node, out string marker))
                    leading = leading.Add(SyntaxFactory.Comment(marker));
                result[0] = result[0].WithLeadingTrivia(leading);
                // 宣言の途中の改行（int a = 1,\n b = 2; など）で減った行を、うしろに足す
                FileLinePositionSpan span = node.GetLocation().GetLineSpan();
                int missing = span.EndLinePosition.Line - span.StartLinePosition.Line - parts.Sum(p => p.Count(c => c == '\n'));
                var trailing = Enumerable.Repeat(SyntaxFactory.EndOfLine("\n"), Math.Max(0, missing)).Concat(node.GetTrailingTrivia());
                result[result.Count - 1] = result[result.Count - 1].WithTrailingTrivia(trailing);
                Changed = true;
                return result;
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                bool hasScope = _scopes.TryGetValue(node, out Scope scope);
                if (!hasScope && !node.Statements.Any(s => s is LocalFunctionStatementSyntax))
                    return base.VisitBlock(node);
                var statements = new List<StatementSyntax>();
                var pending = new List<SyntaxTrivia>();
                foreach (StatementSyntax s in node.Statements)
                {
                    // ローカル関数はメソッドに移し、ここからは消す。前後の trivia は次の文（なければ閉じ括弧）に回し、行をずらさない
                    if (s is LocalFunctionStatementSyntax lf)
                    {
                        Hoist(lf);
                        pending.AddRange(lf.GetLeadingTrivia());
                        pending.AddRange(BlankTrivia(lf));
                        pending.AddRange(lf.GetTrailingTrivia());
                        continue;
                    }
                    List<StatementSyntax> produced = s is LocalDeclarationStatementSyntax l && l.Declaration.Variables.Any(v => TryCaptured(v, out _, out _))
                        ? SplitDeclaration(l)
                        : new List<StatementSyntax> { (StatementSyntax)Visit(s) };
                    if (pending.Count > 0)
                    {
                        produced[0] = produced[0].WithLeadingTrivia(pending.Concat(produced[0].GetLeadingTrivia()));
                        pending.Clear();
                    }
                    statements.AddRange(produced);
                }
                Changed = true;
                BlockSyntax result = node.WithStatements(SyntaxFactory.List(statements));
                if (pending.Count > 0)
                    result = result.WithCloseBraceToken(result.CloseBraceToken.WithLeadingTrivia(pending.Concat(result.CloseBraceToken.LeadingTrivia)));
                return hasScope ? WithPrologue(result, CreateEnv(scope)) : result;
            }

            // ---- ローカル関数（#123） -----------------------------------------------

            /// <summary>
            /// ローカル関数を、同じクラスのメソッドに移す（閉じ括弧の行に置く。本体の文ごとに元の位置の印を付ける）：
            ///   [属性] private (static) R __lf_F__t_n(object[] __env0, ..., 元の引数) { 本体 }
            /// 外の変数は、使う環境を同じ名前の引数で受け取るので、本体はそのまま ((T)__env0[i]) で読み書きできる
            /// </summary>
            private void Hoist(LocalFunctionStatementSyntax node)
            {
                if (!(LocalFuncOf(_model.GetDeclaredSymbol(node)) is LocalFunc local) || _funcErrors.Contains(node))
                    return;
                string name = node.Identifier.ValueText;
                if (node.TypeParameterList != null)
                {
                    Error(node, $"'{name}' : ジェネリックのローカル関数は未対応です。メソッドにしてください");
                    return;
                }
                if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword) || m.IsKind(SyntaxKind.ExternKeyword) || m.IsKind(SyntaxKind.UnsafeKeyword)))
                {
                    Error(node, $"'{name}' : async・extern・unsafe のローカル関数は未対応です");
                    return;
                }

                CSharpSyntaxNode body = (CSharpSyntaxNode)node.Body ?? node.ExpressionBody?.Expression;
                if (body == null)
                    return;
                string bodyText = LambdaBodyText(body, local.Symbol);
                if (bodyText == null)
                    return;
                // ローカル関数の引数を中のラムダ・ローカル関数が使うなら、本体の先頭で環境を作る
                if (_scopes.TryGetValue(node, out Scope own))
                    bodyText = "{ " + CreateEnv(own, s => Ident(s.Name)) + " " + bodyText.Substring(1).TrimStart();

                List<Scope> needs = _needs.TryGetValue(node, out var n) ? n : new List<Scope>();
                string returnType = Flatten(Visit(node.ReturnType), node.ReturnType);
                string parameterList = Flatten(Visit(node.ParameterList), node.ParameterList);
                if (returnType == null || parameterList == null)
                    return;
                string userParameters = parameterList.Substring(1, parameterList.Length - 2).Trim();
                string parameters = string.Join(", ", needs.Select(s => $"{ValueType} {s.Name}")
                    .Concat(userParameters.Length > 0 ? new[] { userParameters } : new string[0]));

                var attributes = new StringBuilder();
                foreach (AttributeListSyntax list in node.AttributeLists)
                    attributes.Append(Flatten(Visit(list), list)).Append(' ');
                if (local.Recursive && !node.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("RecursiveMethod")))
                    attributes.Append("[global::UdonSharp.RecursiveMethod] ");

                string code = $"{SourceMap.MarkerFor(node)}{attributes}private {(local.IsStatic ? "static " : "")}{returnType} {local.Hoisted}({parameters}) {bodyText}";
                HostInfo entry = _shared.HostOf(local.Symbol.ContainingType);
                entry.Members[local.Hoisted] = (code, DeclOf(node));
                Changed = true;
            }

            /// <summary>ブロックの直下でないローカル関数（switch のラベルの直下など）</summary>
            public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                Error(node, $"'{node.Identifier.ValueText}' : この場所のローカル関数は未対応です。{{ }} で囲んでください");
                return node;
            }

            /// <summary>ローカル関数の呼び出し F(x) → __lf_F__t_n(環境..., x)</summary>
            private SyntaxNode LocalFunctionCall(InvocationExpressionSyntax node, LocalFunc callee)
            {
                List<Scope> needs = _needs.TryGetValue(callee.Node, out var n) ? n : new List<Scope>();
                var args = needs.Select(s => s.Name).Concat(node.ArgumentList.Arguments.Select(a => Text(Visit(a))));
                return Parse($"{callee.Hoisted}({string.Join(", ", args)})", node);
            }

            /// <summary>
            /// 呼び出し・デリゲートへの変換のほかでローカル関数の名前を使うところ（List・LINQ のメソッドに渡す list.ForEach(F) など）。
            /// 外の変数を使わなければ、移したメソッドの名前にする
            /// </summary>
            private SyntaxNode LocalFunctionReference(IdentifierNameSyntax node)
            {
                if (!(LocalFuncOf(_model.GetSymbolInfo(node).Symbol) is LocalFunc local))
                    return null;
                if (node.Parent is ArgumentSyntax nameArg && nameArg.Parent?.Parent is InvocationExpressionSyntax inv
                    && inv.Expression is IdentifierNameSyntax nameofId && nameofId.Identifier.ValueText == "nameof")
                    return null;
                if (_needs.TryGetValue(local.Node, out var needs) && needs.Count > 0)
                {
                    Error(node, $"'{node.Identifier.ValueText}' : 外の変数を使うローカル関数を、ここで使うことは未対応です（呼ぶか、ソースで宣言したデリゲートに入れてください）");
                    return node;
                }
                return Parse(local.Hoisted, node);
            }

            public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            {
                if (!_scopes.TryGetValue(node, out Scope scope))
                    return base.VisitForEachStatement(node);
                // foreach の変数は 1 回ごとに別の変数（C# 5 から）なので、1 回ごとに環境を作り、名前を変えた変数から入れる
                string loopVar = scope.Name.Replace("__env", "__fv");
                var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
                visited = visited.WithIdentifier(SyntaxFactory.Identifier(loopVar).WithTriviaFrom(visited.Identifier));
                return visited.WithStatement(WithPrologue(visited.Statement, CreateEnv(scope, _ => loopVar)));
            }

            /// <summary>
            /// 本体の先頭に文を足す。ブロックなら「{」と同じ行に置く（「{」のうしろの改行はそのうしろに回す。次の行が #if などでもよいように）。
            /// ブロックでなければ { ... } で囲む（同じ行のまま）
            /// </summary>
            private BlockSyntax WithPrologue(StatementSyntax body, string prologue)
            {
                Changed = true;
                var added = ((BlockSyntax)SyntaxFactory.ParseStatement("{ " + prologue + " }")).Statements
                    .Select(s => s.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.TriviaList())).ToList();
                if (body is BlockSyntax block)
                {
                    SyntaxTriviaList afterBrace = block.OpenBraceToken.TrailingTrivia;
                    added[added.Count - 1] = added[added.Count - 1].WithTrailingTrivia(afterBrace);
                    return block
                        .WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.TriviaList()))
                        .WithStatements(SyntaxFactory.List(added.Concat(block.Statements)));
                }
                StatementSyntax inner = body.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.TriviaList());
                return SyntaxFactory.Block(SyntaxFactory.List(added.Concat(new[] { inner })))
                    .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(body.GetLeadingTrivia()))
                    .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(body.GetTrailingTrivia()));
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node) => TypeReference(node) ?? base.VisitQualifiedName(node);

            public override SyntaxNode VisitGenericName(GenericNameSyntax node) =>
                node.Parent is QualifiedNameSyntax q && q.Right == node ? base.VisitGenericName(node) : TypeReference(node) ?? base.VisitGenericName(node);

            /// <summary>
            /// デリゲートの配列：Hoge[] → object[][]、new Hoge[2] → new object[2][]
            /// （要素の object[] の [] は、大きさを書く [] のうしろに来る）
            /// </summary>
            public override SyntaxNode VisitArrayType(ArrayTypeSyntax node)
            {
                if (!(_model.GetTypeInfo(node.ElementType).Type is ITypeSymbol element) || _shared.Find(element) == null || _shared.Find(element).Unsupported != null)
                    return base.VisitArrayType(node);
                var ranks = node.RankSpecifiers.Select(r => (ArrayRankSpecifierSyntax)Visit(r)).ToList();
                Changed = true;
                return SyntaxFactory.ParseTypeName("object" + string.Concat(ranks.Select(r => r.ToString())) + "[]").WithTriviaFrom(node);
            }

            public override SyntaxNode VisitAliasQualifiedName(AliasQualifiedNameSyntax node) => TypeReference(node) ?? base.VisitAliasQualifiedName(node);

            // using H = N.Hoge; の右側は名前しか書けないので置き換えない（使う側の H は型の参照として置き換わる）
            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) => node;

            /// <summary>デリゲート型の参照を object[] にする。置き換えられない位置ならエラー</summary>
            private SyntaxNode TypeReference(NameSyntax node)
            {
                if (!(_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type) || !_shared.Delegates.TryGetValue(type.OriginalDefinition, out DelegateInfo info))
                    return null;
                if (info.Unsupported != null)
                {
                    Error(node, $"'{type.Name}' : {info.Unsupported}");
                    return node;
                }

                SyntaxNode context = node.Parent;
                if (context is TypeArgumentListSyntax)
                {
                    Error(node, $"'{type.Name}' : デリゲート型を型引数（List<{type.Name}> など）に使うことは、まだ未対応です。配列にしてください");
                    return node;
                }
                if (context is TypeOfExpressionSyntax || context is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.IsExpression) || b.IsKind(SyntaxKind.AsExpression))
                    || context is TypePatternSyntax || context is DeclarationPatternSyntax || context is RecursivePatternSyntax
                    || context is ArgumentSyntax arg && arg.Parent?.Parent is InvocationExpressionSyntax inv && inv.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof")
                {
                    Error(node, $"'{type.Name}' : デリゲート型の typeof・nameof・is・as・型パターンは未対応です");
                    return node;
                }

                Changed = true;
                return SyntaxFactory.ParseTypeName(ValueType).WithTriviaFrom(node);
            }

            // ---- 作る（メソッド名から） ----------------------------------------

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                SyntaxNode group = MethodGroup(node);
                if (group != null)
                    return group;

                // h.Invoke 以外のデリゲートのメンバー（Method・Target・GetInvocationList・DynamicInvoke など）
                if (DelegateOf(node.Expression) is DelegateInfo && node.Name.Identifier.ValueText != "Invoke")
                {
                    Error(node, $"'{node.Name.Identifier.ValueText}' : デリゲートのメンバーは、Invoke のほかは未対応です");
                    return node;
                }
                return base.VisitMemberAccessExpression(node);
            }

            /// <summary>
            /// ソースのデリゲート型に変換されるメソッド名（h = F; M(F); return F; new Hoge(F)）なら、デリゲートの値を作る式にする。
            /// そうでなければ null
            /// </summary>
            private SyntaxNode MethodGroup(ExpressionSyntax node)
            {
                Conversion conversion = _model.GetConversion(node);
                if (!conversion.IsMethodGroup)
                    return null;
                DelegateInfo info = _shared.Find(_model.GetTypeInfo(node).ConvertedType);
                if (info == null || !(conversion.MethodSymbol is IMethodSymbol method))
                    return null;
                return Create(node, info, method);
            }

            private SyntaxNode Create(ExpressionSyntax node, DelegateInfo info, IMethodSymbol method)
            {
                string name = method.Name;
                if (info.Unsupported != null)
                {
                    Error(node, $"'{info.Symbol.Name}' : {info.Unsupported}");
                    return node;
                }

                // h.Invoke（同じデリゲートの Invoke）は、h そのもの
                if (method.MethodKind == MethodKind.DelegateInvoke && _shared.Find(method.ContainingType) == info && node is MemberAccessExpressionSyntax self)
                    return Visit(self.Expression);

                if (method.IsGenericMethod)
                {
                    Error(node, $"'{name}' : ジェネリックメソッドをデリゲートに入れることは未対応です");
                    return node;
                }
                if (method.MethodKind == MethodKind.DelegateInvoke || method.ReducedFrom != null || method.IsExtensionMethod && !method.IsStatic)
                {
                    Error(node, $"'{name}' : このメソッドをデリゲートに入れることは、まだ未対応です");
                    return node;
                }

                // 作る場所：UdonSharpBehaviour のインスタンスのメソッドの中（this を呼ぶ相手にする）
                INamedTypeSymbol host = HostOf(node, out string hostError);
                if (host == null)
                {
                    Error(node, $"'{name}' : {hostError}");
                    return node;
                }

                if (LocalFuncOf(method) is LocalFunc local)
                    return CreateFromLocalFunction(node, info, local, host);

                string callee;
                if (method.IsStatic)
                {
                    callee = $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{name}";
                }
                else
                {
                    bool ownMember = node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax;
                    if (!ownMember || !InterfaceLowering.IsUdonSharpBehaviour(method.ContainingType))
                    {
                        Error(node, $"'{name}' : ほかのインスタンスのメソッド（クラスのメソッド）をデリゲートに入れることは、まだ未対応です。static メソッドか、この UdonSharpBehaviour のメソッドにしてください");
                        return node;
                    }
                    callee = $"this.{name}";
                }

                string trampoline = $"__dlg_{info.Key}__{InterfaceNaming.Sanitize(ClassLowering.TagOf(method.ContainingType))}_{name}__{InterfaceNaming.Sanitize(ClassLowering.TagOf(host))}";
                HostInfo entry = _shared.HostOf(host);
                entry.Delegates.Add(info);
                if (!entry.Members.ContainsKey(trampoline))
                    entry.Members[trampoline] = (TrampolineCode(info, trampoline, SourceMap.MarkerFor(node), callee), DeclOf(node));
                return Parse($"new {ValueType} {{ this, \"{trampoline}\", null }}", node);
            }

            /// <summary>
            /// ローカル関数から作る（#123）：中継メソッドが、受け取った環境の一覧（__dlg_K__env）から、移したメソッドの環境の引数を取り出して呼ぶ
            ///   public void __dlg_K__lf_F__t_n__C() { ret = this.__lf_F__t_n((object[])__dlg_K__env[0], ..., p0); }
            /// </summary>
            private SyntaxNode CreateFromLocalFunction(ExpressionSyntax node, DelegateInfo info, LocalFunc local, INamedTypeSymbol host)
            {
                if (_funcErrors.Contains(local.Node))
                    return node;
                List<Scope> needs = _needs.TryGetValue(local.Node, out var n) ? n : new List<Scope>();
                IMethodSymbol invoke = info.Invoke;
                string trampoline = $"__dlg_{info.Key}_{local.Hoisted}__{InterfaceNaming.Sanitize(ClassLowering.TagOf(host))}";
                HostInfo entry = _shared.HostOf(host);
                entry.Delegates.Add(info);
                if (!entry.Members.ContainsKey(trampoline))
                {
                    string args = string.Join(", ", needs.Select((s, j) => $"({ValueType}){info.EnvField}[{j}]")
                        .Concat(Enumerable.Range(0, invoke.Parameters.Length).Select(info.ParamField)));
                    string assign = invoke.ReturnsVoid ? "" : info.RetField + " = ";
                    string callee = local.IsStatic ? local.Hoisted : "this." + local.Hoisted;
                    entry.Members[trampoline] = ($"public void {trampoline}() {{ {SourceMap.MarkerFor(node)}{assign}{callee}({args}); }}", DeclOf(node));
                }
                string env = needs.Count > 0 ? $"new {ValueType} {{ {string.Join(", ", needs.Select(s => s.Name))} }}" : "null";
                return Parse($"new {ValueType} {{ this, \"{trampoline}\", {env} }}", node);
            }

            /// <summary>中継メソッド：public void T() { ret = callee(p0, p1); }</summary>
            private static string TrampolineCode(DelegateInfo info, string name, string marker, string callee, bool env = false)
            {
                IMethodSymbol invoke = info.Invoke;
                string args = string.Join(", ", (env ? new[] { info.EnvField } : new string[0])
                    .Concat(Enumerable.Range(0, invoke.Parameters.Length).Select(info.ParamField)));
                string assign = invoke.ReturnsVoid ? "" : info.RetField + " = ";
                return $"public void {name}() {{ {marker}{assign}{callee}({args}); }}";
            }

            // ---- ラムダ（#120） -------------------------------------------------

            /// <summary>
            /// ソースのデリゲート型に入れるラムダ：本体をメソッドにし（引数はラムダの引数）、それを呼ぶ中継メソッドを置く。
            ///   private R __dlg_K__lambdaN__C__body(T0 x) { ... }
            ///   public void __dlg_K__lambdaN__C() { ret = __dlg_K__lambdaN__C__body(p0); }
            /// 外のローカル変数・引数を使うもの（クロージャ）は、まだ未対応（#121）
            /// </summary>
            private SyntaxNode CreateLambda(AnonymousFunctionExpressionSyntax node, DelegateInfo info)
            {
                if (info.Unsupported != null)
                {
                    Error(node, $"'{info.Symbol.Name}' : {info.Unsupported}");
                    return node;
                }
                if (node.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                {
                    Error(node, "async のラムダ・匿名メソッドは未対応です");
                    return node;
                }
                INamedTypeSymbol host = HostOf(node, out string hostError);
                if (host == null)
                {
                    Error(node, hostError.Replace("デリゲートを作る", node is AnonymousMethodExpressionSyntax ? "匿名メソッドをデリゲートに入れる" : "ラムダをデリゲートに入れる"));
                    return node;
                }

                IMethodSymbol invoke = info.Invoke;
                string[] names;
                CSharpSyntaxNode body;
                switch (node)
                {
                    case SimpleLambdaExpressionSyntax s:
                        names = new[] { s.Parameter.Identifier.ValueText };
                        body = s.Body;
                        break;
                    case ParenthesizedLambdaExpressionSyntax p:
                        names = p.ParameterList.Parameters.Select(x => x.Identifier.ValueText).ToArray();
                        body = p.Body;
                        break;
                    case AnonymousMethodExpressionSyntax m:
                        // delegate { ... } は引数を書かなくてよい（どの引数のデリゲートにも入る。本体からは使えない）
                        names = m.ParameterList != null
                            ? m.ParameterList.Parameters.Select(x => x.Identifier.ValueText).ToArray()
                            : Enumerable.Range(0, invoke.Parameters.Length).Select(i => "__a" + i).ToArray();
                        body = m.Block;
                        break;
                    default:
                        return node;
                }
                if (names.Length != invoke.Parameters.Length)
                    return node;   // C# のエラーになる
                // 捨てる引数（(_, _) => ...）は名前が重なるので付け直す（本体からは使えない）
                if (names.Count(n => n == "_") > 1)
                    for (int i = 0; i < names.Length; i++)
                        if (names[i] == "_")
                            names[i] = "__a" + i;

                if (_funcErrors.Contains(node))
                    return node;   // 下調べでエラーにした
                List<Scope> outer = _needs.TryGetValue(node, out var o) ? o : new List<Scope>();
                _scopes.TryGetValue(node, out Scope own);

                HostInfo entry = _shared.HostOf(host);
                entry.Delegates.Add(info);
                string name = $"__dlg_{info.Key}__lambda{entry.LambdaCount++}__{InterfaceNaming.Sanitize(ClassLowering.TagOf(host))}";
                string bodyName = name + "__body";

                // ラムダは名前がないので、自分を呼ぶ（再帰する）にはデリゲートを通すしかない。デリゲートを呼ぶラムダは、
                // 再帰しても引数・ローカル変数が壊れないよう [RecursiveMethod] にする（ラムダには属性を書けないため）
                bool recursive = body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(inv =>
                    DelegateOf(inv.Expression) != null
                    || inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Invoke" && DelegateOf(ma.Expression) != null);

                string bodyText = LambdaBodyText(body, invoke);
                if (bodyText == null)
                    return node;
                // 本体の先頭：受け取った環境を、作った側と同じ名前の変数に取り出す。ラムダの引数を中のラムダが使うなら、その環境を作る
                var prologue = new StringBuilder();
                for (int j = 0; j < outer.Count; j++)
                    prologue.Append($"object[] {outer[j].Name} = ({ValueType})__env[{j}]; ");
                if (own != null)
                    prologue.Append(CreateEnv(own, s => Ident(names[((IParameterSymbol)s).Ordinal])) + " ");
                if (prologue.Length > 0)
                    bodyText = "{ " + prologue + bodyText.Substring(1).TrimStart();

                string ret = invoke.ReturnsVoid ? "void" : _shared.TypeText(invoke.ReturnType);
                string parameters = string.Join(", ", (outer.Count > 0 ? new[] { $"{ValueType} __env" } : new string[0])
                    .Concat(names.Select((n, i) => $"{_shared.TypeText(invoke.Parameters[i].Type)} {n}")));
                string marker = SourceMap.MarkerFor(node);
                string attribute = recursive ? "[global::UdonSharp.RecursiveMethod] " : "";
                entry.Members[bodyName] = ($"{attribute}private {ret} {bodyName}({parameters}) {bodyText}", DeclOf(node));
                entry.Members[name] = (TrampolineCode(info, name, marker, "this." + bodyName, outer.Count > 0), DeclOf(node));
                string env = outer.Count > 0 ? $"new {ValueType} {{ {string.Join(", ", outer.Select(s => s.Name))} }}" : "null";
                return Parse($"new {ValueType} {{ this, \"{name}\", {env} }}", node);
            }

            /// <summary>ラムダの本体を、1 行のメソッドの本体（{ ... }）にする。文ごとに元の位置の印を付ける</summary>
            private string LambdaBodyText(CSharpSyntaxNode body, IMethodSymbol invoke)
            {
                var markers = new Dictionary<SyntaxNode, string>();
                SyntaxNode visited;
                Dictionary<SyntaxNode, string> saved = _statementMarkers;
                if (body is BlockSyntax block)
                {
                    foreach (StatementSyntax s in block.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax)).OfType<StatementSyntax>())
                        if (!(s is BlockSyntax))
                            markers[s] = SourceMap.MarkerFor(s);
                    _statementMarkers = markers;
                    try { visited = Visit(block); }
                    finally { _statementMarkers = saved; }
                    return Flatten(visited, body);
                }

                var expression = (ExpressionSyntax)body;
                _statementMarkers = null;
                try { visited = Visit(expression); }
                finally { _statementMarkers = saved; }
                string text = Flatten(visited, body);
                if (text == null)
                    return null;
                string marker = SourceMap.MarkerFor(expression);
                return invoke.ReturnsVoid ? $"{{ {marker}{text}; }}" : $"{{ {marker}return {text}; }}";
            }

            /// <summary>
            /// 生成メソッドに入れるため 1 行にする（生成メンバーはクラスの閉じ括弧の行に置くので、改行があると後ろの行がずれる）。
            /// 位置の印は残す。だめなときは null
            /// </summary>
            private string Flatten(SyntaxNode node, SyntaxNode original)
            {
                // プリプロセッサ指令と #if の無効な側は trivia なので、空白になり、有効な側のコードだけが残る
                SyntaxNode flat = node.ReplaceTokens(
                    node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken) && t.Text.Contains("\n")),
                    (o, _) => SyntaxFactory.Literal(o.ValueText).WithTriviaFrom(o));
                flat = flat.ReplaceTrivia(flat.DescendantTrivia(descendIntoTrivia: false),
                    (o, _) => SourceMap.IsMarker(o) ? o : SyntaxFactory.Space);
                string text = flat.ToFullString().Trim();
                if (text.Contains("\n"))
                {
                    Error(original, "ラムダの中の、改行を含む補間文字列は未対応です");
                    return null;
                }
                return text;
            }

            /// <summary>デリゲートを作れる場所なら、その UdonSharpBehaviour。そうでなければ null と理由</summary>
            private INamedTypeSymbol HostOf(SyntaxNode node, out string error)
            {
                ISymbol s = _model.GetEnclosingSymbol(node.SpanStart);
                error = null;
                while (s is IMethodSymbol m && (m.MethodKind == MethodKind.AnonymousFunction || m.MethodKind == MethodKind.LocalFunction))
                {
                    if (m.MethodKind == MethodKind.LocalFunction && m.IsStatic)
                    {
                        error = "static のローカル関数の中でデリゲートを作ることは未対応です";
                        return null;
                    }
                    s = s.ContainingSymbol;
                }
                if (!(s is IMethodSymbol enclosing))
                {
                    error = "フィールドの初期化子の中でデリゲートを作ることは未対応です。Start などで代入してください";
                    return null;
                }
                if (!InterfaceLowering.IsUdonSharpBehaviour(enclosing.ContainingType))
                {
                    error = "普通のクラスの中でデリゲートを作ることは、まだ未対応です。UdonSharpBehaviour の中で作って渡してください";
                    return null;
                }
                if (enclosing.IsStatic)
                {
                    error = "static メソッドの中でデリゲートを作ることは未対応です";
                    return null;
                }
                return enclosing.ContainingType;
            }

            /// <summary>(Hoge)F：キャストの中のメソッド名</summary>
            public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
            {
                DelegateInfo info = _shared.Find(_model.GetTypeInfo(node.Type).Type);
                ExpressionSyntax operand = node.Expression is ParenthesizedExpressionSyntax p ? p.Expression : node.Expression;
                if (info != null && !(operand is InvocationExpressionSyntax) && _model.GetSymbolInfo(operand).Symbol is IMethodSymbol method)
                {
                    SyntaxNode created = Create(operand, info, method);
                    return created == operand ? node : created.WithTriviaFrom(node);
                }
                return base.VisitCastExpression(node);
            }

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                DelegateInfo info = _shared.Find(_model.GetTypeInfo(node).Type);
                if (info == null)
                    return base.VisitObjectCreationExpression(node);

                // new Hoge(F)・new Hoge(h)
                ExpressionSyntax arg = node.ArgumentList?.Arguments.Count == 1 ? node.ArgumentList.Arguments[0].Expression : null;
                if (arg is LambdaExpressionSyntax || arg is AnonymousMethodExpressionSyntax)
                    return Visit(arg).WithTriviaFrom(node);
                if (arg != null && _model.GetSymbolInfo(arg).Symbol is IMethodSymbol method)
                {
                    SyntaxNode created = Create(arg, info, method);
                    return created == arg ? node : created.WithTriviaFrom(node);
                }
                if (arg != null && DelegateOf(arg) == info)
                    return Visit(arg).WithTriviaFrom(node);

                Error(node, $"'{info.Symbol.Name}' : この形の new は未対応です");
                return node;
            }

            // ---- ラムダ・匿名メソッド（#120・#123 で対応） -----------------------

            public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Lambda(node) ?? base.VisitSimpleLambdaExpression(node);
            public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Lambda(node) ?? base.VisitParenthesizedLambdaExpression(node);
            public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => Lambda(node) ?? base.VisitAnonymousMethodExpression(node);

            private SyntaxNode Lambda(ExpressionSyntax node)
            {
                DelegateInfo info = _shared.Find(_model.GetTypeInfo(node).ConvertedType);
                if (info == null)
                    return null;
                SyntaxNode created = CreateLambda((AnonymousFunctionExpressionSyntax)node, info);
                if (created != node)
                    return created;
                // エラーにしたもの：後の段（List・LINQ のラムダの書き換え）で同じ場所に別のエラーを出さないよう、null にしておく
                return Parse("null", node);
            }

            // ---- 呼び出し ----------------------------------------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (node.Expression is IdentifierNameSyntax localId && LocalFuncOf(_model.GetSymbolInfo(localId).Symbol) is LocalFunc callee)
                    return LocalFunctionCall(node, callee);
                ExpressionSyntax target = null;
                DelegateInfo info = DelegateOf(node.Expression);
                if (info != null)
                    target = node.Expression;
                else if (node.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Invoke" && DelegateOf(ma.Expression) is DelegateInfo viaInvoke)
                {
                    info = viaInvoke;
                    target = ma.Expression;
                }
                if (info == null)
                    return base.VisitInvocationExpression(node);

                if (info.Unsupported != null)
                {
                    Error(node, $"'{info.Symbol.Name}' : {info.Unsupported}");
                    return node;
                }
                if (node.ArgumentList.Arguments.Any(a => a.NameColon != null || !a.RefKindKeyword.IsKind(SyntaxKind.None)))
                {
                    Error(node, "デリゲートの呼び出しの、名前付きの引数は未対応です");
                    return node;
                }

                string receiver = Text(Visit(target));
                // 受け側が変数・フィールドなら、呼び出した行で null かを確かめる（そうでなければ、static クラスの中の __d.Length で止まる）
                if (IsPure(target))
                    receiver = $"((({receiver}).Length < 0) ? null : ({receiver}))";
                var args = node.ArgumentList.Arguments.Select(a => Text(Visit(a.Expression)));
                return Parse($"{info.HelperFull}.__invoke({string.Join(", ", new[] { receiver }.Concat(args))})", node);
            }

            private static bool IsPure(ExpressionSyntax e)
            {
                switch (e)
                {
                    case IdentifierNameSyntax _:
                        return true;
                    case MemberAccessExpressionSyntax ma when ma.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                        return ma.Expression is ThisExpressionSyntax || IsPure(ma.Expression);
                    case ParenthesizedExpressionSyntax p:
                        return IsPure(p.Expression);
                    default:
                        return false;
                }
            }

            public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
            {
                if (DelegateOf(node.Expression) is DelegateInfo info)
                {
                    Error(node, $"'{info.Symbol.Name}' : ?.Invoke は未対応です（?. を UdonSharp Plus が扱えないため）。if (h != null) h(...); と書いてください");
                    return node;
                }
                return base.VisitConditionalAccessExpression(node);
            }

            // ---- 演算子 ------------------------------------------------------

            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                bool combine = node.IsKind(SyntaxKind.AddAssignmentExpression) || node.IsKind(SyntaxKind.SubtractAssignmentExpression);
                if (combine && DelegateOf(node.Left) is DelegateInfo info)
                    return CombineAssignment(node, info);
                if (Unparen(node.Left) is IdentifierNameSyntax id && TryCaptured(id, out ISymbol symbol, out var slot))
                {
                    string right = Text(Visit(node.Right));
                    if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        return CapturedWrite(node, symbol, slot, right);
                    string op = CompoundOperator(node.Kind());
                    if (op != null)
                        return CapturedWrite(node, symbol, slot, $"(({_shared.TypeText(VariableType(symbol))}){slot.Scope.Name}[{slot.Index}]) {op} ({right})");
                }
                return base.VisitAssignmentExpression(node);
            }

            /// <summary>
            /// h += x → h = __dlg_K.__combine(h, x)、h -= x → h = __dlg_K.__remove(h, x)（#122）。
            /// 受け側は 2 回書くので、何度評価しても同じもの（変数・フィールド・プロパティ・定数の添字の配列の要素など）に限る
            /// </summary>
            private SyntaxNode CombineAssignment(AssignmentExpressionSyntax node, DelegateInfo info)
            {
                if (info.Unsupported != null)
                {
                    Error(node, $"'{info.Symbol.Name}' : {info.Unsupported}");
                    return node;
                }
                string helper = $"{info.HelperFull}.{(node.IsKind(SyntaxKind.AddAssignmentExpression) ? "__combine" : "__remove")}";
                string right = Text(Visit(node.Right));
                if (Unparen(node.Left) is IdentifierNameSyntax id && TryCaptured(id, out ISymbol symbol, out var slot))
                    return CapturedWrite(node, symbol, slot, $"{helper}(({ValueType}){slot.Scope.Name}[{slot.Index}], {right})");
                if (!IsRepeatable(node.Left))
                {
                    Error(node.Left, $"'{info.Symbol.Name}' : この形の受け側への += / -= は未対応です（受け側を 2 回評価するため）。いったん変数に入れてください");
                    return node;
                }
                string left = Text(Visit(node.Left));
                return Parse($"{left} = {helper}({left}, {right})", node);
            }

            /// <summary>何度評価しても結果と副作用が変わらない受け側か（変数・フィールド・プロパティ、添字が定数か変数の配列の要素）</summary>
            private bool IsRepeatable(ExpressionSyntax e)
            {
                switch (e)
                {
                    case IdentifierNameSyntax _:
                    case ThisExpressionSyntax _:
                    case BaseExpressionSyntax _:
                    case LiteralExpressionSyntax _:
                        return true;
                    case ParenthesizedExpressionSyntax p:
                        return IsRepeatable(p.Expression);
                    case MemberAccessExpressionSyntax ma when ma.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                        return _model.GetSymbolInfo(ma.Expression).Symbol is ITypeSymbol || IsRepeatable(ma.Expression);
                    case ElementAccessExpressionSyntax el:
                        return IsRepeatable(el.Expression) && el.ArgumentList.Arguments.All(a => IsRepeatable(a.Expression));
                    default:
                        return false;
                }
            }

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.AddExpression) || node.IsKind(SyntaxKind.SubtractExpression))
                {
                    // 結果がデリゲート型のときだけ（"a" + h のような文字列の連結は別）
                    if (_shared.Find(_model.GetTypeInfo(node).Type) is DelegateInfo info)
                    {
                        if (info.Unsupported != null)
                        {
                            Error(node, $"'{info.Symbol.Name}' : {info.Unsupported}");
                            return node;
                        }
                        string helper = node.IsKind(SyntaxKind.AddExpression) ? "__combine" : "__remove";
                        return Parse($"{info.HelperFull}.{helper}({Text(Visit(node.Left))}, {Text(Visit(node.Right))})", node);
                    }
                }
                else if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))
                {
                    bool leftNull = node.Left.IsKind(SyntaxKind.NullLiteralExpression);
                    bool rightNull = node.Right.IsKind(SyntaxKind.NullLiteralExpression);
                    if (!leftNull && !rightNull && (DelegateOf(node.Left) ?? DelegateOf(node.Right)) is DelegateInfo info)
                    {
                        Error(node, $"'{info.Symbol.Name}' : デリゲートどうしの == / != は未対応です（null との比較はできます）");
                        return node;
                    }
                }
                return base.VisitBinaryExpression(node);
            }
        }
    }
}
