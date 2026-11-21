using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UdonSharpPlus
{
    /// <summary>
    /// UdonSharpBehaviour のコルーチン（Redmine #107）。Udon には StartCoroutine がないので、IEnumerator を返すメソッドを
    /// 状態機械（再開する位置で分けた switch）に書き換え、待つ処理を遅れのイベントで行う。
    ///
    ///   IEnumerator Foo(int n) { A; yield return null; B; }
    ///     → private void __co_Foo_0(object[] __s) { int[] __h = ...; int n = (int)__s[1]; int __pc = __h[2]; while (true) { switch (__pc) { case 0:
    ///         A; __h[2] = 1; __co_wf(__s); return; case 1:
    ///         B; __co_done(__s); return; } } }
    ///       int Foo(int n) { object[] __s = __co_alloc(0, 2); __s[1] = n; return ((int[])__s[0])[0]; }   … クラスの閉じ括弧の行
    ///   StartCoroutine(Foo(3));  → __co_go(Foo(3));
    ///
    /// - 開始ごとの状態は object[]。[0] は int[] の見出し（番号・メソッドの番号・再開する位置・開始したか・空きのつなぎ）、[1] からは引数
    /// - Coroutine・IEnumerator の値は、状態の番号（int）。番号は「世代 &lt;&lt; 16 | 表の位置 + 1」で、0 は null。終わった・止めた状態は世代を
    ///   進めて表に残し、同じメソッドの次の開始で使い回す。古い番号は表で見つからなくなる（#107 の決めたこと 9）
    /// - 待つ処理：yield return null は SendCustomEventDelayedFrames(…, 1)、WaitForSeconds は SendCustomEventDelayedSeconds。
    ///   次のフレームの予約は、ビヘイビアごとに 1 フレーム 1 回にまとめ、届いたら待ち始めた順に全部再開する（#174）。止めた状態の番号は見つからないので捨てる
    /// - ほかの段（List・デリゲート・普通のクラスなど）を書き換えたあとの、最後の段に置く（状態に入れる値の型が、Udon の型になっているため）
    /// - 行は増やさない（メソッドの頭はその行に、yield はその行に置き換え、生成したメンバーはクラスの閉じ括弧の行に置く）
    /// </summary>
    internal static partial class CoroutineLowering
    {
        private const string EnumeratorName = "System.Collections.IEnumerator";
        private const string CoroutineName = "UnityEngine.Coroutine";
        private const string MonoBehaviourName = "UnityEngine.MonoBehaviour";
        private const string WaitForSecondsName = "UnityEngine.WaitForSeconds";
        private const string DataTokenName = "VRC.SDK3.Data.DataToken";

        public static bool MayHaveCoroutines(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is YieldStatementSyntax
                || (n is IdentifierNameSyntax id && UsesCoroutineApi(id))));

        private static bool UsesCoroutineApi(IdentifierNameSyntax id)
        {
            string name = id.Identifier.ValueText;
            return name == "StartCoroutine" || name == "StopCoroutine" || name == "StopAllCoroutines";
        }

        // ---- 型ごとの情報 ----------------------------------------------------

        private sealed class Coroutine
        {
            public MethodDeclarationSyntax Declaration;
            public IMethodSymbol Symbol;
            public int Id;
            public string StepName;
        }

        /// <summary>
        /// 継承でつながった UdonSharpBehaviour の組（いちばん上の、ソースにある基底のクラスが Root）。
        /// 状態の表・待つ処理・OnDisable は Root に 1 組だけ置き、コルーチンの番号は組の全体で振る（#107 の追加テスト案の 10）
        /// </summary>
        private sealed class TypeWork
        {
            public INamedTypeSymbol Root;
            public readonly List<Coroutine> Coroutines = new List<Coroutine>();
            /// <summary>状態の表・待つ処理などを置く宣言（Root の最初の宣言）</summary>
            public ClassDeclarationSyntax Host;
            /// <summary>クラスごとの、状態機械のメソッドへの振り分け（override）を置く宣言（そのクラスの最初の宣言）</summary>
            public readonly Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> DispatchHosts = new Dictionary<INamedTypeSymbol, ClassDeclarationSyntax>(SymbolEqualityComparer.Default);
            /// <summary>
            /// ユーザーが書いた OnDisable（その先頭で止める）と、宣言の書き換え：Override = 親をさかのぼって、生成した virtual の OnDisable が
            /// あるので public override にする。Public = 自分で virtual / override と書いたもので、組に生成した virtual があるので、public にそろえる
            /// </summary>
            public readonly Dictionary<MethodDeclarationSyntax, OnDisableEdit> OnDisables = new Dictionary<MethodDeclarationSyntax, OnDisableEdit>();
            /// <summary>OnDisable を生成する宣言と、virtual にするか（子孫のどれかが OnDisable を書いているとき）</summary>
            public readonly Dictionary<ClassDeclarationSyntax, bool> GeneratedOnDisables = new Dictionary<ClassDeclarationSyntax, bool>();

            public IEnumerable<Coroutine> Of(INamedTypeSymbol type) =>
                Coroutines.Where(c => SymbolEqualityComparer.Default.Equals(c.Symbol?.ContainingType, type));
        }

        private enum OnDisableEdit { None, Override, Public }

        /// <summary>type から基底をたどった、ソースにあるいちばん上の UdonSharpBehaviour</summary>
        private static INamedTypeSymbol RootOf(INamedTypeSymbol type, HashSet<INamedTypeSymbol> inSource)
        {
            INamedTypeSymbol t = type;
            while (t.BaseType != null && inSource.Contains(t.BaseType.OriginalDefinition))
                t = t.BaseType.OriginalDefinition;
            return t;
        }

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = Compilations.For(trees, references);

            // 1) ソースの UdonSharpBehaviour の宣言（tree の順）
            var decls = new List<(SyntaxTree Tree, ClassDeclarationSyntax Class, INamedTypeSymbol Type, List<MethodDeclarationSyntax> Methods, bool Uses)>();
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    List<MethodDeclarationSyntax> methods = cls.Members.OfType<MethodDeclarationSyntax>().Where(m => IsCoroutine(model, m)).ToList();
                    bool uses = methods.Count > 0 || OwnNodes(cls).OfType<IdentifierNameSyntax>().Any(UsesCoroutineApi);
                    decls.Add((tree, cls, type.OriginalDefinition, methods, uses));
                }
            }
            var inSource = new HashSet<INamedTypeSymbol>(decls.Select(d => d.Type), SymbolEqualityComparer.Default);

            // 2) コルーチンを使うクラスの組。番号は組の全体で、tree の順に振る
            var works = new Dictionary<INamedTypeSymbol, TypeWork>(SymbolEqualityComparer.Default);   // Root → 組
            foreach (var d in decls.Where(d => d.Uses))
            {
                INamedTypeSymbol root = RootOf(d.Type, inSource);
                if (!works.ContainsKey(root))
                    works[root] = new TypeWork { Root = root };
            }
            if (works.Count == 0)
                return trees;

            var classes = new List<(SyntaxTree Tree, ClassDeclarationSyntax Class, INamedTypeSymbol Type, TypeWork Work)>();
            foreach (var d in decls)
            {
                if (!works.TryGetValue(RootOf(d.Type, inSource), out TypeWork work))
                    continue;
                SemanticModel model = compilation.Model(d.Tree);
                if (work.Host == null && SymbolEqualityComparer.Default.Equals(d.Type, work.Root))
                    work.Host = d.Class;
                if (d.Methods.Count > 0 && !work.DispatchHosts.ContainsKey(d.Type))
                    work.DispatchHosts[d.Type] = d.Class;
                foreach (MethodDeclarationSyntax m in d.Methods)
                {
                    int id = work.Coroutines.Count;
                    work.Coroutines.Add(new Coroutine { Declaration = m, Symbol = model.GetDeclaredSymbol(m), Id = id, StepName = $"__co_{m.Identifier.ValueText}_{id}" });
                }
                classes.Add((d.Tree, d.Class, d.Type, work));
            }
            PlanOnDisable(classes.Select(c => (c.Class, c.Type, c.Work)).ToList(), inSource);
            // DispatchHosts のうち、Root の分は Host の振り分け（virtual）に入れる
            foreach (TypeWork work in works.Values)
                work.DispatchHosts.Remove(work.Root);

            // 同じコルーチンへの再入（コンパイル時にたどれるもの。#107 の決めたこと 10）
            ReentryCheck.Run(compilation, trees, works.Values, diagnostics);

            // 3) ファイルごとに書き換える
            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                var mine = classes.Where(c => c.Tree == tree).ToList();
                if (mine.Count == 0)
                {
                    result.Add(tree);
                    continue;
                }
                SemanticModel model = compilation.Model(tree);
                var edits = new Edits(tree.GetText());
                foreach (var (_, cls, type, work) in mine)
                    new ClassRewriter(model, cls, type, work, edits, diagnostics).Run();
                result.Add(edits.Any ? CSharpSyntaxTree.ParseText(edits.Apply(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8) : tree);
            }
            return result;
        }

        /// <summary>
        /// GameObject が非アクティブになったら止める OnDisable の置き場所（#107 の決めたこと 6）。U# は基底と派生の両方に同じ名前の
        /// メソッドを置けない（隠すのは未対応）が、virtual / override は書ける。
        /// - ユーザーが書いた OnDisable：その先頭で止める。親をさかのぼって、生成した virtual の OnDisable があれば、
        ///   public override にして base.OnDisable() も呼ぶ（書き換え後のコードだけを変え、元のソースは変えない）
        /// - 書いていないクラス：親をさかのぼって OnDisable（ユーザーのもの・生成したもの）があれば、それを引き継ぐので作らない。
        ///   なければ作る。子孫のどれかが OnDisable を書いていれば public virtual にする
        /// </summary>
        private static void PlanOnDisable(List<(ClassDeclarationSyntax Class, INamedTypeSymbol Type, TypeWork Work)> classes, HashSet<INamedTypeSymbol> inSource)
        {
            var user = new Dictionary<INamedTypeSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
            var first = new Dictionary<INamedTypeSymbol, ClassDeclarationSyntax>(SymbolEqualityComparer.Default);
            foreach (var (cls, type, _) in classes)
            {
                if (!first.ContainsKey(type))
                    first[type] = cls;
                MethodDeclarationSyntax onDisable = cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m =>
                    m.Identifier.ValueText == "OnDisable" && m.ParameterList.Parameters.Count == 0 && m.Body != null && !m.Modifiers.Any(SyntaxKind.StaticKeyword));
                if (onDisable != null && !user.ContainsKey(type))
                    user[type] = onDisable;
            }

            IEnumerable<INamedTypeSymbol> Ancestors(INamedTypeSymbol t)
            {
                for (INamedTypeSymbol b = t.BaseType?.OriginalDefinition; b != null && inSource.Contains(b); b = b.BaseType?.OriginalDefinition)
                    yield return b;
            }

            // 基底から順に（親が先に決まるように）
            var generated = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);   // 型 → virtual か
            foreach (INamedTypeSymbol type in first.Keys.OrderBy(t => Ancestors(t).Count()))
            {
                if (user.ContainsKey(type) || Ancestors(type).Any(a => user.ContainsKey(a) || generated.ContainsKey(a)))
                    continue;
                bool descendantWrites = user.Keys.Any(u => Ancestors(u).Contains(type, SymbolEqualityComparer.Default));
                generated[type] = descendantWrites;
            }

            TypeWork WorkOf(INamedTypeSymbol type) => classes.First(c => SymbolEqualityComparer.Default.Equals(c.Type, type)).Work;
            foreach (var pair in generated)
                WorkOf(pair.Key).GeneratedOnDisables[first[pair.Key]] = pair.Value;
            foreach (var pair in user)
            {
                // いちばん近い、OnDisable のある親が、生成した virtual のものなら override にする
                INamedTypeSymbol nearest = Ancestors(pair.Key).FirstOrDefault(a => user.ContainsKey(a) || generated.ContainsKey(a));
                bool toOverride = nearest != null && !user.ContainsKey(nearest) && generated[nearest];
                bool chain = pair.Value.Modifiers.Any(SyntaxKind.VirtualKeyword) || pair.Value.Modifiers.Any(SyntaxKind.OverrideKeyword);
                bool anyVirtual = generated.Any(g => g.Value && SymbolEqualityComparer.Default.Equals(RootOf(g.Key, inSource), RootOf(pair.Key, inSource)));
                WorkOf(pair.Key).OnDisables[pair.Value] = toOverride ? OnDisableEdit.Override : chain && anyVirtual ? OnDisableEdit.Public : OnDisableEdit.None;
            }
        }

        /// <summary>IEnumerator を返し、本体に yield があるメソッド（ラムダ・ローカル関数の中の yield は数えない）</summary>
        private static bool IsCoroutine(SemanticModel model, MethodDeclarationSyntax m) =>
            m.Body != null && Yields(m.Body).Any()
            && model.GetDeclaredSymbol(m) is IMethodSymbol symbol && symbol.ReturnType.ToDisplayString() == EnumeratorName && !symbol.IsStatic;

        /// <summary>
        /// ログ・名前の比べに使うコルーチンの名前。デリゲートの段が移したローカル関数（__lf_Foo__t_n）は、元の名前（Foo）。
        /// ローカル関数は StopCoroutine("Foo") では止まらない（文字列では開始できないので、名前で開始した印が付かない）
        /// </summary>
        private static string DisplayName(string name)
        {
            if (!name.StartsWith("__lf_"))
                return name;
            int end = name.LastIndexOf("__", System.StringComparison.Ordinal);
            return end > 5 ? name.Substring(5, end - 5) : name;
        }

        private static IEnumerable<YieldStatementSyntax> Yields(SyntaxNode body) =>
            body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax)).OfType<YieldStatementSyntax>();

        /// <summary>cls のノード（入れ子の型の中は含めない）</summary>
        private static IEnumerable<SyntaxNode> OwnNodes(ClassDeclarationSyntax cls) =>
            cls.DescendantNodes(n => n == cls || !(n is BaseTypeDeclarationSyntax)).Where(n => !(n is BaseTypeDeclarationSyntax));

        // ---- テキストの編集 ----------------------------------------------------

        /// <summary>
        /// 元のテキストへの置き換えを集め、最後にまとめて当てる。外側の置き換えを作るときは、中の置き換えを当てたテキストを Take で取り出す
        /// （取り出した置き換えは外側に含まれるので、一覧から消す）
        /// </summary>
        private sealed class Edits
        {
            private readonly SourceText _text;
            private readonly List<(TextSpan Span, string Text)> _list = new List<(TextSpan, string)>();

            public Edits(SourceText text) { _text = text; }

            public SourceText Text => _text;

            public bool Any => _list.Count > 0;

            public void Add(TextSpan span, string text)
            {
                // 同じところへの同じ置き換えは 1 つにする。先に足した置き換えの中に入るもの（default(Coroutine) → 0 の中の型など）は、
                // 外側が中身ごと置き換えるので捨てる（境目への挿入は残す）
                if (_list.Any(e => (e.Span == span && e.Text == text)
                                   || (e.Span.Length > 0 && e.Span.Contains(span) && e.Span != span && !(span.Length == 0 && (span.Start == e.Span.Start || span.Start == e.Span.End)))))
                    return;
                _list.Add((span, text));
            }

            public string Take(SyntaxNode node) => Take(node.Span);

            public string Take(TextSpan span)
            {
                var inner = _list.Where(e => span.Contains(e.Span)).OrderBy(e => e.Span.Start).ToList();
                _list.RemoveAll(e => span.Contains(e.Span));
                var sb = new StringBuilder();
                int pos = span.Start;
                foreach (var e in inner)
                {
                    sb.Append(_text.ToString(TextSpan.FromBounds(pos, e.Span.Start))).Append(e.Text);
                    pos = e.Span.End;
                }
                return sb.Append(_text.ToString(TextSpan.FromBounds(pos, span.End))).ToString();
            }

            public string Apply()
            {
                var sorted = _list.OrderBy(e => e.Span.Start).ThenBy(e => e.Span.Length).ToList();
                for (int i = 1; i < sorted.Count; i++)
                    if (sorted[i].Span.Start < sorted[i - 1].Span.End)
                        throw new System.InvalidOperationException($"[UdonSharpPlus] coroutine lowering: overlapping edits at {_text.Lines.GetLinePosition(sorted[i].Span.Start)}: "
                                                                   + $"'{_text.ToString(sorted[i - 1].Span)}' → '{sorted[i - 1].Text}' / '{_text.ToString(sorted[i].Span)}' → '{sorted[i].Text}'");
                return _text.WithChanges(sorted.Select(e => new TextChange(e.Span, e.Text))).ToString();
            }
        }

        /// <summary>text の改行の数だけの改行（置き換えたところのうしろの行をずらさないため）</summary>
        private static string LineBreaks(string text) => new string('\n', text.Count(c => c == '\n'));

        /// <summary>
        /// 置き換えた code のうしろに、元の original より足りない分の改行を足す（置き換えたところのうしろの行をずらさないため）。
        /// code の中に、取り出した中の式（複数行のもの）の改行が入っていれば、その分は足さない
        /// </summary>
        private static string KeepLines(string code, string original) =>
            code + new string('\n', System.Math.Max(0, original.Count(c => c == '\n') - code.Count(c => c == '\n')));

        // ---- クラスの書き換え ----------------------------------------------------

        private sealed partial class ClassRewriter
        {
            private readonly SemanticModel _model;
            private readonly ClassDeclarationSyntax _cls;
            private readonly INamedTypeSymbol _type;
            private readonly TypeWork _work;
            private readonly Edits _edits;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly List<string> _members = new List<string>();

            public ClassRewriter(SemanticModel model, ClassDeclarationSyntax cls, INamedTypeSymbol type, TypeWork work, Edits edits, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _cls = cls;
                _type = type;
                _work = work;
                _edits = edits;
                _diagnostics = diagnostics;
            }

            private void Error(SyntaxNode at, string message) => _diagnostics.Error(at.GetLocation(), message);

            public void Run()
            {
                // 1) 中の小さな置き換え：IEnumerator・Coroutine の型 → int、StartCoroutine → 状態の番号で開始するメソッド
                foreach (SyntaxNode node in OwnNodes(_cls))
                {
                    if (node is InvocationExpressionSyntax call)
                        RewriteCall(call);
                    else if (node is FieldDeclarationSyntax field)
                        KeepUnserialized(field);
                    else if (node is TypeSyntax type && IsHandleType(type))
                        _edits.Add(type.Span, "int");
                    else if ((node.IsKind(SyntaxKind.NullLiteralExpression) || node.IsKind(SyntaxKind.DefaultLiteralExpression) || node is DefaultExpressionSyntax)
                             && (IsHandle(_model.GetTypeInfo(node).ConvertedType)
                                 || (node.Parent is CastExpressionSyntax nullCast && IsHandle(_model.GetTypeInfo(nullCast.Type).Type))))   // (IEnumerator)null
                        _edits.Add(node.Span, "0");   // Coroutine・IEnumerator の null は 0
                    else if (node is CastExpressionSyntax cast)
                        RewriteTokenCast(cast);
                    else if (node is BinaryExpressionSyntax compare && (compare.IsKind(SyntaxKind.EqualsExpression) || compare.IsKind(SyntaxKind.NotEqualsExpression)))
                    {
                        // _co != null（null の型は object になる）
                        if (compare.Right.IsKind(SyntaxKind.NullLiteralExpression) && IsHandle(_model.GetTypeInfo(compare.Left).Type))
                            _edits.Add(compare.Right.Span, "0");
                        else if (compare.Left.IsKind(SyntaxKind.NullLiteralExpression) && IsHandle(_model.GetTypeInfo(compare.Right).Type))
                            _edits.Add(compare.Left.Span, "0");
                    }
                }

                // GameObject が非アクティブになったら止める（#107 の決めたこと 6）。ユーザーの OnDisable があれば、その先頭で
                // （継承でつながったクラスのどれかが OnDisable を書いていれば、書いたものすべての先頭で。U# は基底と派生の両方に同じ名前の
                // メソッドを置けないので、生成するのは 1 つも書いていないときだけ）
                foreach (var pair in _work.OnDisables.Where(p => p.Key.Parent == _cls))
                {
                    MethodDeclarationSyntax onDisable = pair.Key;
                    _edits.Add(new TextSpan(onDisable.Body.OpenBraceToken.Span.End, 0), pair.Value == OnDisableEdit.Override ? " __co_off(); base.OnDisable();" : " __co_off();");
                    if (pair.Value == OnDisableEdit.None)
                        continue;
                    // アクセス修飾子を public に、Override なら virtual でなく override に
                    string keep = pair.Value == OnDisableEdit.Override ? "override"
                        : string.Join(" ", onDisable.Modifiers.Where(t => !(t.IsKind(SyntaxKind.PrivateKeyword) || t.IsKind(SyntaxKind.ProtectedKeyword) || t.IsKind(SyntaxKind.InternalKeyword) || t.IsKind(SyntaxKind.PublicKeyword))).Select(t => t.ValueText));
                    string modifiers = "public" + (keep.Length > 0 ? " " + keep : "");
                    if (onDisable.Modifiers.Count > 0)
                        _edits.Add(TextSpan.FromBounds(onDisable.Modifiers.First().SpanStart, onDisable.Modifiers.Last().Span.End), modifiers);
                    else
                        _edits.Add(new TextSpan(onDisable.ReturnType.SpanStart, 0), modifiers + " ");
                }
                if (_work.GeneratedOnDisables.TryGetValue(_cls, out bool isVirtual))
                    _members.Add(isVirtual ? "public virtual void OnDisable() { __co_off(); }" : "private void OnDisable() { __co_off(); }");

                // 2) コルーチンのメソッド → その場に状態機械、閉じ括弧の行に開始のメソッド
                foreach (Coroutine co in _work.Coroutines.Where(c => c.Declaration.Parent == _cls))
                    RewriteCoroutine(co);

                if (_cls == _work.Host)
                    _members.AddRange(Runtime());
                else if (_work.DispatchHosts.TryGetValue(_type, out ClassDeclarationSyntax dispatch) && dispatch == _cls)
                    _members.Add($"protected override void __co_stepx(object[] s, int id) {{ switch (id) {{ {Cases(_type)}}} base.__co_stepx(s, id); }}");

                if (_members.Count > 0)
                    _edits.Add(new TextSpan(_cls.CloseBraceToken.SpanStart, 0), string.Join(" ", _members) + " ");
            }

            /// <summary>
            /// Coroutine・IEnumerator（とその配列）のフィールドは、Unity と同じくシリアライズさせない。int にしたままシリアライズさせると、
            /// Inspector に番号が出て、配列は null ではなく空の配列で始まる（#127 と同じ）。public なら [NonSerialized] を足し、
            /// [OdinSerialize] は外す。[SerializeField]・[UdonSynced] はエラー（番号は、そのビヘイビアの中だけのもの）
            /// </summary>
            private void KeepUnserialized(FieldDeclarationSyntax field)
            {
                ITypeSymbol t = _model.GetTypeInfo(field.Declaration.Type).Type;
                if (!IsHandle(t) && !(t is IArrayTypeSymbol array && IsHandle(array.ElementType)))
                    return;
                var attributes = field.AttributeLists.SelectMany(l => l.Attributes).ToList();
                var names = attributes.Select(a => _model.SymbolInfoOf(a).Symbol?.ContainingType?.Name).ToList();
                if (names.Contains("SerializeField") || names.Contains("UdonSyncedAttribute") || names.Contains("SerializeReference"))
                {
                    Error(field, Loc.T("Coroutine・IEnumerator のフィールドは、シリアライズ・同期できません（[SerializeField] / [UdonSynced] は付けられません。値はそのビヘイビアの中だけの番号です）",
                        "Coroutine / IEnumerator fields cannot be serialized or synced ([SerializeField] / [UdonSynced] cannot be used; the value is a number that only means something inside this behaviour)"));
                    return;
                }
                foreach (AttributeListSyntax list in field.AttributeLists)
                    if (list.Attributes.All(SerializationFields.IsOdinSerialize))
                        _edits.Add(list.Span, "");
                bool nonSerialized = attributes.Any(a => a.Name.ToString().EndsWith("NonSerialized") || a.Name.ToString().EndsWith("NonSerializedAttribute"));
                if (field.Modifiers.Any(SyntaxKind.PublicKeyword) && !nonSerialized)
                    _edits.Add(new TextSpan(field.Modifiers.First().SpanStart, 0), "[global::System.NonSerialized] ");
            }

            /// <summary>IEnumerator・Coroutine の型を書いたところ（修飾名は外側だけ）</summary>
            private bool IsHandleType(TypeSyntax type)
            {
                if (!(type is NameSyntax) || type.Parent is QualifiedNameSyntax || type.Parent is AliasQualifiedNameSyntax)
                    return false;
                if (type.Parent is MemberAccessExpressionSyntax || type.Parent is InvocationExpressionSyntax)
                    return false;
                string name = (_model.GetSymbolInfo(type).Symbol as INamedTypeSymbol)?.ToDisplayString();
                return name == EnumeratorName || name == CoroutineName;
            }

            /// <summary>
            /// List・Dictionary の段は、Coroutine・IEnumerator を参照の値として DataToken に入れる（new DataToken((object)(c))・(Coroutine)(t.Reference)）。
            /// 番号の int のトークンにして（Contains・Remove などが番号で比べられるように）、読むところは .Int にする
            /// </summary>
            private void RewriteTokenCast(CastExpressionSyntax cast)
            {
                if (cast.Type.IsKind(SyntaxKind.PredefinedType) && ((PredefinedTypeSyntax)cast.Type).Keyword.IsKind(SyntaxKind.ObjectKeyword)
                    && cast.Parent is ArgumentSyntax arg && arg.Parent?.Parent is ObjectCreationExpressionSyntax create
                    && _model.GetTypeInfo(create).Type?.ToDisplayString() == DataTokenName && IsHandle(_model.GetTypeInfo(cast.Expression).Type))
                {
                    _edits.Add(cast.Type.Span, "int");
                    return;
                }
                ExpressionSyntax inner = cast.Expression;
                while (inner is ParenthesizedExpressionSyntax p)
                    inner = p.Expression;
                if (inner is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Reference"
                    && _model.GetTypeInfo(access.Expression).Type?.ToDisplayString() == DataTokenName && IsHandle(_model.GetTypeInfo(cast.Type).Type))
                    _edits.Add(access.Name.Span, "Int");
            }

            private static bool IsHandle(ITypeSymbol type)
            {
                string name = type?.ToDisplayString();
                return name == EnumeratorName || name == CoroutineName;
            }

            /// <summary>StartCoroutine・StopCoroutine・StopAllCoroutines → 状態の番号で開始・停止するメソッド</summary>
            private void RewriteCall(InvocationExpressionSyntax call)
            {
                if (!(_model.SymbolInfoOf(call).Symbol is IMethodSymbol method) || method.ContainingType.ToDisplayString() != MonoBehaviourName
                    || !(method.Name == "StartCoroutine" || method.Name == "StopCoroutine" || method.Name == "StopAllCoroutines"))
                    return;
                SimpleNameSyntax name = call.Expression is MemberAccessExpressionSyntax access ? access.Name : call.Expression as SimpleNameSyntax;
                if (name == null)
                    return;
                if (call.Expression is MemberAccessExpressionSyntax receiver && !(receiver.Expression is ThisExpressionSyntax || receiver.Expression is BaseExpressionSyntax))
                {
                    Error(call, Loc.T($"ほかのビヘイビアの {method.Name} は未対応です。そのビヘイビアのメソッドの中で呼んでください", $"Calling {method.Name} on another behaviour is not supported. Call it from a method of that behaviour"));
                    return;
                }
                if (method.Parameters.Length > 0 && method.Parameters[0].Type.SpecialType == SpecialType.System_String)
                {
                    RewriteByName(call, method, name);
                    return;
                }
                string target;
                if (method.Name == "StopCoroutine")
                    // StopCoroutine(Foo()) は、作ったばかりの（開始していない）状態を止めるので、Unity でも何も起きない。その状態はどこからも使えないので、
                    // 空きに戻す（Update などで呼び続けても、状態が増え続けない。#174）
                    target = IsFreshCoroutine(call.ArgumentList.Arguments.FirstOrDefault()?.Expression) ? "__co_drop" : "__co_stop";
                else if (method.Name == "StopAllCoroutines")
                    target = "__co_stopall";
                else
                    // 戻り値を使わないときは、開始したあとに何もしないメソッドにする（入れ子で呼ばれても、ローカル変数が上書きされて困ることがない）
                    target = call.Parent is ExpressionStatementSyntax ? "__co_go" : "__co_start";
                _edits.Add(name.Span, target);
            }

            /// <summary>
            /// 文字列で指定する StartCoroutine("Foo")・StartCoroutine("Foo", value)・StopCoroutine("Foo")（#174）。
            /// 開始は、名前を定数にして、StartCoroutine(Foo(value)) と同じに開始し、名前で開始した印を付ける（__co_named）。
            /// 止めるのは、Unity と同じく、名前で開始したもののうち、その名前のものを全部（__co_stopn。名前は実行時の文字列でよい）
            /// </summary>
            private void RewriteByName(InvocationExpressionSyntax call, IMethodSymbol method, SimpleNameSyntax name)
            {
                var args = call.ArgumentList.Arguments;
                if (method.Name == "StopCoroutine")
                {
                    _edits.Add(name.Span, "__co_stopn");
                    return;
                }
                Optional<object> constant = _model.GetConstantValue(args[0].Expression);
                if (!constant.HasValue || !(constant.Value is string target))
                {
                    Error(args[0], Loc.T("文字列で指定する StartCoroutine は、名前を定数（\"Foo\"・nameof(Foo)）で書いてください", "StartCoroutine with a method name needs a constant name (\"Foo\" or nameof(Foo))"));
                    return;
                }
                var candidates = _model.LookupSymbols(call.SpanStart, _type, target).OfType<IMethodSymbol>()
                    .Where(m => !m.IsStatic && m.ReturnType.ToDisplayString() == EnumeratorName).ToList();
                // override は基底の宣言と同じものとして数える
                candidates = candidates.Where(m => !candidates.Any(o => !SymbolEqualityComparer.Default.Equals(o, m) && SymbolEqualityComparer.Default.Equals(o.OverriddenMethod, m))).ToList();
                if (candidates.Count == 0)
                {
                    Error(args[0], Loc.T($"StartCoroutine(\"{target}\")：このクラスに、IEnumerator を返す {target} というメソッドがありません", $"StartCoroutine(\"{target}\"): this class has no method named {target} that returns IEnumerator"));
                    return;
                }
                if (candidates.Count > 1)
                {
                    Error(args[0], Loc.T($"StartCoroutine(\"{target}\")：{target} という名前のメソッドが複数あります。StartCoroutine({target}(...)) と書いてください", $"StartCoroutine(\"{target}\"): there are several methods named {target}. Write StartCoroutine({target}(...))"));
                    return;
                }
                IMethodSymbol co = candidates[0];
                if (co.IsGenericMethod)
                {
                    Error(args[0], Loc.T($"StartCoroutine(\"{target}\")：ジェネリックのメソッドは、文字列では開始できません（Unity も同じ）。StartCoroutine({target}(...)) と書いてください", $"StartCoroutine(\"{target}\"): a generic method cannot be started by name (same as Unity). Write StartCoroutine({target}(...))"));
                    return;
                }
                bool hasValue = args.Count > 1;
                if (co.Parameters.Length != (hasValue ? 1 : 0))
                {
                    Error(call, Loc.T($"StartCoroutine(\"{target}\")：{target} の引数の数が合いません（文字列で指定するときは、引数なしか、値 1 つの StartCoroutine(\"{target}\", value)）",
                        $"StartCoroutine(\"{target}\"): the number of arguments of {target} does not match (with a method name, use no argument or one value: StartCoroutine(\"{target}\", value))"));
                    return;
                }
                _edits.Add(name.Span, call.Parent is ExpressionStatementSyntax ? "__co_go" : "__co_start");
                string callee = "this." + (SyntaxFacts.GetKeywordKind(co.Name) != SyntaxKind.None ? "@" : "") + co.Name;
                if (!hasValue)
                {
                    _edits.Add(args[0].Span, $"__co_named({callee}())");
                    return;
                }
                ExpressionSyntax value = args[1].Expression;
                ITypeSymbol paramType = co.Parameters[0].Type;
                string cast = "";
                if (!_model.ClassifyConversion(value, paramType).IsImplicit)
                {
                    if (_model.GetTypeInfo(value).Type?.SpecialType != SpecialType.System_Object)
                    {
                        Error(value, Loc.T($"StartCoroutine(\"{target}\", value)：この値は {target} の引数（{paramType.ToDisplayString()}）に渡せません", $"StartCoroutine(\"{target}\", value): this value cannot be passed to the parameter of {target} ({paramType.ToDisplayString()})"));
                        return;
                    }
                    cast = "(" + paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
                }
                // "Foo", value → __co_named(this.Foo((T)(value)))（value の中の置き換えを残すため、前とうしろだけを置き換える）
                _edits.Add(TextSpan.FromBounds(args[0].SpanStart, value.SpanStart), $"__co_named({callee}({cast}(");
                _edits.Add(new TextSpan(value.Span.End, 0), ")))");
            }

            /// <summary>この組のコルーチンのメソッドを、その場で呼んだ式か（新しい状態の番号を返す）</summary>
            private bool IsFreshCoroutine(ExpressionSyntax e)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                if (!(e is InvocationExpressionSyntax inv) || !(_model.SymbolInfoOf(inv).Symbol is IMethodSymbol m))
                    return false;
                return _work.Coroutines.Any(c => SymbolEqualityComparer.Default.Equals(c.Symbol, m.OriginalDefinition));
            }

            private void RewriteCoroutine(Coroutine co) => new BodyWriter(this, co).Run();

            /// <summary>yield return の値に合わせた、待つ処理の呼び出し（未対応ならエラーにして null）</summary>
            private string Wait(ExpressionSyntax value)
            {
                if (value.IsKind(SyntaxKind.NullLiteralExpression))
                    return "__co_wf(__s);";
                ITypeSymbol type = _model.GetTypeInfo(value).Type;
                if (value is ObjectCreationExpressionSyntax create && create.ArgumentList?.Arguments.Count == 1 && type?.ToDisplayString() == WaitForSecondsName)
                    return $"__co_ws(__s, {_edits.Take(create.ArgumentList.Arguments[0].Expression)});";
                // 子のコルーチンを待つ：yield return StartCoroutine(Foo())・yield return Foo()・yield return Coroutine / IEnumerator の値
                if (value is InvocationExpressionSyntax call && _model.SymbolInfoOf(call).Symbol is IMethodSymbol method && method.Name == "StartCoroutine"
                    && method.ContainingType.ToDisplayString() == MonoBehaviourName && call.ArgumentList.Arguments.Count == 1 && IsHandle(method.Parameters[0].Type))
                    return $"__co_await(__s, {_edits.Take(call.ArgumentList.Arguments[0].Expression)});";
                if (IsHandle(type))
                    return $"__co_await(__s, {_edits.Take(value)});";
                // 数・bool・文字列など（Unity では、null と同じく次のフレームまで待つ）
                if (type != null && (type.IsValueType || type.SpecialType == SpecialType.System_String))
                    return "__co_wf(__s);";
                // WaitForEndOfFrame・WaitForFixedUpdate は非対応（#107 の決めたこと 2 の続き。遅れのイベントでは Unity と同じタイミングにできない）
                if (type?.ToDisplayString() == "UnityEngine.WaitForEndOfFrame")
                {
                    Error(value, Loc.T("WaitForEndOfFrame は未対応です（Udon の遅れのイベントでは、そのフレームの描画のあとに再開できません）。次のフレームでよければ yield return null、そのフレームの最後に動かすなら PostLateUpdate() に書いてください",
                        "WaitForEndOfFrame is not supported (Udon's delayed events cannot resume after the frame is rendered). Use yield return null if the next frame is fine, or write the code in PostLateUpdate() to run at the end of the frame"));
                    return null;
                }
                if (type?.ToDisplayString() == "UnityEngine.WaitForFixedUpdate")
                {
                    Error(value, Loc.T("WaitForFixedUpdate は未対応です（Udon の遅れのイベントでは、物理の計算のあとに再開できません）。物理に合わせる処理は FixedUpdate() に書いてください",
                        "WaitForFixedUpdate is not supported (Udon's delayed events cannot resume after the physics step). Write physics-timed code in FixedUpdate()"));
                    return null;
                }
                Error(value, Loc.T("この yield return の値は未対応です（null・WaitForSeconds・WaitForSecondsRealtime・WaitUntil・WaitWhile・子のコルーチンに対応しています）", "This yield return value is not supported (null, WaitForSeconds, WaitForSecondsRealtime, WaitUntil, WaitWhile and child coroutines are supported)"));
                return null;
            }

            /// <summary>type で宣言したコルーチンの、状態機械のメソッドへの振り分け</summary>
            private string Cases(INamedTypeSymbol type) => string.Concat(_work.Of(type).Select(co => $"case {co.Id}: {co.StepName}(s); return; "));

            /// <summary>状態の表・開始・待つ処理（継承でつながったクラスの組に 1 組。派生から呼ぶものは protected）</summary>
            private IEnumerable<string> Runtime()
            {
                int count = _work.Coroutines.Count;
                yield return "private object[] __co_slots = new object[8]; private int __co_used;";
                yield return $"private int[] __co_free = new int[{System.Math.Max(count, 1)}];";
                // コルーチンのメソッドごとの「動いているか」（同じコルーチンへの再入を止める。#107 の決めたこと 10）
                yield return $"private int[] __co_on = new int[{System.Math.Max(count, 1)}];";
                yield return "private string[] __co_names = new string[] { " + string.Join(", ", _work.Coroutines.Select(c => "\"" + DisplayName(c.Declaration.Identifier.ValueText) + "\"")) + " };";
                // 次のフレームを待っているものの番号と、待ち始めたフレーム（外から __co_frame を呼ばれても、待ち始めたフレームのうちには再開しない）
                yield return "private int[] __co_fq = new int[8]; private int[] __co_ff = new int[8]; private int __co_fh; private int __co_fn;";
                yield return "private int[] __co_sh = new int[8]; private float[] __co_st = new float[8]; private int[] __co_sf = new int[8]; private int __co_sn;";

                // 状態を作る（同じメソッドの空いた状態があれば使い回す）。
                // 見出し：[0] 番号 [1] メソッドの番号 [2] 再開する位置 [3] 開始したか [4] 空きのつなぎ [5] 動いている途中か [6] 動いている途中で止められたか
                // [7] 終わるのを待っている親の番号 [8] 親がまだ yield の途中か（子が最初の yield より前に終わったら、親は次のフレームで再開する）
                // [9] この状態の直近 32 世代が、一度も yield せずに（StartCoroutine の中で）終わったか（世代 & 31 のビット）。Unity では、それを待つと
                //     次のフレームで続きが動く。ほかの、もう終わった・止めたものを待つと、その場で続きが動く（Unity で測った結果。#174）
                // [10] 文字列の StartCoroutine("Foo") で開始したか（StopCoroutine("Foo") が止めるのは、これだけ。Unity と同じ）
                yield return "protected object[] __co_alloc(int id, int size) { int f = __co_free[id]; if (f != 0) { object[] r = (object[])__co_slots[f - 1]; int[] rh = (int[])r[0]; __co_free[id] = rh[4]; rh[2] = 0; rh[3] = 0; rh[4] = 0; rh[5] = 0; rh[6] = 0; rh[7] = 0; rh[8] = 0; rh[10] = 0; return r; } "
                             + "if (__co_used == __co_slots.Length) { object[] g = new object[__co_used * 2]; for (int i = 0; i < __co_used; i++) g[i] = __co_slots[i]; __co_slots = g; } "
                             + "object[] s = new object[size]; int[] h = new int[11]; h[0] = 65536 | (__co_used + 1); h[1] = id; s[0] = h; __co_slots[__co_used] = s; __co_used++; return s; }";
                yield return "private object[] __co_get(int c) { int i = (c & 65535) - 1; if (i < 0 || i >= __co_used) return null; object[] s = (object[])__co_slots[i]; if (((int[])s[0])[0] != c) return null; return s; }";
                // 古い番号を無効にする（世代を進める）
                // 世代を使い切ったら（32767）、世代を 0 にして引退させる（空きに戻さないので、古い番号が新しい開始を指すことはない。#107）
                yield return "private void __co_bump(int[] h) { int g = (h[0] >> 16) + 1; if (g > 32767) g = 0; h[0] = (g << 16) | (h[0] & 65535); h[3] = 0; }";
                // 空きに戻す：参照を切って、空きのつなぎに入れる
                yield return "private void __co_recycle(object[] s) { int[] h = (int[])s[0]; for (int i = 1; i < s.Length; i++) s[i] = null; h[2] = -1; h[5] = 0; h[6] = 0; h[7] = 0; h[8] = 0; if ((h[0] >> 16) == 0) return; h[4] = __co_free[h[1]]; __co_free[h[1]] = h[0] & 65535; }";
                // 止める。動いている途中（自分を止めたときなど）なら、次の yield で空きに戻す（Unity と同じく、次の yield まで動く）
                yield return "private void __co_kill(object[] s) { int[] h = (int[])s[0]; h[9] &= ~(1 << ((h[0] >> 16) & 31)); __co_bump(h); if (h[5] != 0) h[6] = 1; else __co_recycle(s); }";
                // 終わり。待っている親があれば、同じフレームのうちに再開する（親がまだ yield の途中なら、次のフレーム）
                yield return "protected void __co_done(object[] s) { int[] h = (int[])s[0]; __co_on[h[1]] = 0; h[5] = 0; int w = h[7]; int sync = h[8]; if (h[6] == 0) { int b = 1 << ((h[0] >> 16) & 31); if (h[2] == 0) h[9] |= b; else h[9] &= ~b; __co_bump(h); } __co_recycle(s); "
                             + "if (w == 0) return; object[] p = __co_get(w); if (p == null) return; if (sync != 0) __co_wf(p); else __co_step(p); }";
                yield return "protected void __co_stop(int c) { object[] s = __co_get(c); if (s != null && ((int[])s[0])[3] != 0) __co_kill(s); }";
                yield return "protected int __co_named(int c) { object[] s = __co_get(c); if (s != null) ((int[])s[0])[10] = 1; return c; }";
                yield return "protected void __co_stopn(string n) { for (int i = 0; i < __co_used; i++) { object[] s = (object[])__co_slots[i]; int[] h = (int[])s[0]; if (h[3] != 0 && h[10] != 0 && __co_names[h[1]] == n) __co_kill(s); } }";
                yield return "protected void __co_drop(int c) { object[] s = __co_get(c); if (s == null) return; int[] h = (int[])s[0]; if (h[3] != 0) return; __co_bump(h); __co_recycle(s); }";
                yield return "protected void __co_stopall() { for (int i = 0; i < __co_used; i++) { object[] s = (object[])__co_slots[i]; if (((int[])s[0])[3] != 0) __co_kill(s); } }";
                // GameObject が非アクティブになったときだけ止める（ビヘイビアを無効にしただけなら止めない。Unity と同じ）
                yield return "protected void __co_off() { if (!gameObject.activeInHierarchy) __co_stopall(); }";

                string inactive = "UnityEngine.Debug.LogError(\"Coroutine couldn't be started because the the game object '\" + gameObject.name + \"' is inactive!\");";
                yield return $"protected void __co_go(int c) {{ object[] s = __co_get(c); if (s == null) return; int[] h = (int[])s[0]; if (h[3] != 0) return; if (!gameObject.activeInHierarchy) {{ {inactive} __co_kill(s); return; }} h[3] = 1; __co_step(s); }}";
                // 戻り値を使う開始。動かしている途中に入れ子で呼ばれると引数・ローカル変数が上書きされるので、戻す番号は自分の積み上げに退避する
                // （RecursiveMethod は呼ぶたびにローカル変数を退避して、確保が 3 回増えるので使わない。#174）
                yield return "private int[] __co_rs = new int[4]; private int __co_rn;";
                yield return $"protected int __co_start(int c) {{ object[] s = __co_get(c); if (s == null) return 0; int[] h = (int[])s[0]; if (h[3] != 0) return c; if (!gameObject.activeInHierarchy) {{ {inactive} __co_kill(s); return 0; }} h[3] = 1; "
                             + "if (__co_rn == __co_rs.Length) { int[] g = new int[__co_rn * 2]; for (int i = 0; i < __co_rn; i++) g[i] = __co_rs[i]; __co_rs = g; } __co_rs[__co_rn] = c; __co_rn++; __co_step(s); __co_rn--; return __co_rs[__co_rn]; }";

                // 派生クラスのコルーチンは、派生クラスの override（__co_stepx）が振り分ける
                yield return $"protected virtual void __co_stepx(object[] s, int id) {{ switch (id) {{ {Cases(_work.Root)}}} }}";
                // 状態機械のメソッドを呼ぶ。そのメソッドが動いている途中なら再入なので、エラーにして止める（メソッドの中で確かめると、引数がもう上書きされている）
                string reenter = Loc.T(" のコルーチンが動いている途中に、同じコルーチンを StartCoroutine で開始したため、止めました（ローカル変数が上書きされるため。yield return StartCoroutine(...) の形なら開始できます）",
                    " : the same coroutine was started with StartCoroutine while it was running, so it was stopped (its local variables would be overwritten; it can be started with yield return StartCoroutine(...))");
                yield return $"private void __co_step(object[] s) {{ int id = ((int[])s[0])[1]; if (__co_on[id] != 0) {{ UnityEngine.Debug.LogError(\"[UdonSharpPlus] \" + __co_names[id] + \"{reenter}\"); __co_kill(s); return; }} __co_on[id] = 1; __co_stepx(s, id); }}";

                // 子のコルーチンを待つ。もう終わった・止めたもの（番号が見つからない）なら、その場で続ける。ただし、一度も yield せずに終わったもの（[9]）と null は
                // 次のフレーム（Unity と同じ。#174）。まだ開始していなければ、親を覚えてから開始する
                // 自分を待つ・ほかの親が待っているものを待つと、Unity と同じく二度と再開しない（止めるまで状態を持ったまま）。#107 の追加テスト案の 4
                // 待つのをやめて、再開を予約しないでおく。動いている途中で止められていたら空きに戻す（戻したら false）
                yield return "private bool __co_park(object[] s) { int[] hd = (int[])s[0]; __co_on[hd[1]] = 0; hd[5] = 0; hd[8] = 0; if (hd[6] != 0) { __co_recycle(s); return false; } return true; }";
                string inactive2 = "UnityEngine.Debug.LogError(\"Coroutine couldn't be started because the the game object '\" + gameObject.name + \"' is inactive!\");";
                yield return "protected void __co_await(object[] s, int c) { int[] hd = (int[])s[0]; object[] k = __co_get(c); "
                             + "if (k == null) { if (c == 0) { __co_wf(s); return; } int i = (c & 65535) - 1; if (i >= 0 && i < __co_used) { int[] sh = (int[])((object[])__co_slots[i])[0]; int g = c >> 16; int now = sh[0] >> 16; "
                             + "if (g < now && now - g <= 31 && (sh[9] & (1 << (g & 31))) != 0) { __co_wf(s); return; } } if (__co_park(s)) __co_step(s); return; } "
                             + "if (k == s) { __co_park(s); return; } int[] kh = (int[])k[0]; "
                             + "if (kh[7] != 0) { UnityEngine.Debug.LogError(\"Another coroutine is already waiting for this coroutine!\"); __co_park(s); return; } "
                             + "if (kh[3] != 0) { __co_on[hd[1]] = 0; hd[5] = 0; hd[8] = 0; if (hd[6] != 0) { __co_recycle(s); return; } kh[7] = hd[0]; kh[8] = kh[5]; return; } "
                             + $"if (!gameObject.activeInHierarchy) {{ {inactive2} __co_kill(k); __co_wf(s); return; }} "
                             + "__co_on[hd[1]] = 0; hd[5] = 0; hd[8] = 0; if (hd[6] != 0) { __co_recycle(s); __co_kill(k); return; } kh[7] = hd[0]; kh[8] = 1; kh[3] = 1; __co_step(k); }";

                // 次のフレーム（yield return null など）。待ち行列に入れ、まだ予約していなければイベントを 1 つ予約する（ビヘイビアごとに 1 フレーム 1 回。
                // 予約のたびに確保があるため。#174）。届いたら、前のフレームまでに待ち始めたものを、待ち始めた順に全部再開する
                // （同じビヘイビアの中の順は Unity と同じ。ビヘイビアをまたいだ順は、Unity と違うことがある）
                // 予約は、そのフレームで最初に待ち始めたときにする（前のフレームの予約がまだ届いていなくても、それは今のフレームに届くので、別に予約する）。
                // ビヘイビアどうしの再開の順は、前のフレームで各ビヘイビアが最初に待ち始めた順になる
                yield return "private int __co_fr = -1;";
                yield return "protected void __co_wf(object[] s) { int[] hd = (int[])s[0]; __co_on[hd[1]] = 0; hd[5] = 0; hd[8] = 0; if (hd[6] != 0) { __co_recycle(s); return; } "
                             // 行列の末尾に届いたら、前に詰める。半分より多く埋まっているときだけ、2 倍の配列に広げる（毎フレーム待つものが多いと、詰めるたびに確保していた。#174）
                             + "if (__co_fh + __co_fn == __co_fq.Length) { int[] g = __co_fq; int[] gf = __co_ff; if (__co_fn * 2 > __co_fq.Length) { g = new int[__co_fq.Length * 2]; gf = new int[__co_fq.Length * 2]; } "
                             + "for (int i = 0; i < __co_fn; i++) { g[i] = __co_fq[__co_fh + i]; gf[i] = __co_ff[__co_fh + i]; } __co_fq = g; __co_ff = gf; __co_fh = 0; } "
                             + "__co_fq[__co_fh + __co_fn] = hd[0]; __co_ff[__co_fh + __co_fn] = UnityEngine.Time.frameCount; __co_fn++; int f = UnityEngine.Time.frameCount; if (__co_fr != f) { __co_fr = f; SendCustomEventDelayedFrames(\"__co_frame\", 1); } }";
                // 待ち始めたフレームのうちに呼ばれたら（外から呼ばれたときなど）、何も再開しない。再開したものがまた待つと、行列の後ろに今のフレームで入るので、ここでは再開しない
                yield return "public void __co_frame() { int f = UnityEngine.Time.frameCount; "
                             + "while (__co_fn > 0 && __co_ff[__co_fh] < f) { int c = __co_fq[__co_fh]; __co_fh++; __co_fn--; if (__co_fn == 0) __co_fh = 0; object[] s = __co_get(c); if (s != null) __co_step(s); } }";

                // NaN・正の無限大は、Unity と同じく二度と再開しない（NaN は Unity と同じ警告を出す）。#107 の追加テスト案の 12
                string naninf = "if (t != t) { UnityEngine.Debug.LogWarning(\"float.NaN has been passed into WaitForSeconds which will result in an infinite wait time.\"); __co_park(s); return; } if (t > float.MaxValue) { __co_park(s); return; } ";
                // 秒（WaitForSeconds）。待ち始めたフレームのうちに届いたら（ごく短い秒）、Unity と同じく次のフレームまで待つ。イベントは期限の順に届くので、届くたびに期限がいちばん早いもの（同じなら先に予約したもの）を再開する
                yield return "protected void __co_ws(object[] s, float t) { if (t <= 0f) { __co_wf(s); return; } " + naninf + " int[] hd = (int[])s[0]; __co_on[hd[1]] = 0; hd[5] = 0; hd[8] = 0; if (hd[6] != 0) { __co_recycle(s); return; } "
                             + "if (__co_sn == __co_sh.Length) { int[] g = new int[__co_sn * 2]; float[] gt = new float[__co_sn * 2]; int[] gf = new int[__co_sn * 2]; for (int i = 0; i < __co_sn; i++) { g[i] = __co_sh[i]; gt[i] = __co_st[i]; gf[i] = __co_sf[i]; } __co_sh = g; __co_st = gt; __co_sf = gf; } "
                             + "__co_sh[__co_sn] = hd[0]; __co_st[__co_sn] = UnityEngine.Time.time + t; __co_sf[__co_sn] = UnityEngine.Time.frameCount; __co_sn++; SendCustomEventDelayedSeconds(\"__co_sec\", t); }";
                yield return "public void __co_sec() { if (__co_sn == 0) return; int b = 0; for (int i = 1; i < __co_sn; i++) if (__co_st[i] < __co_st[b]) b = i; int c = __co_sh[b]; int sf = __co_sf[b]; "
                             + "for (int i = b + 1; i < __co_sn; i++) { __co_sh[i - 1] = __co_sh[i]; __co_st[i - 1] = __co_st[i]; __co_sf[i - 1] = __co_sf[i]; } __co_sn--; object[] s = __co_get(c); if (s == null) return; "
                             + "if (sf >= UnityEngine.Time.frameCount) { __co_wf(s); return; } __co_step(s); }";
            }
        }
    }
}
