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
    ///   届いたイベントは、待っている番号を予約した順に再開する。止めた状態の番号は見つからないので捨てる
    /// - ほかの段（List・デリゲート・普通のクラスなど）を書き換えたあとの、最後の段に置く（状態に入れる値の型が、Udon の型になっているため）
    /// - 行は増やさない（メソッドの頭はその行に、yield はその行に置き換え、生成したメンバーはクラスの閉じ括弧の行に置く）
    /// </summary>
    internal static partial class CoroutineLowering
    {
        private const string EnumeratorName = "System.Collections.IEnumerator";
        private const string CoroutineName = "UnityEngine.Coroutine";
        private const string MonoBehaviourName = "UnityEngine.MonoBehaviour";
        private const string WaitForSecondsName = "UnityEngine.WaitForSeconds";

        public static bool MayHaveCoroutines(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is YieldStatementSyntax
                || (n is IdentifierNameSyntax id && id.Identifier.ValueText == "StartCoroutine")));

        // ---- 型ごとの情報 ----------------------------------------------------

        private sealed class Coroutine
        {
            public MethodDeclarationSyntax Declaration;
            public IMethodSymbol Symbol;
            public int Id;
            public string StepName;
        }

        private sealed class TypeWork
        {
            public INamedTypeSymbol Type;
            public readonly List<Coroutine> Coroutines = new List<Coroutine>();
            /// <summary>状態の表・待つ処理などを置く宣言（partial のときは最初の宣言）</summary>
            public ClassDeclarationSyntax Host;
        }

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = Compilations.For(trees, references);

            // 1) コルーチンを使う UdonSharpBehaviour と、そのコルーチンのメソッド（番号は tree の順に振る）
            var works = new Dictionary<INamedTypeSymbol, TypeWork>(SymbolEqualityComparer.Default);
            var classes = new List<(SyntaxTree Tree, ClassDeclarationSyntax Class, TypeWork Work)>();
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    List<MethodDeclarationSyntax> methods = cls.Members.OfType<MethodDeclarationSyntax>().Where(m => IsCoroutine(model, m)).ToList();
                    if (methods.Count == 0 && !OwnNodes(cls).OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == "StartCoroutine"))
                        continue;
                    if (!works.TryGetValue(type, out TypeWork work))
                        works[type] = work = new TypeWork { Type = type, Host = cls };
                    foreach (MethodDeclarationSyntax m in methods)
                    {
                        int id = work.Coroutines.Count;
                        work.Coroutines.Add(new Coroutine
                        {
                            Declaration = m, Symbol = model.GetDeclaredSymbol(m), Id = id,
                            StepName = $"__co_{m.Identifier.ValueText}_{id}",
                        });
                    }
                    classes.Add((tree, cls, work));
                }
            }
            if (classes.Count == 0)
                return trees;

            // 2) ファイルごとに書き換える
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
                foreach (var (_, cls, work) in mine)
                    new ClassRewriter(model, cls, work, edits, diagnostics).Run();
                result.Add(CSharpSyntaxTree.ParseText(edits.Apply(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8));
            }
            return result;
        }

        /// <summary>IEnumerator を返し、本体に yield があるメソッド（ラムダ・ローカル関数の中の yield は数えない）</summary>
        private static bool IsCoroutine(SemanticModel model, MethodDeclarationSyntax m) =>
            m.Body != null && Yields(m.Body).Any()
            && model.GetDeclaredSymbol(m) is IMethodSymbol symbol && symbol.ReturnType.ToDisplayString() == EnumeratorName;

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

            public void Add(TextSpan span, string text) => _list.Add((span, text));

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

            public string Apply() =>
                _text.WithChanges(_list.OrderBy(e => e.Span.Start).Select(e => new TextChange(e.Span, e.Text))).ToString();
        }

        /// <summary>text の改行の数だけの改行（置き換えたところのうしろの行をずらさないため）</summary>
        private static string LineBreaks(string text) => new string('\n', text.Count(c => c == '\n'));

        // ---- クラスの書き換え ----------------------------------------------------

        private sealed partial class ClassRewriter
        {
            private readonly SemanticModel _model;
            private readonly ClassDeclarationSyntax _cls;
            private readonly TypeWork _work;
            private readonly Edits _edits;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly List<string> _members = new List<string>();

            public ClassRewriter(SemanticModel model, ClassDeclarationSyntax cls, TypeWork work, Edits edits, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _cls = cls;
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
                        RewriteStart(call);
                    else if (node is TypeSyntax type && IsHandleType(type))
                        _edits.Add(type.Span, "int");
                }

                // 2) コルーチンのメソッド → その場に状態機械、閉じ括弧の行に開始のメソッド
                foreach (Coroutine co in _work.Coroutines.Where(c => c.Declaration.Parent == _cls))
                    RewriteCoroutine(co);

                if (_cls == _work.Host)
                    _members.AddRange(Runtime());

                if (_members.Count > 0)
                    _edits.Add(new TextSpan(_cls.CloseBraceToken.SpanStart, 0), string.Join(" ", _members) + " ");
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

            private void RewriteStart(InvocationExpressionSyntax call)
            {
                if (!(_model.SymbolInfoOf(call).Symbol is IMethodSymbol method) || method.Name != "StartCoroutine"
                    || method.ContainingType.ToDisplayString() != MonoBehaviourName)
                    return;
                SimpleNameSyntax name = call.Expression is MemberAccessExpressionSyntax access ? access.Name : call.Expression as SimpleNameSyntax;
                if (name == null)
                    return;
                if (method.Parameters.Length != 1 || method.Parameters[0].Type.ToDisplayString() != EnumeratorName)
                {
                    Error(call, Loc.T("文字列で指定する StartCoroutine は未対応です。StartCoroutine(Foo()) の形で書いてください", "StartCoroutine with a method name string is not supported. Write StartCoroutine(Foo())"));
                    return;
                }
                if (call.Expression is MemberAccessExpressionSyntax receiver && !(receiver.Expression is ThisExpressionSyntax || receiver.Expression is BaseExpressionSyntax))
                {
                    Error(call, Loc.T("ほかのビヘイビアの StartCoroutine は未対応です。そのビヘイビアのメソッドの中で開始してください", "Calling StartCoroutine on another behaviour is not supported. Start it from a method of that behaviour"));
                    return;
                }
                // 戻り値を使わないときは、開始したあとに何もしないメソッドにする（入れ子で呼ばれても、ローカル変数が上書きされて困ることがない）
                _edits.Add(name.Span, call.Parent is ExpressionStatementSyntax ? "__co_go" : "__co_start");
            }

            private void RewriteCoroutine(Coroutine co) => new BodyWriter(this, co).Run();

            /// <summary>yield return の値に合わせた、待つ処理の呼び出し（未対応ならエラーにして null）</summary>
            private string Wait(ExpressionSyntax value)
            {
                if (value.IsKind(SyntaxKind.NullLiteralExpression))
                    return "__co_wf(__s);";
                if (value is ObjectCreationExpressionSyntax create && create.ArgumentList?.Arguments.Count == 1
                    && _model.GetTypeInfo(create).Type?.ToDisplayString() == WaitForSecondsName)
                    return $"__co_ws(__s, {_edits.Take(create.ArgumentList.Arguments[0].Expression)});";
                Error(value, Loc.T("この yield return の値は、まだ対応していません（null・new WaitForSeconds(秒) に対応しています）", "This yield return value is not supported yet (null and new WaitForSeconds(seconds) are supported)"));
                return null;
            }

            /// <summary>状態の表・開始・待つ処理（クラスに 1 組）</summary>
            private IEnumerable<string> Runtime()
            {
                int count = _work.Coroutines.Count;
                yield return "private object[] __co_slots = new object[8]; private int __co_used;";
                yield return $"private int[] __co_free = new int[{System.Math.Max(count, 1)}];";
                yield return "private int[] __co_fq = new int[8]; private int[] __co_fp = new int[8]; private int __co_fqn; private bool __co_fs;";
                yield return "private int[] __co_sh = new int[8]; private float[] __co_st = new float[8]; private int __co_sn;";

                // 状態を作る（同じメソッドの空いた状態があれば使い回す）。見出し：[0] 番号 [1] メソッドの番号 [2] 再開する位置 [3] 開始したか [4] 空きのつなぎ
                yield return "private object[] __co_alloc(int id, int size) { int f = __co_free[id]; if (f != 0) { object[] r = (object[])__co_slots[f - 1]; int[] rh = (int[])r[0]; __co_free[id] = rh[4]; rh[2] = 0; rh[3] = 0; rh[4] = 0; return r; } "
                             + "if (__co_used == __co_slots.Length) { object[] g = new object[__co_used * 2]; for (int i = 0; i < __co_used; i++) g[i] = __co_slots[i]; __co_slots = g; } "
                             + "object[] s = new object[size]; int[] h = new int[5]; h[0] = 65536 | (__co_used + 1); h[1] = id; s[0] = h; __co_slots[__co_used] = s; __co_used++; return s; }";
                yield return "private object[] __co_get(int c) { int i = (c & 65535) - 1; if (i < 0 || i >= __co_used) return null; object[] s = (object[])__co_slots[i]; if (((int[])s[0])[0] != c) return null; return s; }";
                // 返す：世代を進めて古い番号を無効にし、参照を切って、空きのつなぎに入れる
                yield return "private void __co_release(object[] s) { int[] h = (int[])s[0]; int g = (h[0] >> 16) + 1; if (g > 32767) g = 1; h[0] = (g << 16) | (h[0] & 65535); h[2] = -1; for (int i = 1; i < s.Length; i++) s[i] = null; h[4] = __co_free[h[1]]; __co_free[h[1]] = h[0] & 65535; }";
                yield return "private void __co_done(object[] s) { __co_release(s); }";

                string inactive = "UnityEngine.Debug.LogError(\"Coroutine couldn't be started because the the game object '\" + gameObject.name + \"' is inactive!\");";
                yield return $"private void __co_go(int c) {{ object[] s = __co_get(c); if (s == null) return; int[] h = (int[])s[0]; if (h[3] != 0) return; if (!gameObject.activeInHierarchy) {{ {inactive} __co_release(s); return; }} h[3] = 1; __co_step(s); }}";
                // 戻り値を使う開始は、入れ子で呼ばれても戻り値を失わないよう RecursiveMethod にする
                yield return $"[UdonSharp.RecursiveMethod] private int __co_start(int c) {{ object[] s = __co_get(c); if (s == null) return 0; int[] h = (int[])s[0]; if (h[3] != 0) return c; if (!gameObject.activeInHierarchy) {{ {inactive} __co_release(s); return 0; }} h[3] = 1; __co_step(s); return c; }}";

                var cases = new StringBuilder();
                foreach (Coroutine co in _work.Coroutines)
                    cases.Append($"case {co.Id}: {co.StepName}(s); return; ");
                yield return $"private void __co_step(object[] s) {{ switch (((int[])s[0])[1]) {{ {cases}}} }}";

                // 次のフレーム（yield return null）
                yield return "private void __co_wf(object[] s) { if (__co_fqn == __co_fq.Length) { int[] g = new int[__co_fqn * 2]; for (int i = 0; i < __co_fqn; i++) g[i] = __co_fq[i]; __co_fq = g; } __co_fq[__co_fqn] = ((int[])s[0])[0]; __co_fqn++; "
                             + "if (!__co_fs) { __co_fs = true; SendCustomEventDelayedFrames(\"__co_frame\", 1); } }";
                yield return "public void __co_frame() { int[] q = __co_fq; int n = __co_fqn; __co_fq = __co_fp; __co_fp = q; __co_fqn = 0; __co_fs = false; for (int i = 0; i < n; i++) { object[] s = __co_get(q[i]); if (s != null) __co_step(s); } }";

                // 秒（WaitForSeconds）。イベントは期限の順に届くので、届くたびに期限がいちばん早いもの（同じなら先に予約したもの）を再開する
                yield return "private void __co_ws(object[] s, float t) { if (__co_sn == __co_sh.Length) { int[] g = new int[__co_sn * 2]; float[] gt = new float[__co_sn * 2]; for (int i = 0; i < __co_sn; i++) { g[i] = __co_sh[i]; gt[i] = __co_st[i]; } __co_sh = g; __co_st = gt; } "
                             + "__co_sh[__co_sn] = ((int[])s[0])[0]; __co_st[__co_sn] = UnityEngine.Time.time + t; __co_sn++; SendCustomEventDelayedSeconds(\"__co_sec\", t); }";
                yield return "public void __co_sec() { if (__co_sn == 0) return; int b = 0; for (int i = 1; i < __co_sn; i++) if (__co_st[i] < __co_st[b]) b = i; int c = __co_sh[b]; "
                             + "for (int i = b + 1; i < __co_sn; i++) { __co_sh[i - 1] = __co_sh[i]; __co_st[i - 1] = __co_st[i]; } __co_sn--; object[] s = __co_get(c); if (s != null) __co_step(s); }";
            }
        }
    }
}
