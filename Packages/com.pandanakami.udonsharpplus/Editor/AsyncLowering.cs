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
    /// 最初の版は、値を返さない UniTask・UniTaskVoid・async void だけ。UniTask&lt;T&gt;・WhenAll・WhenAny・CancellationToken・変数に入れた UniTask は、エラーにして案内する
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
                        };
                    }
                }
            }
            if (methods.Count == 0)
                return trees;

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
            if (kind == "generic")
                return Loc.T("値を返す UniTask<T> は未対応です（値を返さない UniTask か UniTaskVoid で書き、結果はフィールドに入れてください）",
                    "UniTask<T> (returning a value) is not supported yet. Use UniTask or UniTaskVoid and keep the result in a field");
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
                        "UniTask は、async メソッドの戻り値と、await の中（UniTask.Delay など）だけで使えます。UniTask の値を変数・フィールド・引数・戻り値に入れる書き方は未対応です",
                        "UniTask can only be used as the return type of an async method and inside await (UniTask.Delay, ...). Keeping a UniTask in a variable, field, parameter or return value is not supported"));
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

            public void Run(ref int temp)
            {
                var handled = new HashSet<SyntaxNode>();

                // 1) async メソッドの中の await（文）と return;。外側の文のほうが先に出ないよう、うしろから（中のものを先に）書き換える
                var asyncBodies = _cls.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => _model.GetDeclaredSymbol(m) is IMethodSymbol s && _methods.ContainsKey(s.OriginalDefinition)).ToList();
                var candidates = new List<SyntaxNode>();
                foreach (MethodDeclarationSyntax m in asyncBodies)
                {
                    candidates.AddRange(OwnBody(m).Where(n => n is ExpressionStatementSyntax es && es.Expression is AwaitExpressionSyntax));
                    candidates.AddRange(OwnBody(m).Where(n => n is ReturnStatementSyntax r && r.Expression == null));
                }
                // 2) 待たずに呼ぶ（Foo(); ・ Foo().Forget();）。どのメソッドの中でも
                foreach (SyntaxNode n in OwnNodes().OfType<ExpressionStatementSyntax>())
                    if (CallStatementTarget(((ExpressionStatementSyntax)n).Expression) != null)
                        candidates.Add(n);
                foreach (SyntaxNode node in candidates.Distinct().OrderByDescending(n => n.SpanStart))
                {
                    handled.Add(node);
                    if (node is ReturnStatementSyntax ret)
                        _edits.Add(ret.Span, KeepLines("yield break;", ret.ToString()));
                    else if (node is ExpressionStatementSyntax stmt && stmt.Expression is AwaitExpressionSyntax aw)
                        AwaitStatement(stmt, aw, ref temp);
                    else if (node is ExpressionStatementSyntax call)
                        StartStatement(call);
                }

                // 3) 書き換えていない await・async メソッドの呼び出し（値を使う形・式の途中・ほかのビヘイビア）はエラー
                foreach (MethodDeclarationSyntax m in asyncBodies)
                    foreach (AwaitExpressionSyntax aw in OwnBody(m).OfType<AwaitExpressionSyntax>())
                        if (!handled.Contains(aw.Parent))
                            Error(aw, Loc.T("await は、文（await UniTask.Delay(1000); など）として書いてください。await の値を使う書き方は未対応です",
                                "Write await as a statement (await UniTask.Delay(1000);). Using the value of an await is not supported"));
                foreach (InvocationExpressionSyntax call in OwnNodes().OfType<InvocationExpressionSyntax>())
                {
                    AsyncMethod target = AsyncOf(call);
                    if (target == null || IsHandledCall(call, handled))
                        continue;
                    Error(call, Loc.T(
                        $"async メソッド {target.Symbol.Name} は、Foo(); ・ Foo().Forget(); ・ async メソッドの中の await Foo(); の形だけで呼べます（値として使う書き方は未対応です）",
                        $"The async method {target.Symbol.Name} can only be called as Foo();, Foo().Forget(); or await Foo(); inside an async method. Using it as a value is not supported"));
                }

                // 4) メソッドの宣言
                foreach (MethodDeclarationSyntax m in asyncBodies)
                    Declaration(m, _methods[((IMethodSymbol)_model.GetDeclaredSymbol(m)).OriginalDefinition]);
            }

            /// <summary>cls のノード（入れ子の型は含めない）</summary>
            private IEnumerable<SyntaxNode> OwnNodes() => CoroutineLowering.OwnNodes(_cls);

            /// <summary>メソッドの本体のノード（ラムダ・ローカル関数の中は含めない）</summary>
            private static IEnumerable<SyntaxNode> OwnBody(MethodDeclarationSyntax m) =>
                m.Body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax));

            /// <summary>式が、待たずに呼ぶ形（Foo() ・ Foo().Forget()）なら、呼ぶ async メソッド（なければ null）</summary>
            private AsyncMethod CallStatementTarget(ExpressionSyntax expression)
            {
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

            /// <summary>書き換えた文（待たずに呼ぶ・await）の中の呼び出しか</summary>
            private bool IsHandledCall(InvocationExpressionSyntax call, HashSet<SyntaxNode> handled)
            {
                for (SyntaxNode n = call; n != null; n = n.Parent)
                {
                    if (handled.Contains(n))
                    {
                        // 文の「いちばん外側の呼び出し」だけが書き換えたもの。文の中の別の呼び出し（引数の中など）は、書き換えていない
                        ExpressionSyntax top = n is ExpressionStatementSyntax es ? (es.Expression is AwaitExpressionSyntax a ? a.Expression : es.Expression) : null;
                        while (top is ParenthesizedExpressionSyntax p)
                            top = p.Expression;
                        if (top == call)
                            return true;
                        if (top is InvocationExpressionSyntax outer && outer.Expression is MemberAccessExpressionSyntax access
                            && access.Name.Identifier.ValueText == "Forget" && access.Expression == call)
                            return true;
                        return false;
                    }
                    if (n is MethodDeclarationSyntax)
                        break;
                }
                return false;
            }

            // ---- 文の書き換え ----------------------------------------------------

            /// <summary>Foo(); ・ Foo().Forget(); → StartCoroutine(Foo());</summary>
            private void StartStatement(ExpressionStatementSyntax stmt)
            {
                var inv = (InvocationExpressionSyntax)stmt.Expression;
                if (AsyncOf(inv) == null)
                    inv = (InvocationExpressionSyntax)((MemberAccessExpressionSyntax)inv.Expression).Expression;
                string call = CallCode(inv);
                if (call == null)
                    return;
                string original = stmt.ToString();
                _edits.Add(stmt.Span, KeepLines($"StartCoroutine({call});", original));
            }

            /// <summary>async メソッドの呼び出しの文字（イベントのものはコルーチンの名前に変える）。ほかのビヘイビアの呼び出しは null（エラー）</summary>
            private string CallCode(InvocationExpressionSyntax call)
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
                if (target.Wrapper)
                    _edits.Add(name.Identifier.Span, target.CoroutineName);
                return _edits.Take(call);
            }

            /// <summary>await ...; → yield return ...;</summary>
            private void AwaitStatement(ExpressionStatementSyntax stmt, AwaitExpressionSyntax aw, ref int temp)
            {
                // 待ち方のあるところでは、コルーチンの状態機械が書けない文の中の await は、コルーチンの段のエラーより先に、await の言葉で案内する
                for (SyntaxNode n = stmt.Parent; n != null && !(n is MethodDeclarationSyntax); n = n.Parent)
                    if (n is TryStatementSyntax || n is UsingStatementSyntax || n is LockStatementSyntax)
                    {
                        Error(aw, Loc.T("try・catch・finally・using・lock の中の await は未対応です（コルーチンと同じ）",
                            "await inside try, catch, finally, using or lock is not supported (same as coroutines)"));
                        return;
                    }

                ExpressionSyntax operand = aw.Expression;
                while (operand is ParenthesizedExpressionSyntax p)
                    operand = p.Expression;
                string code = operand is InvocationExpressionSyntax inv ? AwaitCode(inv, ref temp) : null;
                if (code == null)
                {
                    if (!(operand is InvocationExpressionSyntax))
                        Error(operand, Loc.T("この await は未対応です。UniTask.Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...)、または async メソッドの呼び出しを await してください",
                            "This await is not supported. Await UniTask.Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...), WaitWhile(...) or a call to an async method"));
                    return;
                }
                _edits.Add(stmt.Span, KeepLines(code, stmt.ToString()));
            }

            /// <summary>await の式の、書き換え先の文（書けないものはエラーにして null）</summary>
            private string AwaitCode(InvocationExpressionSyntax inv, ref int temp)
            {
                AsyncMethod target = AsyncOf(inv);
                if (target != null)
                {
                    string call = CallCode(inv);
                    return call == null ? null : $"yield return {call};";
                }
                if (!(_model.GetSymbolInfo(inv).Symbol is IMethodSymbol method) || !method.IsStatic || method.ContainingType.ToDisplayString() != UniTaskName)
                {
                    Error(inv, Loc.T("この await は未対応です。UniTask.Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...)、または async メソッドの呼び出しを await してください",
                        "This await is not supported. Await UniTask.Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...), WaitWhile(...) or a call to an async method"));
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
                        return $"for (int {i} = UnityEngine.Mathf.Max(({_edits.Take(count)}), 1); {i} > 0; {i}--) yield return null;";
                    }
                    case "Delay":
                        return DelayCode(inv, args);
                    case "WaitUntil":
                    case "WaitWhile":
                    {
                        if (!args.TryGetValue("predicate", out ExpressionSyntax predicate))
                            return null;
                        return $"yield return new UnityEngine.{method.Name}({_edits.Take(predicate)});";
                    }
                }
                Error(inv, Loc.T($"UniTask.{method.Name} は未対応です。Yield()・NextFrame()・Delay(...)・DelayFrame(n)・WaitUntil(...)・WaitWhile(...) が使えます",
                    $"UniTask.{method.Name} is not supported. Yield(), NextFrame(), Delay(...), DelayFrame(n), WaitUntil(...) and WaitWhile(...) can be used"));
                return null;
            }

            /// <summary>UniTask.Delay(ms) / Delay(TimeSpan) → WaitForSeconds / WaitForSecondsRealtime</summary>
            private string DelayCode(InvocationExpressionSyntax inv, Dictionary<string, ExpressionSyntax> args)
            {
                string seconds;
                if (args.TryGetValue("millisecondsDelay", out ExpressionSyntax ms))
                    seconds = $"({_edits.Take(ms)}) / 1000f";
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
                    string value = _edits.Take(call.ArgumentList.Arguments[0].Expression);
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

                // 最後まで行ったときに終わるよう、本体の最後に yield break; を足す（await のない async メソッドも、コルーチンとして書けるように）
                _edits.Add(new TextSpan(m.Body.CloseBraceToken.SpanStart, 0), "yield break; ");
            }

            private static string KeepLines(string code, string original) =>
                code + new string('\n', System.Math.Max(0, original.Count(c => c == '\n') - code.Count(c => c == '\n')));
        }
    }
}
