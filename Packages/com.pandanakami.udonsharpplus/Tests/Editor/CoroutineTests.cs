using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// コルーチン（#107）：IEnumerator を返すメソッドを状態機械に、StartCoroutine を状態の番号で開始するメソッドに書き換える。
    /// 実行時の順番・フレームは、FeatureTests のパネル「#107 coroutines」（CoroutineRunner）で普通の Unity と比べる
    /// </summary>
    public class CoroutineTests
    {
        private const string Head = "using System.Collections;\nusing UdonSharp;\nusing UnityEngine;\n";

        private static CompileResult Compile(string body)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("CoUse.cs", Head + "public class CoUse : UdonSharpBehaviour\n{\n" + body + "\n}"));
            return r;
        }

        private static CompileResult CompileOk(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
            return r;
        }

        private static void AssertError(string body, string message, int line)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains($"CoUse.cs({line},")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        // CoUse.cs の 1〜3 行目は using、4 行目はクラス、5 行目は {。本体は 6 行目から
        private const int BodyLine = 6;

        private const string Basic = @"private Coroutine _c;
void Start() { StartCoroutine(Foo(3)); _c = StartCoroutine(Bar()); }
IEnumerator Foo(int n)
{
    Debug.Log(n);
    yield return null;
    Debug.Log(n + 1);
    yield return new WaitForSeconds(0.5f);
    Debug.Log(n + 2);
}
private IEnumerator Bar() { yield return null; yield break; }";

        [Test]
        public void ResumeEventsArePublicAndStepsAreNot()
        {
            var entries = CompileOk(Basic).Programs.Single().EntryPoints.ToList();
            Assert.That(entries, Does.Contain("__co_frame"));
            Assert.That(entries, Does.Contain("__co_sec"));
            Assert.That(entries.Where(e => e.StartsWith("__co_Foo") || e.StartsWith("__co_Bar") || e == "__co_alloc"), Is.Empty, string.Join(", ", entries));
        }

        [Test]
        public void UsesDelayedEvents()
        {
            string uasm = CompileOk(Basic).Programs.Single().Uasm;
            Assert.That(uasm, Does.Contain("__SendCustomEventDelayedFrames__"));
            Assert.That(uasm, Does.Contain("__SendCustomEventDelayedSeconds__"));
        }

        [Test]
        public void BehaviourWithoutCoroutinesIsUnchanged()
        {
            var entries = CompileOk("void Start() { Debug.Log(1); }").Programs.Single().EntryPoints.ToList();
            Assert.That(entries.Where(e => e.StartsWith("__co_")), Is.Empty);
        }

        [Test]
        public void KeepsLinesAfterCoroutine()
        {
            // コルーチンのあとのメソッドで出た U# のエラーが、元の行を指す
            AssertError(Basic + "\nvoid Late() { try { } finally { } }", "Try/Catch/Finally", BodyLine + 11);
        }

        [Test]
        public void ErrorForYieldInsideLoop()
        {
            AssertError("IEnumerator Foo() { for (int i = 0; i < 3; i++) { yield return null; } }", "if・ループなどの中の yield return", BodyLine);
        }

        [Test]
        public void ErrorForUnsupportedYieldValue()
        {
            AssertError("IEnumerator Foo()\n{\n    yield return 3;\n}", "この yield return の値は", BodyLine + 2);
        }

        [Test]
        public void ErrorForStringStart()
        {
            AssertError("void Start() { StartCoroutine(\"Foo\"); }\nIEnumerator Foo() { yield return null; }", "文字列で指定する StartCoroutine", BodyLine);
        }

        [Test]
        public void ErrorForOtherBehavioursStart()
        {
            AssertError("public CoUse other;\nvoid Start() { other.StartCoroutine(Foo()); }\nIEnumerator Foo() { yield return null; }", "ほかのビヘイビアの StartCoroutine", BodyLine + 1);
        }
    }
}
