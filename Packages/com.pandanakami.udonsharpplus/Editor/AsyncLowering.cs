using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Edits = UdonSharpPlus.CoroutineLowering.Edits;

namespace UdonSharpPlus
{
    /// <summary>
    /// UniTask の async / await（Redmine #207）。UdonSharpBehaviour の async メソッドを、コルーチン（#107・#174）にふつうの C# で書き換え、
    /// あとの動き（状態機械・待つ処理・停止・デバッグ情報）はコルーチンの段に任せる。コルーチンの下ごしらえ（Prepare）より前に置く。
    ///
    ///   async UniTask Foo(int n) { A; await UniTask.Delay(500); B; }
    ///     → System.Collections.IEnumerator Foo(int n) { A; yield return new UnityEngine.WaitForSeconds((500) / 1000f); B; yield break; }
    ///   Foo(3); / Foo(3).Forget();        → StartCoroutine(Foo(3));
    ///   await Foo(3);（async の中）         → yield return Foo(3);（子のコルーチンを待つ）
    ///   await UniTask.Yield() / NextFrame() → yield return null;
    ///   await UniTask.DelayFrame(n)         → for (...) yield return null;（n フレーム。0 は Yield と同じで 1 フレーム）
    ///   await UniTask.WaitUntil(f) / WaitWhile(f) → yield return new UnityEngine.WaitUntil(f) / WaitWhile(f)
    ///   async void Start() / override async void Interact() など、Udon がイベント名で呼ぶもの
    ///     → void Start() { StartCoroutine(__async_X_Start()); } private IEnumerator __async_X_Start()
    ///
    /// UniTask を使わないプロジェクトでは、この段は何もしない（UniTask のアセンブリは参照せず、型の名前の文字だけで見分ける）。
    ///   async UniTask&lt;int&gt; Foo(int n) { ...; return n + 1; }
    ///     → IEnumerator Foo(int n, object[] __ar) { ...; { __ar[0] = (object)(n + 1); yield break; } yield break; }
    ///   var x = await Foo(3);   → object[] __ar0 = new object[1]; yield return Foo(3, __ar: __ar0); var x = ((int)__ar0[0]);
    ///   await UniTask.WhenAll(A(), B());  → 子を全部始めて、順に待つ（値を返すものは、値を T[] にする）
    ///   戻り値の置き場は、呼ぶ側が開始ごとに作る（コルーチンの状態は、終わると使い回されるので、状態の中には置かない）
    /// WhenAny・CancellationToken・変数に入れた UniTask は、エラーにして案内する
    /// </summary>
    internal static class AsyncLowering
    {
        private const string UniTaskName = "Cysharp.Threading.Tasks.UniTask";
        private const string UniTaskVoidName = "Cysharp.Threading.Tasks.UniTaskVoid";
        private const string CancellationTokenName = "System.Threading.CancellationToken";

        private static readonly string[] Words = { "async", "UniTask" };

        /// <summary>Udon がイベント名で呼ぶ、override ではないもの（Unity のメッセージ）。override のものは、名前によらずイベントとして扱う</summary>
        private static readonly HashSet<string> UnityMessages = new HashSet<string>
        {
            "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy",
            "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay",
            "OnParticleCollision", "OnControllerColliderHit", "OnAnimatorIK", "OnAnimatorMove", "OnBecameVisible", "OnBecameInvisible",
            "OnMouseDown", "OnMouseUp", "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag",
        };

        public static bool MayHaveAsync(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => CoroutineLowering.MayContain(t.GetRoot(), Words) && t.GetRoot().DescendantTokens().Any(token => token.IsKind(SyntaxKind.AsyncKeyword)));

        private sealed class AsyncMethod
        {
            public MethodDeclarationSyntax Declaration;
            public IMethodSymbol Symbol;
            public INamedTypeSymbol Type;
            /// <summary>書き換えたコルーチンの名前（イベントのものは __async_型_名前）</summary>
            public string CoroutineName;
            /// <summary>Udon がイベント名で呼ぶので、同じ名前の void のメソッドを残し、コルーチンを始めさせる</summary>
            public bool Wrapper;
            /// <summary>値を返す async（UniTask&lt;T&gt;）の T（値を返さないものは null）。戻り値は、呼ぶ側が渡す object[1]（最後の引数 __ar）に入れる</summary>
            public ITypeSymbol ResultType;
        }

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = Compilations.For(trees, references);

