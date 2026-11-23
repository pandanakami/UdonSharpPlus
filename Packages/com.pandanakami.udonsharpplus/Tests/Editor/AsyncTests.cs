using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// UniTask の async / await（#207・#209）→ コルーチン。UniTask は、VRChat SDK に同梱されている本物（Cysharp.Threading.Tasks）を使う。
    /// 動きは FeatureTests のパネル（AsyncRunner）で、コルーチンと同じ順番・フレームになることを見る
    /// </summary>
    public class AsyncTests
    {
        private const string Head = "using System;\nusing System.Threading;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\n";

        // AsyUse.cs の 1〜5 行目は using、6 行目はクラス、7 行目は {。本体は 8 行目から
        private const int BodyLine = 8;

        private static string Source(string body) => Head + "public class AsyUse : UdonSharpBehaviour\n{\n" + body + "\n}";

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("AsyUse.cs", Source(body)));

        private static CompileResult CompileOk(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
            return r;
        }

        private static void AssertError(string body, string message, int line)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains($"AsyUse.cs({line},")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        private static string Lowered(string name, string body)
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusAsync_" + name));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("AsyUse.cs", Source(body)));
                Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
                return File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "AsyUse.cs"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ---- 動くもの ----------------------------------------------------

        [Test]
        public void AwaitsBecomeCoroutineWaits()
        {
            string code = Lowered("Waits", @"public int n;
void Start() { Run().Forget(); }
async UniTaskVoid Run()
{
    await UniTask.Yield();
    await UniTask.NextFrame();
    await UniTask.Delay(500);
    await UniTask.Delay(TimeSpan.FromSeconds(1.5));
    await UniTask.Delay(250, ignoreTimeScale: true);
    await UniTask.Delay(100, DelayType.Realtime);
    await UniTask.DelayFrame(3);
    await UniTask.WaitUntil(() => n > 2);
    await UniTask.WaitWhile(() => n > 5);
}");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(code, @"(?<![A-Za-z_])await\b"), Is.False, "await は残らない（__co_await は実行時のメソッド）");
            Assert.That(code, Does.Not.Contain("UniTask.").And.Not.Contain("async"));
            Assert.That(code, Does.Contain("__co_Run_0"), "コルーチンの状態機械になる");
            Assert.That(code, Does.Contain("(500) / 1000f"), "Delay の ms は秒にする");
            Assert.That(code, Does.Contain("(float)(1.5)"), "TimeSpan.FromSeconds");
            Assert.That(code, Does.Contain("UnityEngine.Mathf.Max((3), 1)"), "DelayFrame は n フレーム（0 は 1 フレーム）");
            Assert.That(code, Does.Contain("realtimeSinceStartup"), "ignoreTimeScale・Realtime は WaitForSecondsRealtime");
        }

        [Test]
        public void ReturnBecomesYieldBreakAndBodyEndsWithIt()
        {
            string code = Lowered("Return", @"public int n;
void Start() { Go(); }
async UniTask Go()
{
    if (n > 0) return;
    n = 1;
}");
            Assert.That(code, Does.Not.Contain("async"));
            Assert.That(code, Does.Contain("__co_Go_0"));
        }

        [Test]
        public void CallsStartTheCoroutine()
        {
            string code = Lowered("Calls", @"public int n;
void Start() { A(); B().Forget(); this.A(); }
async UniTask A() { await UniTask.Yield(); n++; }
async UniTaskVoid B() { await UniTask.Yield(); n++; }");
            Assert.That(code, Does.Not.Contain("async").And.Not.Contain("Forget"));
            Assert.That(code, Does.Contain("__co_go(A())").And.Contain("__co_go(this.A())"), "待たずに呼ぶと、コルーチンを始める");
            Assert.That(code, Does.Contain("__co_go(B())"));
        }

        [Test]
        public void AwaitingAnAsyncMethodWaitsForTheChild()
        {
            string code = Lowered("Child", @"public int n;
void Start() { Parent(); }
async UniTask Parent() { await Child(); n++; await Child(); }
async UniTask Child() { await UniTask.Yield(); }");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_await\(__s, Child\(\)\)").Count, Is.EqualTo(2), "async の中の await Child() は、子のコルーチンを待つ");
        }

        [Test]
        public void EventMethodsKeepTheirNameAndStartTheCoroutine()
        {
            string code = Lowered("Events", @"public int n;
async UniTaskVoid Start() { await UniTask.Yield(); n++; }
public override async void Interact() { await UniTask.Delay(100); n++; }
public override async void OnPlayerJoined(VRC.SDKBase.VRCPlayerApi player) { await UniTask.Yield(); n++; }");
            Assert.That(code, Does.Contain("void Start()"));
            Assert.That(code, Does.Contain("public override void Interact()"));
            Assert.That(code, Does.Contain("public override void OnPlayerJoined(VRC.SDKBase.VRCPlayerApi player)"));
            Assert.That(code, Does.Contain("__async_AsyUse_Start").And.Contain("__async_AsyUse_Interact").And.Contain("__async_AsyUse_OnPlayerJoined"));
            Assert.That(code, Does.Not.Contain("async void").And.Not.Contain("UniTaskVoid Start"));
        }

        [Test]
        public void LinesAreKept()
        {
            string code = Lowered("Lines", @"public int n;
void Start() { A(); }
async UniTask A()
{
    await UniTask.Yield();
    n++;
    await UniTask.Delay(1000);
    n++;
}");
            string[] before = Source(@"public int n;
void Start() { A(); }
async UniTask A()
{
    await UniTask.Yield();
    n++;
    await UniTask.Delay(1000);
    n++;
}").Split('\n');
            // 行番号が変わらない：n++ の行が元と同じ行にある
            string[] after = code.Split('\n');
            Assert.That(after.Length, Is.GreaterThanOrEqualTo(before.Length));
            Assert.That(after[before.ToList().FindIndex(l => l.Contains("n++")) ], Does.Contain("n++"));
        }

        // ---- UniTask を使わないプロジェクト ----------------------------------------------------

        [Test]
        public void ProjectsWithoutUniTaskAreNotTouched()
        {
            // UniTask も async もない。この段は動かない
            var plain = new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("using UdonSharp; public class P : UdonSharpBehaviour { void Start() { } }") };
            Assert.That(AsyncLowering.MayHaveAsync(plain), Is.False);
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("P.cs", "using UdonSharp; public class P : UdonSharpBehaviour { public int n; void Start() { n++; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void AsyncOutsideUdonSharpBehaviourIsLeftAlone()
        {
            // UniTask の型がなくても、async の文字があるだけで、書き換えもエラーも出さない（普通の Unity の async は触らない）
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Mono.cs", "using System.Threading.Tasks; using UnityEngine; public class Mono : MonoBehaviour { async Task Foo() { await Task.Delay(1); } }"),
                new TestSource("P.cs", "using UdonSharp; public class P : UdonSharpBehaviour { public int n; void Start() { n++; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void PlainClassesWithoutUniTaskAreNotReportedInAProjectThatUsesUniTask()
        {
            // 別のビヘイビアが async を使っていても、UniTask と関係ない普通のクラス（void・値を返すメソッド）は、エラーにならない
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("AsyUse.cs", Source("async UniTask A() { await UniTask.Yield(); }\nvoid Start() { A(); var p = new Plain(); p.Hit(); }")),
                new TestSource("Plain.cs", "public class Plain { public int hp = 3; public void Hit() { hp--; } public int Get() { return hp; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void UniTaskInAnotherClassOfTheSameFileIsLeftAlone()
        {
            // UdonSharpBehaviour でないクラスの UniTask は、普通の Unity のもの（残った UniTask のエラーは、UdonSharpBehaviour の中だけ）
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Mix.cs",
                Head + "public class Helper : MonoBehaviour { UniTask _t; public UniTask Foo() => UniTask.Delay(1); }\n"
                + "public class Mix : UdonSharpBehaviour { public int n; void Start() { n++; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        // ---- 対応しない書き方 ----------------------------------------------------

        [Test]
        public void LocalsThatCarryOverAreNullAndTheOthersAreDefault()
        {
            // yield をまたいで持ち越す配列（戻り値の置き場）は、再開のときにキャスト（object の箱）で戻すので、null（object の箱）で初期化する。
            // 持ち越さない配列（タプルを受ける一時変数）は、コンストラクタの結果（object[] の箱）が入るので、default（object[] の箱）のまま。
            // 箱の型が合わないと、Udon の変数コピーで、実行のたびに確保する（#217・#220）
            string code = Lowered("Carry", @"void Start() { Run(); }
async UniTask Run() { var (a, b) = await UniTask.WhenAll(Add(1, 2), Add(3, 4)); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(code, @"object\[\] __ar\d+ = null;"), Is.True, "持ち越す置き場は、null で初期化する");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(code, @"object\[\] __td\d+ = default\(object\[\]\);"), Is.True, "持ち越さないタプルの一時変数は、default のまま");
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(code, @"object\[\] __td\d+ = null;"), Is.False);
        }

        [Test]
        public void ValueReturningAsyncUsesAHolder()
        {
            string code = Lowered("Value", @"public int n;
public string s;
void Start() { Run(); }
async UniTask Run() { var a = await Add(1, 2); int b = await Add(a, 3); n = await Add(b, 4); s = await Name(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }
async UniTask<string> Name() { return ""a""; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("async"));
            Assert.That(code, Does.Contain("object[] __ar").And.Contain("__co_h()"), "戻り値の置き場は、呼ぶ側が作る");
            Assert.That(code, Does.Contain("__ar: __ar"), "最後の引数に渡す");
            Assert.That(code, Does.Contain("(int)__ar").And.Contain("(string)__ar"), "値は型を付けて読み出す");
            Assert.That(code, Does.Contain("__ar[0] = (object)(x + y)"), "return は置き場に入れる");
        }

        [Test]
        public void ReturnAwaitPassesTheValueOn()
        {
            string code = Lowered("ReturnAwait", @"public long n;
void Start() { Wide(); }
async UniTask<long> Wide() { return await Add(1, 2); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(code, Does.Contain("(object)((long)"), "型が違うときは、変換して返す");
        }

        [Test]
        public void ValueReturningAsyncStartedWithoutAwait()
        {
            string code = Lowered("ValueFire", @"void Start() { Add(1, 2); Add(3, 4).Forget(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_go\(Add\(\d, \d, __ar: __co_h\(\)\)\)").Count, Is.EqualTo(2),
                "待たずに呼ぶときは、使わない置き場を渡して始める");
        }

        [Test]
        public void WhenAllOfDifferentTypesIsTakenAsATuple()
        {
            string code = Lowered("WhenAllTuple", @"public int n;
public string s;
void Start() { Run(); }
async UniTask Run() { var (a, b) = await UniTask.WhenAll(Add(), Name()); n = a; s = b; }
async UniTask<int> Add() { await UniTask.Yield(); return 1; }
async UniTask<string> Name() { await UniTask.Yield(); return ""x""; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("WhenAll"));
            Assert.That(code, Does.Contain("__co_start(Add(__ar: __ar").And.Contain("__co_start(Name(__ar: __ar"));
        }

        [Test]
        public void WhenAllStartsAllAndWaitsForEach()
        {
            string code = Lowered("WhenAll", @"public int n;
void Start() { Run(); }
async UniTask Run()
{
    await UniTask.WhenAll(A(), B());
    var (x, y) = await UniTask.WhenAll(Add(1, 1), Add(2, 2));
    n = x + y;
}
async UniTask A() { await UniTask.Yield(); }
async UniTask B() { await UniTask.Yield(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("WhenAll"));
            Assert.That(code, Does.Contain("__co_start(A())").And.Contain("__co_start(B())"), "子は、すべて始めてから待つ");
            Assert.That(code, Does.Contain("__co_start(Add(1, 1, __ar: __ar").And.Contain("__co_start(Add(2, 2, __ar: __ar"), "値を返すものも、holder を渡して始める");
        }

        // ---- CancellationToken（#225）----

        private const string TokenUse =
            "private CancellationTokenSource _cts = new CancellationTokenSource();\n"
            + "private bool _flag;\n"
            + "private int _n;\n"
            + "async UniTask Work(CancellationToken token = default)\n"
            + "{\n"
            + "    await UniTask.Delay(100, cancellationToken: token);\n"
            + "    await UniTask.Yield(token);\n"
            + "    await UniTask.DelayFrame(3, cancellationToken: token);\n"
            + "    await UniTask.WaitUntil(() => _flag, cancellationToken: token);\n"
            + "    await UniTask.WaitWhile(() => !_flag, cancellationToken: token);\n"
            + "    token.ThrowIfCancellationRequested();\n"
            + "    while (!token.IsCancellationRequested) { _n++; await UniTask.Yield(token); }\n"
            + "}\n"
            + "async UniTask Outer(CancellationToken token) { await Work(token); _n = 99; }\n"
            + "void Start() { Outer(_cts.Token); Work(); }\n"
            + "public void Stop() { _cts.Cancel(); _cts.Dispose(); }\n"
            + "public bool Stopped() { return _cts.Token.IsCancellationRequested; }";

        [Test]
        public void CancellationTokensBecomeIntArrays()
        {
            string code = Lowered("Token", TokenUse);
            Assert.That(code, Does.Not.Contain("CancellationToken"), "トークンの型は、すべて int[] になる");
            Assert.That(code, Does.Contain("new int[1]"), "new CancellationTokenSource() は、キャンセル済みかを持つ int[1]");
            Assert.That(code, Does.Contain("_cts[0] = 1"), "Cancel() はフラグを立てる");
            Assert.That(code, Does.Contain("__co_tk(__s)"), "キャンセルで終わるとき、待っている親も終わらせる印を付ける");
            Assert.That(code, Does.Contain("int[] token = null").Or.Contain("int[] token"), "async メソッドの引数");
            Assert.That(code, Does.Not.Contain("Dispose"), "Dispose() は何もしない");
        }

        [Test]
        public void TokenWaitsCheckEveryFrame()
        {
            string code = Lowered("TokenWaits", TokenUse);
            Assert.That(code, Does.Contain("UnityEngine.Time.time"), "Delay は、毎フレーム、期限とキャンセルを見る");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_tk\(__s\);").Count, Is.GreaterThanOrEqualTo(10), "待ちの前後と ThrowIfCancellationRequested でキャンセルを見る\n" + code);
        }

        [Test]
        public void TokensThatNeverCancelUsePlainWaits()
        {
            string code = Lowered("TokenNever", "async UniTask Foo() { await UniTask.Delay(100, cancellationToken: destroyCancellationToken); await UniTask.Yield(this.GetCancellationTokenOnDestroy()); }\nvoid Start() { Foo(); }");
            Assert.That(code, Does.Not.Contain("__co_tk").And.Not.Contain("CancellationToken"), "destroyCancellationToken・GetCancellationTokenOnDestroy() は、止まらないトークン");
            Assert.That(code, Does.Contain("WaitForSeconds"));
        }

        [Test]
        public void CancellationTokenCompilesInUdonSharp()
        {
            CompileOk(TokenUse);
        }

        [Test]
        public void ErrorForUnsupportedCancellationTokenForms()
        {
            // async のメソッドが 1 つもないと、この段は何もしないので、足しておく
            string head = "async UniTask Dummy() { await UniTask.Yield(); }\nprivate CancellationTokenSource _cts = new CancellationTokenSource();\n";
            AssertError(head + "void Start() { _cts.CancelAfter(100); }", "CancelAfter は未対応", BodyLine + 2);
            AssertError(head + "void Start() { _cts.Token.Register(() => { }); }", "Register は未対応", BodyLine + 2);
            AssertError(head + "void Start() { var l = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token); }", "CreateLinkedTokenSource は未対応", BodyLine + 2);
            AssertError("async UniTask Dummy() { await UniTask.Yield(); }\nvoid Start() { var c = new CancellationTokenSource(100); }", "引数のある new CancellationTokenSource", BodyLine + 1);
            AssertError(head + "void Start() { _cts.Cancel(true); }", "Cancel(bool) は未対応", BodyLine + 2);
            AssertError(head + "void Start() { _cts?.Cancel(); }", "?. （null 条件演算子） は未対応", BodyLine + 2);
            AssertError("async UniTask Dummy() { await UniTask.Yield(); }\nvoid Foo(CancellationToken t) { t.ThrowIfCancellationRequested(); }", "async メソッドの外", BodyLine + 1);
        }

        [Test]
        public void NoErrorForDefaultCancellationToken()
        {
            CompileOk("async UniTask Foo() { await UniTask.Delay(100, cancellationToken: default); await UniTask.Yield(CancellationToken.None); }\nvoid Start() { Foo(); }");
        }

        [Test]
        public void ErrorForAwaitOfUnknownTask()
        {
            AssertError("async UniTask Foo() { await UniTask.Run(() => 1); }", "UniTask.Run は未対応", BodyLine);
        }

        [Test]
        public void ErrorForAwaitOutsideStatement()
        {
            AssertError("public int n;\nasync UniTask Foo() { n = 1 + await Add(1, 2); }\nasync UniTask<int> Add(int a, int b) { return a + b; }", "式の途中の await は未対応", BodyLine + 1);
        }

        [Test]
        public void ErrorForAwaitInTry()
        {
            AssertError("async UniTask Foo() { try { await UniTask.Yield(); } finally { } }", "try・catch・finally・using・lock の中の await は未対応", BodyLine);
        }

        [Test]
        public void ErrorForAwaitedValueOfNoValueTask()
        {
            AssertError("public int n;\nasync UniTask Foo() { n = await UniTask.Delay(1); }", "値を返さない await の結果は、代入できません", BodyLine + 1);
        }

        [Test]
        public void ErrorForWhenAllOfDifferentTypesOrVariables()
        {
            string common = "async UniTask<int> Add() { await UniTask.Yield(); return 1; }\nasync UniTask<string> Name() { await UniTask.Yield(); return \"a\"; }\n";
            AssertError(common + "async UniTask Foo() { await UniTask.WhenAll(new UniTask[0]); }", "UniTask.WhenAll は、async メソッドの呼び出しか", BodyLine + 2);
            AssertError(common + "void Take(object o) { }\nasync UniTask Foo() { var t = Add(); Take(t); }", "UniTask の変数は、await", BodyLine + 3);
            AssertError(common + "async UniTask Foo() { UniTask t = UniTask.CompletedTask; }", "async メソッドの呼び出しだけ", BodyLine + 2);
        }

        [Test]
        public void ErrorForEventMethodReturningAValue()
        {
            AssertError("async UniTask<int> Start() { await UniTask.Yield(); return 1; }", "イベントのメソッド（Start・Interact など）は、値を返せません", BodyLine);
        }

        [Test]
        public void WhenAnyWaitsUntilOneIsDone()
        {
            string code = Lowered("WhenAny", @"public int n;
public string s;
void Start() { Run(); }
async UniTask Run()
{
    int i = await UniTask.WhenAny(A(), B());
    var (j, r1, r2) = await UniTask.WhenAny(Add(), Add());
    n = i + j + r1 + r2;
}
async UniTask A() { await UniTask.Yield(); }
async UniTask B() { await UniTask.Yield(); await UniTask.Yield(); }
async UniTask<int> Add() { await UniTask.Yield(); return 1; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("WhenAny"));
            Assert.That(code, Does.Contain("__co_start(A())").And.Contain("__co_start(B())"), "子は、すべて始める");
            Assert.That(code, Does.Contain("__co_alive("), "どれかが終わるまで、終わったかを見る");
            Assert.That(code, Does.Contain("protected bool __co_alive(int c)"));
        }

        [Test]
        public void UniTaskVariablesHoldTheStartedCoroutine()
        {
            string code = Lowered("Variables", @"public int n;
UniTask _field;
UniTask<int> _value;
void Start() { Run(); }
async UniTask Run()
{
    UniTask a = A();
    var b = Add(1, 2);
    _field = A();
    _value = Add(3, 4);
    await a;
    int x = await b;
    await _field;
    n = x;
    await UniTask.WhenAll(a, _field);
    var (p, q1, q2) = await UniTask.WhenAny(b, _value);
    b.Forget();
}
async UniTask A() { await UniTask.Yield(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(code, Does.Not.Contain("UniTask"));
            Assert.That(code, Does.Contain("a = __co_start(A())").And.Contain("b = __co_start(Add(1, 2, __ar: b__ar))").And.Contain("_field = __co_start(A())"));
            Assert.That(code, Does.Contain("b__ar = __co_h()"), "UniTask<T> の変数は、置き場の変数も持つ");
            Assert.That(code, Does.Contain("_value__ar"), "UniTask<T> のフィールドは、置き場のフィールドも持つ");
        }

        [Test]
        public void AwaitInControlFlowBecomesCoroutineCode()
        {
            // 波括弧のない 1 文（if・else・foreach）、ループ、switch の節の中の await（#207 の追加テスト案の 3）
            string code = Lowered("ControlFlow", @"public int n;
public int[] arr = new int[2];
UniTask _held;
void Start() { Run(true); }
async UniTask Run(bool c)
{
    if (c) await UniTask.Yield();
    if (c) n = await Add(1, 2);
    if (c) await Child();
    else await UniTask.Delay(10);
    if (c) Child();
    if (c) _held = Child();
    if (c) await UniTask.WhenAll(Child(), Child());
    if (c) await UniTask.WhenAny(Child(), Child());
    for (int i = 0; i < 2; i++) { var v = await Add(i, 1); n += v; }
    foreach (int k in arr) await UniTask.Yield();
    while (n < 5) { int d = await Add(1, 1); n += d; }
    do { await UniTask.Yield(); } while (false);
    switch (n)
    {
        case 1: await UniTask.Yield(); break;
        case 2: { int z = await Add(1, 2); n = z; break; }
        case 3: { UniTask<int> h = Add(2, 2); n = await h; break; }
        default: return;
    }
    if (c) return;
    await UniTask.Yield();
}
async UniTask Child() { await UniTask.Yield(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("async"));
            Assert.That(code, Does.Not.Contain("await ").Or.Contain("__co_await"), "await は残さない");
        }

        [Test]
        public void LocalsFromOutVarAndDeconstructionSurviveAnAwait()
        {
            string code = Lowered("OutVars", @"public int n;
public int[] arr = new int[2];
void Start() { Run(); }
async UniTask Run()
{
    int.TryParse(""1"", out var pv);
    var (a, b) = (1, 2);
    await UniTask.Yield();
    n = pv + a + b;
    foreach (int k in arr)
    {
        int.TryParse(""2"", out int w);
        await UniTask.Yield();
        n += w + k;
    }
}");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain("async"));
        }

        [Test]
        public void NegativeWaitsAreClampedInsteadOfThrowing()
        {
            // 負の値は、例外にせず 1 フレーム待つ（UniTask は例外。#207 の追加テスト案の 2）
            string code = Lowered("Negative", @"void Start() { Run(); }
async UniTask Run()
{
    await UniTask.DelayFrame(-1);
    await UniTask.Delay(-5);
    await UniTask.Delay(TimeSpan.FromSeconds(-1));
}");
            Assert.That(code, Does.Contain("Mathf.Max((-1), 1)"), "DelayFrame は、1 以上にそろえる");
            Assert.That(code, Does.Contain("__co_ws(").Or.Contain("__co_wf("), "Delay は、コルーチンの待ち（0 以下は次のフレーム）");
            Assert.That(code, Does.Not.Contain("ArgumentOutOfRange"));
        }

        [Test]
        public void ErrorForAsyncLambdasAnonymousMethodsAndLocalFunctions()
        {
            // async のラムダ・匿名メソッド・ローカル関数は、書けない（#207 の追加テスト案）。メッセージの出る行は、書いた行
            AssertError("public int n;\nvoid Start() { Action a = async () => { await UniTask.Yield(); n = 1; }; a(); }", "async のラムダ・匿名メソッドは未対応", BodyLine + 1);
            AssertError("public int n;\nvoid Start() { Func<UniTask> f = async () => { await UniTask.Yield(); n = 1; }; f(); }", "async のラムダ・匿名メソッドは未対応", BodyLine + 1);
            AssertError("public int n;\nvoid Start() { Action a = async delegate { await UniTask.Yield(); n = 1; }; a(); }", "async のラムダ・匿名メソッドは未対応", BodyLine + 1);
            AssertError("public int n;\nasync UniTask Foo() { async UniTask Local() { await UniTask.Yield(); n = 1; } await Local(); }", "async・extern・unsafe のローカル関数は未対応", BodyLine + 1);
            AssertError("public int n;\nvoid Start() { async void Local() { await UniTask.Yield(); n = 1; } Local(); }", "async・extern・unsafe のローカル関数は未対応", BodyLine + 1);
            AssertError("public int n;\nvoid Start() { UniTask.Void(async () => { await UniTask.Yield(); n = 1; }); }\nasync UniTask Z() { await UniTask.Yield(); }", "UniTask.Void に async のラムダを渡す書き方は未対応", BodyLine + 1);
        }

        [Test]
        public void CallsOfAnEventOverrideStayCallsOfTheMethod()
        {
            // イベントの override（Interact）を、コードから呼ぶ。コルーチンの名前を直接呼ぶと、派生の override を飛ばして基底のものが動くので、
            // メソッド（void のもの）の呼び出しのままにする（Forget() だけ外す）
            string code = Lowered("EventCall", @"public int n;
public override async void Interact() { await UniTask.Yield(); n++; }
public async UniTask Show() { await UniTask.Yield(); }
void Start() { Interact(); this.Interact(); }
void Twice() { Interact(); Show().Forget(); }");
            Assert.That(code, Does.Contain("Interact(); this.Interact();"));
            Assert.That(code, Does.Contain("Interact();"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_go\(__async_AsyUse_Interact\(").Count, Is.EqualTo(1), "コルーチンを始めるのは、void のメソッドの中だけ");
        }

        [Test]
        public void ErrorForAwaitingAnAsyncMethodNamedLikeAnEvent()
        {
            // イベントの名前（Update）の async UniTask は、await・変数・WhenAll・WhenAny では使えない（#214）。待たずに呼ぶ形は使える
            string update = "public virtual async UniTask Update() { await UniTask.Yield(); }\n";
            AssertError(update + "async UniTask Run() { await Update(); }", "イベントの名前（Update）の async メソッドは", BodyLine + 1);
            AssertError(update + "async UniTask Run() { UniTask t = Update(); await t; }", "イベントの名前（Update）の async メソッドは", BodyLine + 1);
            AssertError(update + "async UniTask Run() { await UniTask.WhenAll(Update(), Update()); }", "イベントの名前（Update）の async メソッドは", BodyLine + 1);
            AssertError(update + "async UniTask Run() { await UniTask.WhenAny(Update(), Update()); }", "イベントの名前（Update）の async メソッドは", BodyLine + 1);
            string ok = Lowered("EventNameCalls", update + "void Start() { Update(); Update().Forget(); }");
            Assert.That(ok, Does.Not.Contain("UniTask"));
        }

        [Test]
        public void DiscardedCallsStartTheCoroutineLikePlainCalls()
        {
            // _ = Foo(); は、Foo(); と同じ（待たずに呼ぶ。#212）。_ = await Add(1, 2); は、値を捨てて待つ
            string code = Lowered("Discard", @"void Start() { Run(true); }
async UniTask Run(bool c)
{
    _ = Child();
    _ = Add(1, 2);
    if (c) _ = Child(); else _ = Add(3, 4);
    _ = await Add(5, 6);
    await Child();
}
async UniTask Child() { await UniTask.Yield(); }
async UniTask<int> Add(int x, int y) { await UniTask.Yield(); return x + y; }");
            Assert.That(code, Does.Not.Contain("UniTask").And.Not.Contain(" _ = "), "破棄への代入は、残さない");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_go\(Child\(\)\)").Count, Is.EqualTo(2), "_ = Child(); は、Child(); と同じ");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_go\(Add\(\d, \d, __ar: __co_h\(\)\)\)").Count, Is.EqualTo(2), "値を返すものは、使わない置き場を渡して始める");
        }

        [Test]
        public void AVariableNamedUnderscoreIsNotADiscard()
        {
            // _ という名前の変数（UniTask _ = Foo();）は、これまでどおり変数として扱う。破棄は、その名前の変数がないときだけ
            string code = Lowered("UnderscoreVariable", @"void Start() { Run(); }
async UniTask Run()
{
    UniTask _ = Child();
    await _;
    _ = Child();
    await _;
}
async UniTask Child() { await UniTask.Yield(); }");
            Assert.That(code, Does.Not.Contain("UniTask"));
            Assert.That(code, Does.Contain("__co_await(__s, _)"), "変数 _ を await する");
        }

        [Test]
        public void ErrorForUniTaskThingsThatWereSilentOrUnclear()
        {
            // async のメソッドが 1 つもなくても、UniTask を返す非 async のメソッドは、エラー（#216）
            AssertError("UniTask Foo() => UniTask.CompletedTask;\nvoid Start() { Foo(); }", "UniTask を返す、async でないメソッドは未対応", BodyLine);
            // await foreach・await using 宣言は、C# の生のエラーでなく、案内つきのエラー
            AssertError("public int n;\nasync UniTask Foo() { await foreach (var i in Cysharp.Threading.Tasks.Linq.UniTaskAsyncEnumerable.Range(0, 3)) { n += i; } }",
                "await foreach・await using は未対応", BodyLine + 1);
            AssertError("async UniTask Foo() { await using var ms = new System.IO.MemoryStream(); await UniTask.Yield(); }", "await foreach・await using は未対応", BodyLine);
            // Forget の例外のハンドラー
            AssertError("async UniTask Bar() { await UniTask.Yield(); }\nvoid Start() { Bar().Forget(e => { }); }", "Forget の引数（例外のハンドラー）は未対応", BodyLine + 1);
        }

        [Test]
        public void ErrorForStoredUniTask()
        {
            AssertError("void Foo(UniTask t) { }\nasync UniTask Z() { await UniTask.Yield(); }", "UniTask は、async メソッドの戻り値と", BodyLine);
            AssertError("UniTask[] _all;\nasync UniTask Z() { await UniTask.Yield(); }", "UniTask は、async メソッドの戻り値と", BodyLine);
        }

        [Test]
        public void ErrorForAsyncMethodUsedAsValue()
        {
            AssertError("async UniTask A() { await UniTask.Yield(); }\nvoid Start() { Take(A()); }\nvoid Take(object o) { }", "async メソッド A は、Foo();", BodyLine + 1);
        }

        [Test]
        public void ErrorForAsyncMethodOfAnotherBehaviour()
        {
            string body = "public AsyOther other;\nvoid Start() { other.Go(); }";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("AsyUse.cs", Source(body)),
                new TestSource("AsyOther.cs", Head + "public class AsyOther : UdonSharpBehaviour { public async UniTask Go() { await UniTask.Yield(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("ほかのビヘイビアの async メソッド") && d.Contains($"AsyUse.cs({BodyLine + 1},")), string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void ErrorForStaticGenericAndExpressionBodied()
        {
            AssertError("static async UniTask S() { await UniTask.Yield(); }", "static の async メソッドは未対応", BodyLine);
            CompileOk("public int n;\nasync UniTask G<T>(T v) { await UniTask.Yield(); n++; }\nvoid Start() { G(1); G(\"a\"); }");   // ジェネリクスの段が型ごとのコピーにするので、async のメソッドとして書き換わる
            AssertError("async UniTask E() => await UniTask.Yield();", "{ } の本体", BodyLine);
        }

        [Test]
        public void ErrorForSystemTask()
        {
            AssertError("async System.Threading.Tasks.Task Foo() { await System.Threading.Tasks.Task.Delay(1); }", "UniTask・UniTaskVoid・void を返すものだけ", BodyLine);
        }

        [Test]
        public void ErrorForNonAsyncUniTaskMethod()
        {
            AssertError("async UniTask Ok() { await UniTask.Yield(); }\nUniTask Foo() => default;", "async でないメソッドは未対応", BodyLine + 1);
        }

        [Test]
        public void ErrorForUniTaskInPlainClass()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("AsyUse.cs",
                Head + "public class Plain { public async UniTask Foo() { await UniTask.Yield(); } }\npublic class AsyUse : UdonSharpBehaviour { void Start() { var p = new Plain(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("UdonSharpBehaviour でないクラスのメソッドの async")), string.Join("\n", r.Diagnostics));
        }
    }
}
