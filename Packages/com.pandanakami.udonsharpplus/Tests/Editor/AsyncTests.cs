using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// UniTask の async / await（#207）→ コルーチン。UniTask は入れていないので、型だけの殻（Stub。本物と同じ名前空間・同じ引数）を
    /// テストの入力に含める。動きは FeatureTests のパネル（AsyncRunner）で、コルーチンと同じ順番・フレームになることを見る
    /// </summary>
    public class AsyncTests
    {
        /// <summary>本物の UniTask の名前空間・型・引数（この段が見分ける部分だけ。中身は空）</summary>
        internal const string Stub = @"using System;
using System.Threading;
namespace Cysharp.Threading.Tasks
{
    public enum PlayerLoopTiming { Update, FixedUpdate, LastUpdate }
    public enum DelayType { DeltaTime, UnscaledDeltaTime, Realtime }
    public readonly struct UniTask
    {
        public readonly struct Awaiter { public bool IsCompleted => true; public void GetResult() { } }
        public Awaiter GetAwaiter() => default;
        public readonly struct YieldAwaitable { public Awaiter GetAwaiter() => default; }
        public static YieldAwaitable Yield() => default;
        public static YieldAwaitable Yield(PlayerLoopTiming timing) => default;
        public static UniTask Yield(CancellationToken cancellationToken, bool cancelImmediately = false) => default;
        public static UniTask NextFrame() => default;
        public static UniTask NextFrame(PlayerLoopTiming timing) => default;
        public static UniTask DelayFrame(int delayFrameCount, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask Delay(int millisecondsDelay, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask Delay(TimeSpan delayTimeSpan, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask Delay(int millisecondsDelay, DelayType delayType, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask Delay(TimeSpan delayTimeSpan, DelayType delayType, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
        public static UniTask WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false) => default;
    }
    public readonly struct UniTask<T> { public readonly struct Awaiter { public bool IsCompleted => true; public T GetResult() => default; } public Awaiter GetAwaiter() => default; }
    public readonly struct UniTaskVoid { public void Forget() { } }
    public static class UniTaskExtensions { public static void Forget(this UniTask task) { } }
}";

        private const string Head = "using System;\nusing System.Threading;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\n";

        // AsyUse.cs の 1〜5 行目は using、6 行目はクラス、7 行目は {。本体は 8 行目から
        private const int BodyLine = 8;

        private static string Source(string body) => Head + "public class AsyUse : UdonSharpBehaviour\n{\n" + body + "\n}";

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("UniTaskStub.cs", Stub), new TestSource("AsyUse.cs", Source(body)));

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
                    new TestSource("UniTaskStub.cs", Stub), new TestSource("AsyUse.cs", Source(body)));
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
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("UniTaskStub.cs", Stub),
                new TestSource("AsyUse.cs", Source("async UniTask A() { await UniTask.Yield(); }\nvoid Start() { A(); var p = new Plain(); p.Hit(); }")),
                new TestSource("Plain.cs", "public class Plain { public int hp = 3; public void Hit() { hp--; } public int Get() { return hp; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void UniTaskInAnotherClassOfTheSameFileIsLeftAlone()
        {
            // UdonSharpBehaviour でないクラスの UniTask は、普通の Unity のもの（残った UniTask のエラーは、UdonSharpBehaviour の中だけ）
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("UniTaskStub.cs", Stub), new TestSource("Mix.cs",
                Head + "public class Helper : MonoBehaviour { UniTask _t; public UniTask Foo() => UniTask.Delay(1); }\n"
                + "public class Mix : UdonSharpBehaviour { public int n; void Start() { n++; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        // ---- 対応しない書き方 ----------------------------------------------------

        [Test]
        public void ErrorForValueReturningUniTask()
        {
            AssertError("async UniTask<int> Foo() { await UniTask.Yield(); return 1; }", "値を返す UniTask<T> は未対応", BodyLine);
        }

        [Test]
        public void ErrorForCancellationToken()
        {
            AssertError("public CancellationToken token;\nasync UniTask Foo() { await UniTask.Delay(100, cancellationToken: token); }", "CancellationToken は未対応", BodyLine + 1);
        }

        [Test]
        public void NoErrorForDefaultCancellationToken()
        {
            CompileOk("async UniTask Foo() { await UniTask.Delay(100, cancellationToken: default); await UniTask.Yield(CancellationToken.None); }\nvoid Start() { Foo(); }");
        }

        [Test]
        public void ErrorForAwaitOfUnknownTask()
        {
            AssertError("async UniTask Foo() { await UniTask.Run(() => 1); }", "この await は未対応", BodyLine);
        }

        [Test]
        public void ErrorForAwaitOutsideStatement()
        {
            AssertError("async UniTask Foo() { var x = await UniTask.Delay(1); }", "await は、文（", BodyLine);
        }

        [Test]
        public void ErrorForAwaitInTry()
        {
            AssertError("async UniTask Foo() { try { await UniTask.Yield(); } finally { } }", "try・catch・finally・using・lock の中の await は未対応", BodyLine);
        }

        [Test]
        public void ErrorForStoredUniTask()
        {
            AssertError("UniTask _t;\nasync UniTask Foo() { await UniTask.Yield(); }", "UniTask は、async メソッドの戻り値と", BodyLine);
        }

        [Test]
        public void ErrorForAsyncMethodUsedAsValue()
        {
            AssertError("async UniTask A() { await UniTask.Yield(); }\nvoid Start() { var t = A(); }", "async メソッド A は、Foo();", BodyLine + 1);
        }

        [Test]
        public void ErrorForAsyncMethodOfAnotherBehaviour()
        {
            string body = "public AsyOther other;\nvoid Start() { other.Go(); }";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("UniTaskStub.cs", Stub),
                new TestSource("AsyUse.cs", Source(body)),
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
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("UniTaskStub.cs", Stub), new TestSource("AsyUse.cs",
                Head + "public class Plain { public async UniTask Foo() { await UniTask.Yield(); } }\npublic class AsyUse : UdonSharpBehaviour { void Start() { var p = new Plain(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("UdonSharpBehaviour でないクラスのメソッドの async")), string.Join("\n", r.Diagnostics));
        }
    }
}