            // 1) async のメソッドを集める（UdonSharpBehaviour のものだけ。そのほかのクラスは触らない）
            var methods = new Dictionary<IMethodSymbol, AsyncMethod>(SymbolEqualityComparer.Default);
            var behaviourNames = new HashSet<string>();
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type))
                        continue;
                    bool behaviour = InterfaceLowering.IsUdonSharpBehaviour(type);
                    if (behaviour)
                        behaviourNames.Add(type.Name);
                    foreach (MethodDeclarationSyntax m in cls.Members.OfType<MethodDeclarationSyntax>())
                    {
                        bool isAsync = m.Modifiers.Any(SyntaxKind.AsyncKeyword);
                        // async か、戻り値に UniTask と書いたメソッドだけ（シンボルの問い合わせは、そのメソッドだけにする）
                        if (!isAsync && m.ReturnType.ToString().IndexOf("UniTask", System.StringComparison.Ordinal) < 0)
                            continue;
                        if (!(model.GetDeclaredSymbol(m) is IMethodSymbol symbol))
                            continue;
                        string kind = KindOf(symbol.ReturnType);
                        if (!behaviour)
                        {
                            // 普通のクラスの中の UniTask は、UdonSharpBehaviour から使われるときだけエラーにする（コルーチンと同じ。#49）。
                            // MonoBehaviour などのクラスは、普通の Unity の async なので触らない
                            // UniTask・UniTaskVoid を返すか、async void のものだけ（ふつうの void のメソッドは、UniTask と関係ない）
                            bool usesUniTask = kind == "unitask" || kind == "generic" || isAsync && kind == "void";
                            if (usesUniTask && ClassLowering.IsPlainClass(type))
                                diagnostics.Error(m.Identifier.GetLocation(), Loc.T(
                                    "UdonSharpBehaviour でないクラスのメソッドの async（UniTask）は未対応です。async のメソッドは UdonSharpBehaviour に書き、普通のクラスからは呼ばないでください",
                                    "async (UniTask) in a method of a class that is not an UdonSharpBehaviour is not supported. Write async methods in the UdonSharpBehaviour"),
                                    ClassLowering.TagOf(type));
                            continue;
                        }
                        if (!isAsync)
                        {
                            if (kind != null && kind != "other" && kind != "void")
                                diagnostics.Error(m.Identifier.GetLocation(), Loc.T(
                                    $"{symbol.ReturnType.Name} を返す、async でないメソッドは未対応です。async を付けて書いてください",
                                    $"A method returning {symbol.ReturnType.Name} without async is not supported. Add async"));
                            continue;
                        }
                        string problem = ProblemOf(m, symbol, kind);
                        if (problem != null)
                        {
                            diagnostics.Error(m.Identifier.GetLocation(), problem);
                            continue;
                        }
                        bool wrapper = symbol.IsOverride || UnityMessages.Contains(symbol.Name);
                        methods[symbol.OriginalDefinition] = new AsyncMethod
                        {
                            Declaration = m,
                            Symbol = symbol,
                            Type = type,
                            Wrapper = wrapper,
                            CoroutineName = wrapper ? $"__async_{type.Name}_{symbol.Name}" : symbol.Name,
                            ResultType = kind == "generic" ? ((INamedTypeSymbol)symbol.ReturnType).TypeArguments[0] : null,
                        };
                    }
                }
            }
            if (methods.Count == 0)
                return trees;
            // イベントの override（Interact など）は、同じ名前の void のメソッドが残る。自分で書いた virtual の async を override するものは、
            // 基底と同じ形（コルーチン）にする。基底がイベントの override なら、派生も void のまま（戻り値の型をそろえる）
            foreach (AsyncMethod am in methods.Values)
            {
                am.Wrapper = IsWrapper(am.Symbol, methods);
                am.CoroutineName = am.Wrapper ? $"__async_{am.Type.Name}_{am.Symbol.Name}" : am.Symbol.Name;
            }

            // 2) ファイルごとに書き換える。async・UniTask の文字か、async メソッドの名前（呼ぶだけのクラス）を含むクラスだけを見る
            string[] words = Words.Concat(methods.Values.Select(m => m.Symbol.Name)).Distinct().ToArray();
            var result = new List<SyntaxTree>(trees.Count);
            int temp = 0;
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                Edits edits = null;
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    if (!CoroutineLowering.MayContain(cls, words))
                        continue;
                    edits = edits ?? new Edits(tree.GetText());
                    new Rewriter(model, cls, edits, diagnostics, methods).Run(ref temp);
                }
                SyntaxTree next = edits == null || !edits.Any
                    ? tree
                    : CSharpSyntaxTree.ParseText(edits.Apply(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8);
                ReportLeftovers(next, behaviourNames, diagnostics);
                result.Add(next);
            }
            return result;
        }

        /// <summary>Udon がイベント名で呼ぶ（void のメソッドを残す）メソッドか：Unity のイベントの名前・イベントの override・それを override するもの</summary>
        private static bool IsWrapper(IMethodSymbol symbol, Dictionary<IMethodSymbol, AsyncMethod> methods)
        {
            if (UnityMessages.Contains(symbol.Name))
                return true;
            if (!symbol.IsOverride || symbol.OverriddenMethod == null)
                return false;
            return methods.TryGetValue(symbol.OverriddenMethod.OriginalDefinition, out AsyncMethod baseMethod)
                ? IsWrapper(baseMethod.Symbol, methods)
                : true;
        }

        /// <summary>戻り値の型の種類：UniTask・UniTaskVoid・void・値を返す UniTask&lt;T&gt;（"generic"）・Task など（"other"）</summary>
        private static string KindOf(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Void)
                return "void";
            string name = type.OriginalDefinition.ToDisplayString();
            if (name == UniTaskName || name == UniTaskVoidName)
                return "unitask";
            if (name == UniTaskName + "<T>" || name == "Cysharp.Threading.Tasks.UniTask<T>")
                return "generic";
            if (name.StartsWith("Cysharp.Threading.Tasks."))
                return "other";
            if (name.StartsWith("System.Threading.Tasks."))
                return "other";
            return null;
        }

        /// <summary>対応しない async メソッドなら、エラーの文（対応するなら null）</summary>
        private static string ProblemOf(MethodDeclarationSyntax m, IMethodSymbol symbol, string kind)
        {
            if (kind == "generic" && UnityMessages.Contains(symbol.Name))
                return Loc.T("イベントのメソッド（Start・Interact など）は、値を返せません", "An event method (Start, Interact, ...) cannot return a value");
            if (kind == "other" || kind == null)
                return Loc.T("UdonSharpBehaviour の async メソッドは、UniTask・UniTaskVoid・void を返すものだけ書けます",
                    "An async method in an UdonSharpBehaviour must return UniTask, UniTaskVoid or void");
            if (symbol.IsStatic)
                return Loc.T("static の async メソッドは未対応です（コルーチンと同じ）", "A static async method is not supported (same as coroutines)");
            if (m.TypeParameterList != null)
                return Loc.T("ジェネリックの async メソッドは未対応です", "A generic async method is not supported");
            if (m.Body == null)
                return Loc.T("async メソッドは { } の本体で書いてください（=> の式の本体は未対応です）", "Write an async method with a { } body (an expression body => is not supported)");
            if (m.ParameterList.Parameters.Any(p => p.Modifiers.Any(x => x.IsKind(SyntaxKind.RefKeyword) || x.IsKind(SyntaxKind.OutKeyword) || x.IsKind(SyntaxKind.InKeyword))))
                return Loc.T("ref・out・in の引数がある async メソッドは書けません", "An async method cannot have ref, out or in parameters");
            return null;
        }

        /// <summary>書き換えたあとに残った UniTask・UniTaskVoid の名前（変数・フィールド・引数・式の中）を、案内つきのエラーにする</summary>
        private static void ReportLeftovers(SyntaxTree tree, HashSet<string> behaviourNames, InterfaceLowering.LoweringDiagnostics diagnostics)
        {
            if (!CoroutineLowering.MayContain(tree.GetRoot(), new[] { "UniTask" }))
                return;
            foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
            {
                // UdonSharpBehaviour の中だけ（ほかのクラスの UniTask は、普通の Unity のもの）
                if (!behaviourNames.Contains(cls.Identifier.ValueText))
                    continue;
                var reported = new HashSet<int>();
                foreach (SyntaxToken token in cls.DescendantTokens(n => !(n is BaseTypeDeclarationSyntax b) || n == cls))
                {
                    if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != "UniTask" && token.ValueText != "UniTaskVoid")
                        continue;
                    int line = token.GetLocation().GetLineSpan().StartLinePosition.Line;
                    if (!reported.Add(line))
                        continue;
                    diagnostics.Error(token.GetLocation(), Loc.T(
                        "UniTask は、async メソッドの戻り値と、await の中（UniTask.Delay など）、async の呼び出しを入れる変数・フィールドだけで使えます。UniTask を引数・戻り値・配列・List に入れる書き方は未対応です",
                        "UniTask can only be used as the return type of an async method, inside await (UniTask.Delay, ...), and as a variable or field that holds a call to an async method. Passing a UniTask as an argument or return value, or keeping it in an array or list, is not supported"));
                }
            }
        }

        private sealed class Rewriter
        {
            private readonly SemanticModel _model;
            private readonly ClassDeclarationSyntax _cls;
            private readonly Edits _edits;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly Dictionary<IMethodSymbol, AsyncMethod> _methods;

            public Rewriter(SemanticModel model, ClassDeclarationSyntax cls, Edits edits, InterfaceLowering.LoweringDiagnostics diagnostics,
                Dictionary<IMethodSymbol, AsyncMethod> methods)
            {
                _model = model;
                _cls = cls;
                _edits = edits;
                _diagnostics = diagnostics;
                _methods = methods;
            }

            private void Error(SyntaxNode at, string message) => _diagnostics.Error(at.GetLocation(), message);

            private AsyncMethod AsyncOf(SyntaxNode call) =>
                call is InvocationExpressionSyntax inv && _model.GetSymbolInfo(inv).Symbol is IMethodSymbol symbol
                && _methods.TryGetValue(symbol.OriginalDefinition, out AsyncMethod found) ? found : null;

            /// <summary>node を含む async メソッド</summary>
            private AsyncMethod OwnerOf(SyntaxNode node)
            {
                MethodDeclarationSyntax m = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                return m != null && _model.GetDeclaredSymbol(m) is IMethodSymbol s && _methods.TryGetValue(s.OriginalDefinition, out AsyncMethod found) ? found : null;
            }

            /// <summary>いま書き換えている文の最初の行（0 始まり）。計算で作る待ちの引数を、元の行に合わせて置くのに使う（例外の位置を元の行に出す）</summary>
            private int _anchorLine;

            private static int LineOf(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line;

            /// <summary>引数の式。元の文で、文の最初の行より下にあるときは、その行数の改行を前に足して、生成した文の中でも同じ行に置く</summary>
            private string Arg(ExpressionSyntax e) => new string('\n', System.Math.Max(0, LineOf(e) - _anchorLine)) + _edits.Take(e);

            /// <summary>sb の改行の数を、node の行（文の最初の行からの数）に合わせる（足りないときだけ足す）</summary>
            private void Pad(StringBuilder sb, SyntaxNode node)
            {
                int have = 0;
                for (int i = 0; i < sb.Length; i++)
                    if (sb[i] == '\n')
                        have++;
                int want = LineOf(node) - _anchorLine;
                if (want > have)
                    sb.Append('\n', want - have);
            }

            private static string TypeText(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            public void Run(ref int temp)
            {
                var handled = new HashSet<SyntaxNode>();
                var handledAwaits = new HashSet<AwaitExpressionSyntax>();

                // 1) async メソッドの中の await と return。外側の文のほうが先に出ないよう、うしろから（中のものを先に）書き換える
                var asyncBodies = _cls.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => _model.GetDeclaredSymbol(m) is IMethodSymbol s && _methods.ContainsKey(s.OriginalDefinition)).ToList();
                var candidates = new List<SyntaxNode>();
                foreach (MethodDeclarationSyntax m in asyncBodies)
                {
                    AsyncMethod info = _methods[((IMethodSymbol)_model.GetDeclaredSymbol(m)).OriginalDefinition];
                    foreach (SyntaxNode n in OwnBody(m))
                    {
                        if (n is AwaitExpressionSyntax aw)
                        {
                            SyntaxNode statement = AwaitStatementOf(aw);
                            if (statement != null)
                                candidates.Add(statement);
                        }
                        else if (n is ReturnStatementSyntax r && !(r.Expression is AwaitExpressionSyntax) && (r.Expression == null || info.ResultType != null))
                            candidates.Add(r);
                    }
                }
                // 2) 待たずに呼ぶ（Foo(); ・ Foo().Forget();）。どのメソッドの中でも
                foreach (SyntaxNode n in OwnNodes().OfType<ExpressionStatementSyntax>())
                    if (CallStatementTarget(((ExpressionStatementSyntax)n).Expression) != null)
                        candidates.Add(n);
                // 2b) 変数・フィールドに入れた UniTask（#210）：宣言・代入・Forget。名前は先に集めて、それ以外の式は問い合わせない
                CollectTaskNames();
                if (_taskNames.Count > 0)
                {
                    foreach (SyntaxNode n in OwnNodes())
                        if (n is LocalDeclarationStatementSyntax ld && IsTaskDeclaration(ld) || n is ExpressionStatementSyntax es2 && IsTaskStatement(es2))
                            candidates.Add(n);
                    foreach (FieldDeclarationSyntax f in _cls.Members.OfType<FieldDeclarationSyntax>())
                        TaskField(f);
                }
                foreach (SyntaxNode node in candidates.Distinct().OrderByDescending(n => n.SpanStart))
                {
                    handled.Add(node);
                    if (node is ReturnStatementSyntax ret && !(ret.Expression is AwaitExpressionSyntax))
                        ReturnStatement(ret);
                    else if (node is ExpressionStatementSyntax call && CallStatementTarget(call.Expression) != null)
                        StartStatement(call);
                    else if (AwaitOf(node) == null)
                        TaskStatement(node);
                    else
                        AwaitStatement(node, handledAwaits, ref temp);
                }
                if (_taskNames.Count > 0)
                    CheckTaskUses();

                // 3) 書き換えていない await・async メソッドの呼び出し（式の途中・ほかのビヘイビアなど）はエラー
                foreach (MethodDeclarationSyntax m in asyncBodies)
                    foreach (AwaitExpressionSyntax aw in OwnBody(m).OfType<AwaitExpressionSyntax>())
                        if (!handledAwaits.Contains(aw))
                            Error(aw, Loc.T("await は、文（await UniTask.Delay(1000); など）か、await の値を受け取る形（var x = await Foo(); ・ x = await Foo(); ・ return await Foo(); ）で書いてください。式の途中の await は未対応です",
                                "Write await as a statement (await UniTask.Delay(1000);) or take its value as var x = await Foo();, x = await Foo(); or return await Foo();. An await in the middle of an expression is not supported"));
                foreach (InvocationExpressionSyntax call in OwnNodes().OfType<InvocationExpressionSyntax>())
                {
                    AsyncMethod target = AsyncOf(call);
                    if (target == null || IsHandledCall(call, handled))
                        continue;
                    Error(call, Loc.T(
                        $"async メソッド {target.Symbol.Name} は、Foo(); ・ Foo().Forget(); ・ async メソッドの中の await Foo(); の形だけで呼べます（値として使う書き方は未対応です）",
                        $"The async method {target.Symbol.Name} can only be called as Foo();, Foo().Forget() or await Foo(); inside an async method. Using it as a value is not supported"));
                }

                // 3b) UniTask のメソッドに async のラムダを渡す書き方（UniTask.Void(async () => ...) など）。デリゲートの段は、ソースのデリゲート型のものだけ見るので、ここで案内する
                foreach (AnonymousFunctionExpressionSyntax lambda in OwnNodes().OfType<AnonymousFunctionExpressionSyntax>())
                    if (lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) && lambda.Parent is ArgumentSyntax arg && arg.Parent?.Parent is InvocationExpressionSyntax outer
                        && _model.GetSymbolInfo(outer).Symbol is IMethodSymbol passed && passed.ContainingType.ToDisplayString() == UniTaskName)
                        Error(lambda, Loc.T($"UniTask.{passed.Name} に async のラムダを渡す書き方は未対応です。async のメソッドを書いて、Foo(); で呼んでください",
                            $"Passing an async lambda to UniTask.{passed.Name} is not supported. Write an async method and call it as Foo();"));

                // 4) メソッドの宣言
                foreach (MethodDeclarationSyntax m in asyncBodies)
                    Declaration(m, _methods[((IMethodSymbol)_model.GetDeclaredSymbol(m)).OriginalDefinition]);
            }

            /// <summary>cls のノード（入れ子の型は含めない）</summary>
            private IEnumerable<SyntaxNode> OwnNodes() => CoroutineLowering.OwnNodes(_cls);

            /// <summary>メソッドの本体のノード（ラムダ・ローカル関数の中は含めない）</summary>
            private static IEnumerable<SyntaxNode> OwnBody(MethodDeclarationSyntax m) =>
                m.Body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax));

            /// <summary>
            /// await を含む文（書き換える単位）。await が文の値全体のときだけ：await ...; ・ var x = await ...; ・ x = await ...; ・ return await ...;
            /// （式の途中の await は、前のものの評価の順番が変わるので書き換えない）。ほかは null
            /// </summary>
            private static SyntaxNode AwaitStatementOf(AwaitExpressionSyntax aw)
            {
                SyntaxNode p = aw.Parent;
                if (p is ExpressionStatementSyntax || p is ReturnStatementSyntax)
                    return p;
                if (p is EqualsValueClauseSyntax ev && ev.Parent is VariableDeclaratorSyntax d && d.Parent is VariableDeclarationSyntax v
                    && v.Variables.Count == 1 && v.Parent is LocalDeclarationStatementSyntax ld && ld.Modifiers.Count == 0)
                    return ld;
                if (p is AssignmentExpressionSyntax a && a.IsKind(SyntaxKind.SimpleAssignmentExpression) && a.Right == aw && a.Parent is ExpressionStatementSyntax es)
                    return es;
                return null;
            }

            /// <summary>statement の await</summary>
            private static AwaitExpressionSyntax AwaitOf(SyntaxNode statement)
            {
                switch (statement)
                {
                    case ExpressionStatementSyntax es:
                        return es.Expression as AwaitExpressionSyntax ?? (es.Expression as AssignmentExpressionSyntax)?.Right as AwaitExpressionSyntax;
                    case ReturnStatementSyntax r:
                        return r.Expression as AwaitExpressionSyntax;
                    case LocalDeclarationStatementSyntax ld:
                        return ld.Declaration.Variables[0].Initializer?.Value as AwaitExpressionSyntax;
                }
                return null;
            }

            /// <summary>破棄への代入の左側（_ = Foo(); の _）か。_ という名前の変数・フィールドは、破棄ではない（#212）</summary>
            private bool IsDiscard(ExpressionSyntax left) =>
                left is IdentifierNameSyntax id && id.Identifier.ValueText == "_" && _model.GetSymbolInfo(id).Symbol?.Kind == SymbolKind.Discard;

            /// <summary>式が、待たずに呼ぶ形（Foo() ・ Foo().Forget() ・ _ = Foo()）なら、呼ぶ async メソッド（なければ null）</summary>
            private AsyncMethod CallStatementTarget(ExpressionSyntax expression)
            {
                if (expression is AssignmentExpressionSyntax discard && discard.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && discard.Right is InvocationExpressionSyntax && IsDiscard(discard.Left))
                    return CallStatementTarget(discard.Right);
                if (expression is InvocationExpressionSyntax inv)
                {
                    AsyncMethod direct = AsyncOf(inv);
                    if (direct != null)
                        return direct;
                    if (inv.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Forget"
                        && inv.ArgumentList.Arguments.Count == 0 && access.Expression is InvocationExpressionSyntax inner)
                        return AsyncOf(inner);
                }
                return null;
            }

            /// <summary>書き換える文の、いちばん外側の式（await の中身。括弧は外す）</summary>
            private static ExpressionSyntax TopExpression(SyntaxNode statement)
            {
                ExpressionSyntax e = null;
                switch (statement)
                {
                    case ExpressionStatementSyntax es:
                        e = es.Expression;
                        if (e is AssignmentExpressionSyntax asg && (asg.Right is AwaitExpressionSyntax || asg.Right is InvocationExpressionSyntax))
                            e = asg.Right;
                        break;
                    case ReturnStatementSyntax r:
                        e = r.Expression;
                        break;
                    case LocalDeclarationStatementSyntax ld:
                        e = ld.Declaration.Variables[0].Initializer?.Value;
                        break;
                }
                if (e is AwaitExpressionSyntax a)
                    e = a.Expression;
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                return e;
            }

            /// <summary>書き換えた文（待たずに呼ぶ・await）の中の呼び出しか</summary>
            private bool IsHandledCall(InvocationExpressionSyntax call, HashSet<SyntaxNode> handled)
            {
                for (SyntaxNode n = call; n != null; n = n.Parent)
                {
                    if (handled.Contains(n))
                    {
                        // 文の「いちばん外側の呼び出し」と、WhenAll の引数の呼び出しだけが書き換えたもの。文の中の別の呼び出し（引数の中など）は、書き換えていない
                        ExpressionSyntax top = TopExpression(n);
                        if (top == call)
                            return true;
                        if (top is InvocationExpressionSyntax outer)
                        {
                            if (outer.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Forget" && access.Expression == call)
                                return true;
                            if (call.Parent is ArgumentSyntax arg && arg.Parent?.Parent == outer && (IsUniTaskMethod(outer, "WhenAll") || IsUniTaskMethod(outer, "WhenAny")))
                                return true;
                        }
                        return false;
                    }
                    if (n is MethodDeclarationSyntax)
                        break;
                }
                return false;
            }

            private bool IsUniTaskMethod(InvocationExpressionSyntax inv, string name) =>
                _model.GetSymbolInfo(inv).Symbol is IMethodSymbol m && m.IsStatic && m.Name == name && m.ContainingType.ToDisplayString() == UniTaskName;

            // ---- 文の書き換え ----------------------------------------------------

            /// <summary>return; → yield break; ・ return v;（UniTask&lt;T&gt;）→ { __ar[0] = v; yield break; }</summary>
            private void ReturnStatement(ReturnStatementSyntax ret)
            {
                string original = ret.ToString();
                if (ret.Expression == null)
                    _edits.Add(ret.Span, KeepLines("yield break;", original));
                else
                    _edits.Add(ret.Span, KeepLines($"{{ __ar[0] = (object)({_edits.Take(ret.Expression)}); yield break; }}", original));
            }

            /// <summary>Foo(); ・ Foo().Forget(); → StartCoroutine(Foo());（値を返すものは、使わない置き場を渡す）</summary>
            private void StartStatement(ExpressionStatementSyntax stmt)
            {
                // _ = Foo(); は、右側（Foo()）だけを見る（#212）
                bool assigned = stmt.Expression is AssignmentExpressionSyntax;
                var inv = (InvocationExpressionSyntax)(assigned ? ((AssignmentExpressionSyntax)stmt.Expression).Right : stmt.Expression);
                bool forget = AsyncOf(inv) == null;
                if (forget)
                    inv = (InvocationExpressionSyntax)((MemberAccessExpressionSyntax)inv.Expression).Expression;

                // イベントの override（Interact など）は、void のメソッドが残っている。コードからの呼び出しは、そのメソッドの呼び出しのままにする
                // （コルーチンの名前を直接呼ぶと、派生の override を飛ばして、基底のものが動く）。Forget() だけ外す
                AsyncMethod target = AsyncOf(inv);
                if (target.Wrapper && (inv.Expression is IdentifierNameSyntax
                    || inv.Expression is MemberAccessExpressionSyntax access && (access.Expression is ThisExpressionSyntax || access.Expression is BaseExpressionSyntax) && access.Name is IdentifierNameSyntax))
                {
                    if (forget || assigned)
                        _edits.Add(stmt.Span, KeepLines(_edits.Take(inv) + ";", stmt.ToString()));
                    return;
                }

                string call = CallCode(inv, "new object[1]");
                if (call == null)
                    return;
                string original = stmt.ToString();
                _edits.Add(stmt.Span, KeepLines($"StartCoroutine({call});", original));
            }

            /// <summary>
            /// async メソッドの呼び出しの文字（イベントのものはコルーチンの名前に変える）。値を返すものは、最後の引数に、戻り値の置き場（holder）を足す。
            /// ほかのビヘイビアの呼び出しは null（エラー）
            /// </summary>
            private string CallCode(InvocationExpressionSyntax call, string holder)
            {
                AsyncMethod target = AsyncOf(call);
                SimpleNameSyntax name;
                switch (call.Expression)
                {
                    case SimpleNameSyntax simple:
                        name = simple;
                        break;
                    case MemberAccessExpressionSyntax access when access.Expression is ThisExpressionSyntax || access.Expression is BaseExpressionSyntax:
                        name = access.Name;
                        break;
                    default:
                        Error(call, Loc.T($"ほかのビヘイビアの async メソッド（{call.Expression}）は未対応です。そのビヘイビアに、async を始める public メソッドを作って呼んでください",
                            $"An async method of another behaviour ({call.Expression}) is not supported. Add a public method that starts it to that behaviour and call it"));
                        return null;
                }
                if (name is GenericNameSyntax)
                {
                    Error(call, Loc.T("ジェネリックの async メソッドは未対応です", "A generic async method is not supported"));
                    return null;
                }
                // イベントの名前のメソッド（void のラッパーが残るもの）は、await・変数・WhenAll / WhenAny では使えない。
                // コルーチンの名前を直接呼ぶと、派生の override に振り分けられないため（待たずに呼ぶ Foo(); は、メソッドの呼び出しのまま）
                if (target.Wrapper)
                {
                    Error(call, Loc.T($"イベントの名前（{target.Symbol.Name}）の async メソッドは、await・変数・WhenAll / WhenAny では使えません（派生クラスの override に振り分けられません）。別の名前のメソッドにしてください",
                        $"An async method named like an event ({target.Symbol.Name}) cannot be used with await, in a variable, or in WhenAll / WhenAny (it would not dispatch to an override in a derived class). Give it another name"));
                    return null;
                }
                if (target.ResultType != null)
                    _edits.Add(new TextSpan(call.ArgumentList.CloseParenToken.SpanStart, 0),
                        (call.ArgumentList.Arguments.Count > 0 ? ", " : "") + "__ar: " + holder);
                return _edits.Take(call);
            }

            /// <summary>await ...; ・ var x = await ...; ・ x = await ...; ・ return await ...; → yield return ...;（値は holder から読む）</summary>
            private void AwaitStatement(SyntaxNode statement, HashSet<AwaitExpressionSyntax> handledAwaits, ref int temp)
            {
                AwaitExpressionSyntax aw = AwaitOf(statement);
                if (aw == null)
                    return;
                handledAwaits.Add(aw);
                _anchorLine = LineOf(statement);

                // コルーチンの状態機械が書けない文の中の await は、コルーチンの段のエラーより先に、await の言葉で案内する
                for (SyntaxNode n = statement.Parent; n != null && !(n is MethodDeclarationSyntax); n = n.Parent)
                    if (n is TryStatementSyntax || n is UsingStatementSyntax || n is LockStatementSyntax)
                    {
                        Error(aw, Loc.T("try・catch・finally・using・lock の中の await は未対応です（コルーチンと同じ）",
                            "await inside try, catch, finally, using or lock is not supported (same as coroutines)"));
                        return;
                    }

                ExpressionSyntax operand = aw.Expression;
                while (operand is ParenthesizedExpressionSyntax p)
                    operand = p.Expression;
                string code = null, value = null;
                bool block = false;
                TaskVar held = VarAt(operand);
                if (held != null)
                {
                    Use(operand);
                    code = $"yield return {held.Handle};";
                    value = held.Result == null ? null : $"(({TypeText(held.Result)}){held.Holder}[0])";
                }
                else if (operand is InvocationExpressionSyntax inv)
                    code = AwaitCode(inv, ref temp, out value, out block);
                if (code == null)
                {
                    if (!(operand is InvocationExpressionSyntax))
                        Error(operand, Loc.T("この await は未対応です。UniTask.Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...)・WhenAll(...)・WhenAny(...)、async メソッドの呼び出し、または UniTask の変数を await してください",
                            "This await is not supported. Await UniTask.Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...), WaitWhile(...), WhenAll(...), WhenAny(...), a call to an async method or a UniTask variable"));
                    return;
                }

                string original = statement.ToString();
                switch (statement)
                {
                    // _ = await Foo(); は、値を捨てて待つ（#212）
                    case ExpressionStatementSyntax discarded when discarded.Expression is AssignmentExpressionSyntax toDiscard && IsDiscard(toDiscard.Left):
                        _edits.Add(discarded.Span, KeepLines(block ? $"{{ {code} }}" : code, original));
                        break;
                    case ExpressionStatementSyntax es when es.Expression is AssignmentExpressionSyntax asg:
                    {
                        if (value == null || asg.Left.DescendantNodesAndSelf().Any(x => x is InvocationExpressionSyntax || x is AwaitExpressionSyntax))
                        {
                            Error(asg, value == null
                                ? Loc.T("値を返さない await の結果は、代入できません", "The result of an await that returns no value cannot be assigned")
                                : Loc.T("await の値を代入する左側には、変数・フィールド・要素を書いてください（メソッドの呼び出しは、評価の順番が変わるため未対応です）",
                                    "Write a variable, field or element on the left side of an assignment of an await (a method call is not supported because the evaluation order would change)"));
                            return;
                        }
                        // var (a, b) = await ...; は、宣言した変数があとで使えるよう、{ } で囲まない
                        string assignment = $"{code} {_edits.Take(asg.Left)} = {value};";
                        _edits.Add(es.Span, KeepLines(asg.Left is DeclarationExpressionSyntax ? assignment : "{ " + assignment + " }", original));
                        break;
                    }
                    case ExpressionStatementSyntax es:
                        _edits.Add(es.Span, KeepLines(block ? $"{{ {code} }}" : code, original));
                        break;
                    case LocalDeclarationStatementSyntax ld:
                    {
                        if (value == null)
                        {
                            Error(aw, Loc.T("値を返さない await の結果は、変数に入れられません", "The result of an await that returns no value cannot be stored in a variable"));
                            return;
                        }
                        _edits.Add(ld.Span, KeepLines($"{code} {ld.Declaration.Type} {ld.Declaration.Variables[0].Identifier.ValueText} = {value};", original));
                        break;
                    }
                    case ReturnStatementSyntax r:
                    {
                        AsyncMethod owner = OwnerOf(r);
                        if (value == null || owner?.ResultType == null)
                        {
                            Error(aw, Loc.T("return await は、値を返す async メソッド（UniTask<T>）の中で、値を返す async メソッドを await する形だけで書けます",
                                "return await can only be used in an async method returning a value (UniTask<T>) to await an async method that returns a value"));
                            return;
                        }
                        _edits.Add(r.Span, KeepLines($"{{ {code} __ar[0] = (object)(({TypeText(owner.ResultType)}){value}); yield break; }}", original));
                        break;
                    }
                }
            }

            /// <summary>
            /// await の式の、書き換え先の文（書けないものはエラーにして null）。値を返すもの（UniTask&lt;T&gt;・WhenAll）は、value に読み出す式を返し、
            /// block は、文が複数（置き場の宣言を含む）で、単独の文として置くときは { } で囲むもの
            /// </summary>
            private string AwaitCode(InvocationExpressionSyntax inv, ref int temp, out string value, out bool block)
            {
                value = null;
                block = false;
                AsyncMethod target = AsyncOf(inv);
                if (target != null)
                {
                    if (target.ResultType == null)
                    {
                        string call = CallCode(inv, null);
                        return call == null ? null : $"yield return {call};";
                    }
                    string holder = $"__ar{temp++}";
                    string valueCall = CallCode(inv, holder);
                    if (valueCall == null)
                        return null;
                    value = $"(({TypeText(target.ResultType)}){holder}[0])";
                    block = true;
                    return $"object[] {holder} = new object[1]; yield return {valueCall};";
                }
                if (!(_model.GetSymbolInfo(inv).Symbol is IMethodSymbol method) || !method.IsStatic || method.ContainingType.ToDisplayString() != UniTaskName)
                {
                    Error(inv, Loc.T("この await は未対応です。UniTask.Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...)・WhenAll(...)・WhenAny(...)、async メソッドの呼び出し、または UniTask の変数を await してください",
                        "This await is not supported. Await UniTask.Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...), WaitWhile(...), WhenAll(...), WhenAny(...), a call to an async method or a UniTask variable"));
                    return null;
                }

                Dictionary<string, ExpressionSyntax> args = ArgumentsOf(inv, method);
                foreach (KeyValuePair<string, ExpressionSyntax> arg in args)
                    if (_model.GetTypeInfo(arg.Value).ConvertedType?.ToDisplayString() == CancellationTokenName && !IsNoToken(arg.Value))
                    {
                        Error(arg.Value, Loc.T("CancellationToken は未対応です（止めるときは StopCoroutine を使ってください）",
                            "CancellationToken is not supported (use StopCoroutine to stop)"));
                        return null;
                    }

                switch (method.Name)
                {
                    case "Yield":
                    case "NextFrame":
                        return "yield return null;";
                    case "DelayFrame":
                    {
                        if (!args.TryGetValue("delayFrameCount", out ExpressionSyntax count))
                            return null;
                        // UniTask の DelayFrame(n) は n フレーム、DelayFrame(0) は Yield と同じ（1 フレーム）
                        string i = $"__af{temp++}";
                        return $"for (int {i} = UnityEngine.Mathf.Max(({Arg(count)}), 1); {i} > 0; {i}--) yield return null;";
                    }
                    case "Delay":
                        return DelayCode(inv, args);
                    case "WaitUntil":
                    case "WaitWhile":
                    {
                        if (!args.TryGetValue("predicate", out ExpressionSyntax predicate))
                            return null;
                        return $"yield return new UnityEngine.{method.Name}({Arg(predicate)});";
                    }
                    case "WhenAll":
                        return WhenAllCode(inv, method, ref temp, out value, out block);
                    case "WhenAny":
                        return WhenAnyCode(inv, method, ref temp, out value, out block);
                }
                Error(inv, Loc.T($"UniTask.{method.Name} は未対応です。Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...)・WhenAll(...)・WhenAny(...) が使えます",
                    $"UniTask.{method.Name} is not supported. Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...), WaitWhile(...), WhenAll(...) and WhenAny(...) can be used"));
                return null;
            }

            /// <summary>
            /// await UniTask.WhenAll(A(), B()) → 子を全部始めて、順に待つ。値を返すもの（同じ型 T）は、値を T[] にして返す（var r = await ...; ）。
            /// 先に終わったものを待つ動きは、コルーチンの「終わったものを待つ」と同じ（#174）
            /// </summary>
            private string WhenAllCode(InvocationExpressionSyntax inv, IMethodSymbol method, ref int temp, out string value, out bool block)
            {
                value = null;
                block = true;
                var starts = new StringBuilder();
                List<TaskElement> elements = ElementsOf(inv, "WhenAll", starts, ref temp);
                if (elements == null)
                    return null;
                bool generic = elements[0].Result != null;
                string elementType = generic ? TypeText(elements[0].Result) : null;
                if (elements.Any(t => (t.Result != null) != generic))
                {
                    Error(inv, Loc.T("UniTask.WhenAll に並べる async メソッド・変数は、すべて値を返さないか、すべて値を返してください",
                        "The async methods and variables in UniTask.WhenAll must all return no value, or all return a value"));
                    return null;
                }
                // 値を返す WhenAll は、引数が 2〜15 個なら、本物の UniTask ではタプル（T1, T2, ...）を返す。params の形（1 個）は配列を返す
                ITypeSymbol resultArgument = method.ReturnType is INamedTypeSymbol rt && rt.TypeArguments.Length == 1 ? rt.TypeArguments[0] : null;
                bool asArray = resultArgument is IArrayTypeSymbol;
                bool asTuple = resultArgument is INamedTypeSymbol nt && nt.IsTupleType;
                if (generic && !asArray && !asTuple)
                {
                    Error(inv, Loc.T("この UniTask.WhenAll の値の受け取り方は未対応です", "This way of taking the value of UniTask.WhenAll is not supported"));
                    return null;
                }

                var waits = new StringBuilder();
                var reads = new List<string>();
                foreach (TaskElement e in elements)
                {
                    waits.Append($"yield return {e.Handle}; ");
                    if (generic)
                        reads.Add($"({TypeText(e.Result)}){e.Holder}[0]");
                }
                if (generic)
                    value = asTuple ? $"({string.Join(", ", reads)})" : $"new {elementType}[] {{ {string.Join(", ", reads)} }}";
                return (starts.ToString() + waits.ToString()).TrimEnd();
            }

            /// <summary>WhenAll・WhenAny に並べた 1 つ（async の呼び出し、または UniTask の変数）</summary>
            private sealed class TaskElement
            {
                public string Handle;
                public string Holder;
                public ITypeSymbol Result;
            }

            /// <summary>
            /// WhenAll・WhenAny の引数を、1 つずつ見る。async の呼び出しは、starts に、置き場の宣言と開始の文を足す（変数に入れたものは、もう始まっている）。
            /// 書けない引数は、エラーにして null
            /// </summary>
            private List<TaskElement> ElementsOf(InvocationExpressionSyntax inv, string name, StringBuilder starts, ref int temp)
            {
                var list = new List<TaskElement>();
                foreach (ArgumentSyntax a in inv.ArgumentList.Arguments)
                {
                    ExpressionSyntax e = a.Expression;
                    TaskVar v = VarAt(e);
                    if (v != null)
                    {
                        Use(e);
                        list.Add(new TaskElement { Handle = v.Handle, Holder = v.Holder, Result = v.Result });
                        continue;
                    }
                    InvocationExpressionSyntax call = e as InvocationExpressionSyntax;
                    AsyncMethod target = call == null ? null : AsyncOf(call);
                    if (target == null)
                    {
                        Error(inv, Loc.T($"UniTask.{name} は、async メソッドの呼び出しか、async の呼び出しを入れた UniTask の変数を並べる形（{name}(A(), t)）だけで書けます（配列・List・引数に入れた UniTask は未対応です）",
                            $"UniTask.{name} can only list calls to async methods or UniTask variables that hold such a call ({name}(A(), t)). A UniTask in an array, list or parameter is not supported"));
                        return null;
                    }
                    string handle = $"__w{temp++}";
                    string holder = null;
                    Pad(starts, call);
                    if (target.ResultType != null)
                    {
                        holder = $"__ar{temp++}";
                        starts.Append($"object[] {holder} = new object[1]; ");
                    }
                    string code = CallCode(call, holder);
                    if (code == null)
                        return null;
                    starts.Append($"UnityEngine.Coroutine {handle} = StartCoroutine({code}); ");
                    list.Add(new TaskElement { Handle = handle, Holder = holder, Result = target.ResultType });
                }
                if (list.Count == 0)
                {
                    Error(inv, Loc.T($"UniTask.{name} には、async メソッドの呼び出しを 1 つ以上並べてください", $"List one or more calls to async methods in UniTask.{name}"));
                    return null;
                }
                return list;
            }

            /// <summary>
            /// await UniTask.WhenAny(A(), B()) → 子を全部始めて、どれかが終わるまで 1 フレームごとに見る（残りは動き続ける）。
            /// 値は、終わったものの番号（UniTask&lt;int&gt;）、値を返すものは (winArgumentIndex, result)
            /// </summary>
            private string WhenAnyCode(InvocationExpressionSyntax inv, IMethodSymbol method, ref int temp, out string value, out bool block)
            {
                value = null;
                block = true;
                var starts = new StringBuilder();
                List<TaskElement> elements = ElementsOf(inv, "WhenAny", starts, ref temp);
                if (elements == null)
                    return null;
                // 戻り値の形（本物の UniTask のオーバーロード）：UniTask<int>（値なし）・(int winArgumentIndex, T result)（params）・
                // (int winArgumentIndex, T1 result1, T2 result2, ...)（2〜15 個）・(bool hasResultLeft, T result)（UniTask<T> と UniTask の 2 個）
                var tuple = (method.ReturnType as INamedTypeSymbol)?.TypeArguments[0] as INamedTypeSymbol;
                bool generic = tuple != null && tuple.IsTupleType;
                bool left = generic && tuple.TupleElements[0].Type.SpecialType == SpecialType.System_Boolean;
                if (left ? elements.Count != 2 || elements[0].Result == null || elements[1].Result != null
                         : elements.Any(t => (t.Result != null) != generic))
                {
                    Error(inv, Loc.T("UniTask.WhenAny に並べる async メソッド・変数は、すべて値を返さないか、すべて値を返してください",
                        "The async methods and variables in UniTask.WhenAny must all return no value, or all return a value"));
                    return null;
                }
                string index = $"__wi{temp++}";
                string pick = "-1";
                for (int i = elements.Count - 1; i >= 0; i--)
                    pick = $"!__co_alive({elements[i].Handle}) ? {i} : {pick}";
                string wait = $"while ({string.Join(" && ", elements.Select(e => $"__co_alive({e.Handle})"))}) yield return null; int {index} = {pick};";
                if (!generic)
                    value = index;
                else if (left)
                    value = $"(hasResultLeft: {index} == 0, result: {index} == 0 ? ({TypeText(elements[0].Result)}){elements[0].Holder}[0] : default({TypeText(elements[0].Result)}))";
                else if (tuple.TupleElements.Length == 2)
                {
                    // params の形：終わったものの値
                    string chain = "null";
                    for (int i = elements.Count - 1; i >= 0; i--)
                        chain = $"{index} == {i} ? {elements[i].Holder}[0] : {chain}";
                    value = $"(winArgumentIndex: {index}, result: ({TypeText(elements[0].Result)})({chain}))";
                }
                else
                {
                    // 2〜15 個の形：それぞれの値（終わったもの以外は、既定の値）
                    var parts = new List<string> { $"winArgumentIndex: {index}" };
                    for (int i = 0; i < elements.Count; i++)
                    {
                        string type = TypeText(elements[i].Result);
                        parts.Add($"result{i + 1}: {index} == {i} ? ({type}){elements[i].Holder}[0] : default({type})");
                    }
                    value = $"({string.Join(", ", parts)})";
                }
                return (starts.ToString() + wait).TrimEnd();
            }

            // ---- UniTask を入れる変数・フィールド（#210） --------------------------------------
            // UniTask の変数は Coroutine（開始したコルーチンの番号）にする。UniTask<T> は、戻り値の置き場の object[] を、変数の名前 + __ar の変数に持つ

            private sealed class TaskVar
            {
                /// <summary>書いてある形（x ・ this.x）</summary>
                public string Handle;
                public string Holder;
                public ITypeSymbol Result;
            }

            private readonly HashSet<string> _taskNames = new HashSet<string>();
            private readonly HashSet<SimpleNameSyntax> _taskUses = new HashSet<SimpleNameSyntax>();

            private static bool IsTaskType(ITypeSymbol type)
            {
                if (type == null)
                    return false;
                string name = type.OriginalDefinition.ToDisplayString();
                return name == UniTaskName || name == UniTaskName + "<T>";
            }

            /// <summary>UniTask の名前を型に書いた（または var に async の呼び出しを入れた）変数・フィールドの名前を集める（以後の問い合わせは、この名前だけ）</summary>
            private void CollectTaskNames()
            {
                foreach (SyntaxNode n in OwnNodes())
                {
                    if (!(n is VariableDeclarationSyntax d))
                        continue;
                    string text = d.Type.ToString();
                    bool named = text.IndexOf("UniTask", System.StringComparison.Ordinal) >= 0 && text.IndexOf("UniTaskVoid", System.StringComparison.Ordinal) < 0;
                    bool var = text == "var";
                    if (!named && !var)
                        continue;
                    foreach (VariableDeclaratorSyntax v in d.Variables)
                    {
                        ExpressionSyntax init = v.Initializer?.Value;
                        while (init is ParenthesizedExpressionSyntax p)
                            init = p.Expression;
                        if (named || init is InvocationExpressionSyntax call && AsyncOf(call) != null)
                            _taskNames.Add(v.Identifier.ValueText);
                    }
                }
            }

            private static SimpleNameSyntax NameOf(ExpressionSyntax e)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                if (e is IdentifierNameSyntax id)
                    return id;
                if (e is MemberAccessExpressionSyntax access && access.Expression is ThisExpressionSyntax)
                    return access.Name as IdentifierNameSyntax;
                return null;
            }

            /// <summary>e が、UniTask の変数・フィールド（x ・ this.x）なら、その情報</summary>
            private TaskVar VarAt(ExpressionSyntax e)
            {
                SimpleNameSyntax name = NameOf(e);
                if (name == null || !_taskNames.Contains(name.Identifier.ValueText))
                    return null;
                ISymbol symbol = _model.GetSymbolInfo(name).Symbol;
                ITypeSymbol type = symbol is ILocalSymbol l ? l.Type : symbol is IFieldSymbol f ? f.Type : null;
                if (!IsTaskType(type))
                    return null;
                string text = name.Identifier.ValueText;
                string prefix = name.Parent is MemberAccessExpressionSyntax ? "this." : "";
                return new TaskVar
                {
                    Handle = prefix + text,
                    Holder = prefix + text + "__ar",
                    Result = type is INamedTypeSymbol nt && nt.TypeArguments.Length == 1 ? nt.TypeArguments[0] : null,
                };
            }

            /// <summary>書き換えた使い方として記録する（それ以外の使い方は、あとでエラーにする）</summary>
            private void Use(ExpressionSyntax e) => _taskUses.Add(NameOf(e));

            private bool IsTaskDeclaration(LocalDeclarationStatementSyntax ld)
            {
                VariableDeclarationSyntax d = ld.Declaration;
                string text = d.Type.ToString();
                if (text != "var" && (text.IndexOf("UniTask", System.StringComparison.Ordinal) < 0 || text.IndexOf("UniTaskVoid", System.StringComparison.Ordinal) >= 0))
                    return false;
                ExpressionSyntax init = d.Variables[0].Initializer?.Value;
                if (init is AwaitExpressionSyntax)
                    return false;
                if (text == "var" && !(init is InvocationExpressionSyntax call && AsyncOf(call) != null))
                    return false;
                return IsTaskType(_model.GetTypeInfo(d.Type).Type);
            }

            private bool IsTaskStatement(ExpressionStatementSyntax es)
            {
                if (es.Expression is AssignmentExpressionSyntax asg && asg.IsKind(SyntaxKind.SimpleAssignmentExpression) && !(asg.Right is AwaitExpressionSyntax))
                    return VarAt(asg.Left) != null;
                return es.Expression is InvocationExpressionSyntax inv && inv.ArgumentList.Arguments.Count == 0
                    && inv.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Forget" && VarAt(access.Expression) != null;
            }

            /// <summary>フィールド UniTask t; → Coroutine t;（UniTask&lt;T&gt; は、置き場のフィールドも足す）</summary>
            private void TaskField(FieldDeclarationSyntax f)
            {
                string text = f.Declaration.Type.ToString();
                if (text.IndexOf("UniTask", System.StringComparison.Ordinal) < 0 || text.IndexOf("UniTaskVoid", System.StringComparison.Ordinal) >= 0)
                    return;
                ITypeSymbol type = _model.GetTypeInfo(f.Declaration.Type).Type;
                if (!IsTaskType(type))
                    return;
                if (f.Declaration.Variables.Count != 1 || f.Declaration.Variables[0].Initializer != null)
                {
                    Error(f, Loc.T("UniTask のフィールドは、1 つずつ、初期値なしで宣言してください（値は、async の呼び出しを代入します）",
                        "Declare a UniTask field one at a time without an initial value (assign a call to an async method to it)"));
                    return;
                }
                _edits.Add(f.Declaration.Type.Span, "UnityEngine.Coroutine");
                if (type is INamedTypeSymbol nt && nt.TypeArguments.Length == 1)
                    _edits.Add(new TextSpan(f.Span.End, 0), $" private {(f.Modifiers.Any(SyntaxKind.StaticKeyword) ? "static " : "")}object[] {f.Declaration.Variables[0].Identifier.ValueText}__ar;");
            }

            /// <summary>UniTask t = Foo(); ・ UniTask t; ・ t = Foo(); ・ t.Forget();</summary>
            private void TaskStatement(SyntaxNode statement)
            {
                string original = statement.ToString();
                if (statement is LocalDeclarationStatementSyntax ld)
                {
                    VariableDeclarationSyntax d = ld.Declaration;
                    if (d.Variables.Count != 1)
                    {
                        Error(ld, Loc.T("UniTask の変数は、1 つずつ宣言してください", "Declare a UniTask variable one at a time"));
                        return;
                    }
                    string name = d.Variables[0].Identifier.ValueText;
                    bool generic = _model.GetTypeInfo(d.Type).Type is INamedTypeSymbol nt && nt.TypeArguments.Length == 1;
                    ExpressionSyntax init = d.Variables[0].Initializer?.Value;
                    while (init is ParenthesizedExpressionSyntax p)
                        init = p.Expression;
                    string code;
                    if (init == null)
                        code = $"UnityEngine.Coroutine {name};" + (generic ? $" object[] {name}__ar;" : "");
                    else if (init is InvocationExpressionSyntax call && AsyncOf(call) != null)
                    {
                        string started = CallCode(call, generic ? name + "__ar" : null);
                        if (started == null)
                            return;
                        code = (generic ? $"object[] {name}__ar = new object[1]; " : "") + $"UnityEngine.Coroutine {name} = StartCoroutine({started});";
                    }
                    else
                    {
                        Error(init, Loc.T("UniTask の変数に入れられるのは、async メソッドの呼び出しだけです", "Only a call to an async method can be stored in a UniTask variable"));
                        return;
                    }
                    _edits.Add(ld.Span, KeepLines(code, original));
                    return;
                }
                var es = (ExpressionStatementSyntax)statement;
                if (es.Expression is AssignmentExpressionSyntax asg)
                {
                    TaskVar v = VarAt(asg.Left);
                    Use(asg.Left);
                    ExpressionSyntax right = asg.Right;
                    while (right is ParenthesizedExpressionSyntax p)
                        right = p.Expression;
                    if (!(right is InvocationExpressionSyntax rc) || AsyncOf(rc) == null)
                    {
                        Error(asg.Right, Loc.T("UniTask の変数に入れられるのは、async メソッドの呼び出しだけです", "Only a call to an async method can be stored in a UniTask variable"));
                        return;
                    }
                    string started = CallCode(rc, v.Result != null ? v.Holder : null);
                    if (started == null)
                        return;
                    string assign = $"{v.Handle} = StartCoroutine({started});";
                    _edits.Add(es.Span, KeepLines(v.Result != null ? $"{{ {v.Holder} = new object[1]; {assign} }}" : assign, original));
                    return;
                }
                // t.Forget(); → もう始まっているので、何もしない
                var forget = (InvocationExpressionSyntax)es.Expression;
                Use(((MemberAccessExpressionSyntax)forget.Expression).Expression);
                _edits.Add(es.Span, KeepLines(";", original));
            }

            /// <summary>await・WhenAll・WhenAny・代入・Forget 以外の使い方（引数に渡す・戻す・比べるなど）は、エラー</summary>
            private void CheckTaskUses()
            {
                var reported = new HashSet<int>();
                foreach (IdentifierNameSyntax id in OwnNodes().OfType<IdentifierNameSyntax>())
                {
                    if (!_taskNames.Contains(id.Identifier.ValueText) || _taskUses.Contains(id))
                        continue;
                    ISymbol symbol = _model.GetSymbolInfo(id).Symbol;
                    if (!IsTaskType(symbol is ILocalSymbol l ? l.Type : symbol is IFieldSymbol f ? f.Type : null))
                        continue;
                    if (reported.Add(id.GetLocation().GetLineSpan().StartLinePosition.Line))
                        Error(id, Loc.T("UniTask の変数は、await・WhenAll・WhenAny への指定・async の呼び出しの代入・Forget() だけで使えます（引数に渡す・戻す・比べるなどは未対応です）",
                            "A UniTask variable can only be used with await, as an argument of WhenAll / WhenAny, to store a call to an async method, and with Forget(). Passing it as an argument, returning it or comparing it is not supported"));
                }
            }

            /// <summary>UniTask.Delay(ms) / Delay(TimeSpan) → WaitForSeconds / WaitForSecondsRealtime</summary>
            private string DelayCode(InvocationExpressionSyntax inv, Dictionary<string, ExpressionSyntax> args)
            {
                string seconds;
                if (args.TryGetValue("millisecondsDelay", out ExpressionSyntax ms))
                    seconds = $"({Arg(ms)}) / 1000f";
                else if (args.TryGetValue("delayTimeSpan", out ExpressionSyntax span))
                {
                    seconds = SecondsOf(span);
                    if (seconds == null)
                        return null;
                }
                else
                    return null;

                // 時間の種類：ignoreTimeScale（true なら timeScale に左右されない）・DelayType（DeltaTime は timeScale あり、UnscaledDeltaTime・Realtime はなし）
                bool realtime = false;
                if (args.TryGetValue("ignoreTimeScale", out ExpressionSyntax ignore))
                {
                    Optional<object> value = _model.GetConstantValue(ignore);
                    if (!value.HasValue || !(value.Value is bool b))
                    {
                        Error(ignore, Loc.T("ignoreTimeScale は true か false で書いてください", "Write ignoreTimeScale as true or false"));
                        return null;
                    }
                    realtime = b;
                }
                if (args.TryGetValue("delayType", out ExpressionSyntax type))
                {
                    string field = (_model.GetSymbolInfo(type).Symbol as IFieldSymbol)?.Name;
                    if (field == null)
                    {
                        Error(type, Loc.T("DelayType は DelayType.DeltaTime のように書いてください", "Write DelayType as DelayType.DeltaTime"));
                        return null;
                    }
                    realtime = field != "DeltaTime";
                }
                return realtime
                    ? $"yield return new UnityEngine.WaitForSecondsRealtime({seconds});"
                    : $"yield return new UnityEngine.WaitForSeconds({seconds});";
            }

            /// <summary>TimeSpan.FromSeconds(x) ・ FromMilliseconds(x) ・ FromMinutes(x) の、秒の式（TimeSpan は Udon にないので、この形だけ）</summary>
            private string SecondsOf(ExpressionSyntax span)
            {
                if (span is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax access
                    && _model.GetSymbolInfo(call).Symbol is IMethodSymbol m && m.ContainingType.ToDisplayString() == "System.TimeSpan"
                    && call.ArgumentList.Arguments.Count == 1)
                {
                    string value = Arg(call.ArgumentList.Arguments[0].Expression);
                    switch (access.Name.Identifier.ValueText)
                    {
                        case "FromSeconds": return $"(float)({value})";
                        case "FromMilliseconds": return $"({value}) / 1000f";
                        case "FromMinutes": return $"({value}) * 60f";
                    }
                }
                Error(span, Loc.T("UniTask.Delay の TimeSpan は、TimeSpan.FromSeconds(x)・FromMilliseconds(x)・FromMinutes(x) の形で書いてください（TimeSpan は Udon にありません）",
                    "Write the TimeSpan of UniTask.Delay as TimeSpan.FromSeconds(x), FromMilliseconds(x) or FromMinutes(x) (TimeSpan does not exist in Udon)"));
                return null;
            }

            /// <summary>default・default(CancellationToken)・CancellationToken.None（止められないトークン）か</summary>
            private bool IsNoToken(ExpressionSyntax e)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                if (e is DefaultExpressionSyntax || e.IsKind(SyntaxKind.DefaultLiteralExpression))
                    return true;
                return e is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "None"
                       && _model.GetTypeInfo(e).Type?.ToDisplayString() == CancellationTokenName;
            }

            /// <summary>呼び出しの引数を、引数の名前 → 式にする（位置・名前付きの両方）</summary>
            private static Dictionary<string, ExpressionSyntax> ArgumentsOf(InvocationExpressionSyntax inv, IMethodSymbol method)
            {
                var result = new Dictionary<string, ExpressionSyntax>();
                for (int i = 0; i < inv.ArgumentList.Arguments.Count; i++)
                {
                    ArgumentSyntax arg = inv.ArgumentList.Arguments[i];
                    string name = arg.NameColon?.Name.Identifier.ValueText ?? (i < method.Parameters.Length ? method.Parameters[i].Name : null);
                    if (name != null)
                        result[name] = arg.Expression;
                }
                return result;
            }

            // ---- メソッドの宣言 ----------------------------------------------------

            private void Declaration(MethodDeclarationSyntax m, AsyncMethod info)
            {
                SyntaxToken first = m.Modifiers.Count > 0 ? m.Modifiers[0] : m.ReturnType.GetFirstToken();
                string modifiers = string.Join(" ", m.Modifiers.Where(x => !x.IsKind(SyntaxKind.AsyncKeyword)).Select(x => x.ValueText));
                string original = _cls.SyntaxTree.GetText().ToString(TextSpan.FromBounds(first.SpanStart, m.ReturnType.Span.End));
                string header;
                if (info.Wrapper)
                {
                    // Udon がイベント名で呼ぶので、同じ名前の void のメソッドで、コルーチンを始める
                    string parameters = string.Join(", ", m.ParameterList.Parameters.Select(p =>
                        (p.Modifiers.Count > 0 ? string.Join(" ", p.Modifiers.Select(x => x.ValueText)) + " " : "") + p.Type + " " + p.Identifier.ValueText
                        + (p.Default != null ? " " + p.Default.ToString() : "")));
                    string args = string.Join(", ", m.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
                    header = $"{modifiers} void {m.Identifier.ValueText}({parameters}) {{ StartCoroutine({info.CoroutineName}({args})); }} private System.Collections.IEnumerator";
                    _edits.Add(m.Identifier.Span, info.CoroutineName);
                }
                else
                    header = $"{modifiers} System.Collections.IEnumerator";
                _edits.Add(TextSpan.FromBounds(first.SpanStart, m.ReturnType.Span.End), KeepLines(header.Trim(), original));
                // 値を返すメソッド（UniTask<T>）：戻り値は、呼ぶ側が作る object[1] に入れる（最後の引数。呼ぶ側が名前付きで渡す）
                if (info.ResultType != null)
                    _edits.Add(new TextSpan(m.ParameterList.CloseParenToken.SpanStart, 0), (m.ParameterList.Parameters.Count > 0 ? ", " : "") + "object[] __ar");

                // 最後まで行ったときに終わるよう、本体の最後に yield break; を足す（await のない async メソッドも、コルーチンとして書けるように）
                _edits.Add(new TextSpan(m.Body.CloseBraceToken.SpanStart, 0), "yield break; ");
            }

            private static string KeepLines(string code, string original) =>
                code + new string('\n', System.Math.Max(0, original.Count(c => c == '\n') - code.Count(c => c == '\n')));
        }
    }
}
