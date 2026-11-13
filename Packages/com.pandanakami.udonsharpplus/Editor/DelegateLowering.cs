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
    /// デリゲートの値は object[]。1 つのメソッドごとに 3 つの要素「呼ぶ相手の UdonSharpBehaviour・中継メソッドの名前・外の変数（今は null）」を並べる
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
            public IMethodSymbol Invoke => Symbol.DelegateInvokeMethod;
        }

        public static bool MayHaveDelegates(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes(n => !(n is BlockSyntax)).OfType<DelegateDeclarationSyntax>().Any());

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_DelegatePrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var delegates = CollectDelegates(compilation, trees);
            if (delegates.Count == 0)
                return trees;

            var shared = new Shared(delegates, diagnostics);
            var result = new List<SyntaxTree>(trees.Count);
            for (int i = 0; i < trees.Count; i++)
            {
                SemanticModel model = compilation.GetSemanticModel(trees[i]);
                var rewriter = new Rewriter(shared, model);
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

            /// <summary>UdonSharpBehaviour ごとの中継メソッド（名前 → コード）と、使うデリゲート型</summary>
            public readonly Dictionary<INamedTypeSymbol, (Dictionary<string, string> Trampolines, HashSet<DelegateInfo> Delegates)> Hosts =
                new Dictionary<INamedTypeSymbol, (Dictionary<string, string>, HashSet<DelegateInfo>)>(SymbolEqualityComparer.Default);

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
                sb.Append("__send(__u, (string)__d[__i + 1]); ");
                if (hasReturn)
                    sb.Append($"__r = ({ret})__u.GetProgramVariable(\"{info.RetField}\"); ");
                sb.Append("} ");
                if (hasReturn)
                    sb.Append("return __r; ");
                sb.Append("} }");
                return sb.ToString();
            }

            /// <summary>中継メソッドが使う、引数・戻り値のフィールド（1 行ずつ）</summary>
            public IEnumerable<string> FieldCode(DelegateInfo info)
            {
                IMethodSymbol invoke = info.Invoke;
                for (int i = 0; i < invoke.Parameters.Length; i++)
                    yield return $"protected {TypeText(invoke.Parameters[i].Type)} {info.ParamField(i)};";
                if (!invoke.ReturnsVoid)
                    yield return $"protected {TypeText(invoke.ReturnType)} {info.RetField};";
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 中継メソッドと、その引数・戻り値のフィールドを、UdonSharpBehaviour の最初の宣言（partial なら tree の順で最初）の閉じ括弧の行に置く。
        /// フィールドは、同じデリゲート型の中継メソッドを持つ基底があれば、基底の側にだけ置く（派生は継承したものを使う）
        /// </summary>
        private static List<SyntaxTree> AddGeneratedMembers(List<SyntaxTree> rewritten, List<SyntaxTree> originals, Shared shared)
        {
            if (shared.Hosts.Count == 0)
                return rewritten;

            var result = new List<SyntaxTree>(rewritten);
            var byTree = new Dictionary<int, List<(string Name, int Line, List<string> Code)>>();
            foreach (var pair in shared.Hosts)
            {
                INamedTypeSymbol host = pair.Key;
                var code = new List<string>();
                foreach (DelegateInfo info in pair.Value.Delegates.OrderBy(d => d.Key, StringComparer.Ordinal))
                {
                    bool inherited = false;
                    for (INamedTypeSymbol b = host.BaseType; b != null && !inherited; b = b.BaseType)
                        inherited = shared.Hosts.TryGetValue(b, out var baseHost) && baseHost.Delegates.Contains(info);
                    if (!inherited)
                        code.AddRange(shared.FieldCode(info));
                }
                code.AddRange(pair.Value.Trampolines.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Value));

                // 最初の宣言
                var first = host.DeclaringSyntaxReferences
                    .Select(r => (Tree: originals.IndexOf(r.SyntaxTree), Node: r.GetSyntax() as ClassDeclarationSyntax))
                    .Where(r => r.Tree >= 0 && r.Node != null)
                    .OrderBy(r => r.Tree).ThenBy(r => r.Node.SpanStart)
                    .FirstOrDefault();
                if (first.Node == null)
                    continue;
                int line = first.Node.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line;
                if (!byTree.TryGetValue(first.Tree, out var list))
                    byTree[first.Tree] = list = new List<(string, int, List<string>)>();
                list.Add((first.Node.Identifier.ValueText, line, code));
            }

            foreach (var pair in byTree)
            {
                SyntaxTree tree = result[pair.Key];
                SyntaxNode root = tree.GetRoot();
                var targets = new Dictionary<ClassDeclarationSyntax, List<string>>();
                foreach (var (name, line, code) in pair.Value)
                {
                    ClassDeclarationSyntax target = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c =>
                        c.Identifier.ValueText == name && c.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line == line);
                    if (target != null)
                        targets[target] = code;
                }
                if (targets.Count == 0)
                    continue;
                root = root.ReplaceNodes(targets.Keys, (o, n) => GeneratedMembers.AppendOnClosingLine(n, targets[o]));
                result[pair.Key] = Reparse(root, tree);
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

            public Rewriter(Shared shared, SemanticModel model)
            {
                _shared = shared;
                _model = model;
            }

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
                return MethodGroup(node) ?? TypeReference(node) ?? base.VisitIdentifierName(node);
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

                if (method.MethodKind == MethodKind.LocalFunction)
                {
                    Error(node, $"'{name}' : ローカル関数をデリゲートに入れることは、まだ未対応です");
                    return node;
                }
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
                if (!_shared.Hosts.TryGetValue(host, out var entry))
                    _shared.Hosts[host] = entry = (new Dictionary<string, string>(), new HashSet<DelegateInfo>());
                entry.Delegates.Add(info);
                if (!entry.Trampolines.ContainsKey(trampoline))
                {
                    IMethodSymbol invoke = info.Invoke;
                    string args = string.Join(", ", Enumerable.Range(0, invoke.Parameters.Length).Select(info.ParamField));
                    string assign = invoke.ReturnsVoid ? "" : info.RetField + " = ";
                    entry.Trampolines[trampoline] = $"public void {trampoline}() {{ {SourceMap.MarkerFor(node)}{assign}{callee}({args}); }}";
                }
                return Parse($"new {ValueType} {{ this, \"{trampoline}\", null }}", node);
            }

            /// <summary>デリゲートを作れる場所なら、その UdonSharpBehaviour。そうでなければ null と理由</summary>
            private INamedTypeSymbol HostOf(SyntaxNode node, out string error)
            {
                ISymbol s = _model.GetEnclosingSymbol(node.SpanStart);
                while (s is IMethodSymbol m && (m.MethodKind == MethodKind.AnonymousFunction || m.MethodKind == MethodKind.LocalFunction))
                    s = s.ContainingSymbol;
                error = null;
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
                Error(node, $"'{info.Symbol.Name}' : ラムダ・匿名メソッドをデリゲートに入れることは、まだ未対応です。メソッドにして、メソッド名を入れてください");
                // 後の段（List・LINQ のラムダの書き換え）で同じ場所に別のエラーを出さないよう、null にしておく
                return Parse("null", node);
            }

            // ---- 呼び出し ----------------------------------------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
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
                if ((node.IsKind(SyntaxKind.AddAssignmentExpression) || node.IsKind(SyntaxKind.SubtractAssignmentExpression)) && DelegateOf(node.Left) is DelegateInfo info)
                {
                    Error(node, $"'{info.Symbol.Name}' : デリゲートの += / -= は、まだ未対応です");
                    return node;
                }
                return base.VisitAssignmentExpression(node);
            }

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.AddExpression) || node.IsKind(SyntaxKind.SubtractExpression))
                {
                    if ((DelegateOf(node.Left) ?? DelegateOf(node.Right)) is DelegateInfo info)
                    {
                        Error(node, $"'{info.Symbol.Name}' : デリゲートの + / - は、まだ未対応です");
                        return node;
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
