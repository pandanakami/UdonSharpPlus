using System.IO;
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

        /// <summary>書き換え後の CoUse.cs（エラーがないこと）</summary>
        private static string Lowered(string name, string body)
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusCoroutine_" + name));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("CoUse.cs", Head + "public class CoUse : UdonSharpBehaviour\n{\n" + body + "\n}"));
                Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
                return File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "CoUse.cs"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ---- 2 段階目：yield をまたぐ変数と、if・ループ・switch の中の yield ----------------------

        [Test]
        public void YieldInsideStatementsCompiles()
        {
            CompileOk(@"IEnumerator Foo(int n)
{
    int total = 0;
    for (int i = 0; i < n; i++) { if (i == 1) continue; total += i; yield return null; if (total > 9) break; }
    var list = new System.Collections.Generic.List<int> { 1, 2 };
    foreach (int x in list) { yield return null; total += x; }
    var map = new System.Collections.Generic.Dictionary<string, int> { { ""a"", 1 } };
    foreach (var kv in map) { yield return null; total += kv.Value; }
    int[] arr = { 5, 6 };
    foreach (var a in arr) { yield return new WaitForSeconds(0.1f); total += a; }
    foreach (char c in ""ab"") { yield return null; total += c; }
    while (total > 0) { total -= 10; if (total < 5) { yield return null; } else total--; }
    do { n--; yield return null; } while (n > 0);
    switch (total) { case 1: case 2: yield return null; break; default: total = 3; break; }
    Debug.Log(total + n);
}");
        }

        [Test]
        public void RenamesSameNamesInSiblingScopes()
        {
            string code = Lowered("Rename", @"IEnumerator Foo()
{
    for (int i = 0; i < 2; i++) yield return null;
    for (int i = 0; i < 3; i++) yield return null;
    { int k = 1; Debug.Log(k); }
    int k2 = 0; yield return null; Debug.Log(k2);
}");
            Assert.That(code, Does.Contain("i__0"));
            Assert.That(code, Does.Contain("i__1"));
        }

        [Test]
        public void SavesOnlyVariablesUsedAfterTheYield()
        {
            // a は yield のあとで使わない。b は 1 つ目の yield のあとで宣言する。b を書くのは 2 つ目の yield だけ。
            // n は書き換えないので、yield のたびには書かない（再開のたびに頭で読む）
            string code = Lowered("Saved", @"IEnumerator Foo(int n)
{
    int a = n; Debug.Log(a);
    yield return null;
    int b = n + 1;
    yield return null;
    Debug.Log(b);
}");
            string step = code.Substring(code.IndexOf("__co_Foo_0"), code.IndexOf("int Foo(") - code.IndexOf("__co_Foo_0"));
            Assert.That(step, Does.Not.Match(@"__s\[\d+\] = a;"));
            Assert.That(step, Does.Not.Match(@"__s\[\d+\] = n;"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(step, @"__s\[\d+\] = b;").Count, Is.EqualTo(1));
        }

        [Test]
        public void SavesLoopVariablesAcrossIterations()
        {
            string code = Lowered("Loop", @"IEnumerator Foo()
{
    int sum = 0;
    for (int i = 0; i < 3; i++) { yield return null; sum += i; }
    Debug.Log(sum);
}");
            Assert.That(code, Does.Match(@"__s\[\d+\] = i;"));
            Assert.That(code, Does.Match(@"__s\[\d+\] = sum;"));
        }

        [Test]
        public void ErrorForYieldInsideTry()
        {
            AssertError("IEnumerator Foo() { lock (this) { yield return null; } }", "この文（try・using・lock", BodyLine);
        }

        [Test]
        public void ErrorForForeachOverOtherCollections()
        {
            AssertError("IEnumerator Foo() { foreach (Transform t in transform) { yield return null; } }", "yield return を含む foreach", BodyLine);
        }

        // ---- 3 段階目：停止と、Coroutine・IEnumerator の値 ----------------------

        private const string Stops = @"private Coroutine _c;
private IEnumerator _e;
void Start() { _c = StartCoroutine(Foo()); _e = Foo(); StartCoroutine(_e); if (_c != null) StopCoroutine(_c); StopCoroutine(_e); _c = null; StopAllCoroutines(); }
IEnumerator Foo() { yield return null; }";

        [Test]
        public void StopCallsAndNullComparisons()
        {
            string code = Lowered("Stops", Stops);
            Assert.That(code, Does.Contain("if (_c != 0) __co_stop(_c);"));
            Assert.That(code, Does.Contain("__co_stop(_e);"));
            Assert.That(code, Does.Contain("_c = 0;"));
            Assert.That(code, Does.Contain("__co_stopall();"));
        }

        [Test]
        public void GeneratesOnDisable()
        {
            var entries = CompileOk(Stops).Programs.Single().EntryPoints.ToList();
            Assert.That(entries, Does.Contain("_onDisable"));
        }

        [Test]
        public void StopsAtTheStartOfUsersOnDisable()
        {
            string code = Lowered("UserOnDisable", Stops + "\nvoid OnDisable() { Debug.Log(1); }");
            Assert.That(code, Does.Contain("void OnDisable() { __co_off(); Debug.Log(1); }"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"void OnDisable\(\)").Count, Is.EqualTo(1));
        }

        [Test]
        public void ErrorForStringStop()
        {
            AssertError("void Start() { StopCoroutine(\"Foo\"); }\nIEnumerator Foo() { yield return null; }", "文字列で指定する StopCoroutine", BodyLine);
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
