using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public void GenericCoroutineMethodIsCopiedPerTypeArgument()
        {
            // ジェネリックメソッドのコルーチン：型引数ごとのコピーが、それぞれ状態機械になる
            string code = Lowered("Generic", @"private string _log = """";
void Start() { StartCoroutine(Hold(3)); StartCoroutine(Hold(""a"")); }
IEnumerator Hold<T>(T value)
{
    yield return null;
    _log += value.ToString();
}");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"private void __co_Hold\w*\(object\[\] __s\)").Count, Is.EqualTo(2), code);
        }

        [Test]
        public void CoalesceTernaryAndNullCastCompile()
        {
            // c ?? d・c ??= d（番号の 0 が null）、待ち方の三項演算子、(IEnumerator)null
            string code = Lowered("Coalesce", @"private Coroutine _c;
private IEnumerator _e;
private bool _f;
void Start() { _c = _c ?? StartCoroutine(Foo()); _e ??= Foo(); StartCoroutine((IEnumerator)null); }
IEnumerator Foo() { yield return _f ? null : new WaitForSeconds(1f); yield return new WaitForSeconds(_f ? 1f : 2f); }");
            Assert.That(code, Does.Contain("((_c) != 0 ? (_c) : (__co_start(Foo())))"));
            Assert.That(code, Does.Contain("_e = ((_e) != 0 ? (_e) : (Foo()))"));
            Assert.That(code, Does.Contain("__co_ws(__s, 1f)"));
        }

        [Test]
        public void HandleFieldsAreNotSerialized()
        {
            // Unity は Coroutine・IEnumerator のフィールドをシリアライズしない。int にしたあとも、シリアライズさせない
            // （シリアライズすると、Inspector に番号が出て、配列は null ではなく空の配列で始まる。#127 と同じ）
            CompileResult r = CompileOk(@"public Coroutine c;
public IEnumerator e;
public Coroutine[] cs;
[VRC.Udon.Serialization.OdinSerializer.OdinSerialize] public Coroutine oc;
[VRC.Udon.Serialization.OdinSerializer.OdinSerialize]
private IEnumerator op;
public int[] ints;
void Start() { c = StartCoroutine(Foo()); Debug.Log(cs == null); Debug.Log(e == null); Debug.Log(oc == null); Debug.Log(op == null); }
IEnumerator Foo() { yield return null; }");
            var defaults = r.Programs.Single().HeapDefaults;
            foreach (string name in new[] { "c", "e", "cs", "oc", "op" })
                Assert.That(defaults.ContainsKey(name), Is.False, $"{name} = {(defaults.TryGetValue(name, out string v) ? v : "")}");
            Assert.That(defaults["ints"], Is.EqualTo("[]"), "書き換えない型の配列は、UdonSharp のとおり空の配列");
        }

        [TestCase("[SerializeField] private Coroutine c;")]
        [TestCase("[UdonSynced] public int n; [UdonSynced] public Coroutine c;")]
        public void ErrorForSerializedOrSyncedHandleField(string field)
        {
            CompileResult r = Compile(field + "\nIEnumerator Foo() { yield return null; }");
            var errors = r.Diagnostics.Where(d => d.StartsWith("Error")).ToList();
            Assert.That(errors.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(errors[0], Does.Contain("Coroutine・IEnumerator のフィールドは、シリアライズ・同期できません").And.Contain($"CoUse.cs({BodyLine},"));
        }

        [Test]
        public void KeepsLinesAfterMultiLineStatements()
        {
            // 複数行の宣言・yield の値・if の条件・while の条件のあとでも、うしろの行がずれない（#107 の追加テスト案の 14）
            AssertError(@"IEnumerator Foo(int n)
{
    int y = n
        + 1;
    yield return new WaitForSeconds(y
        * 0.5f);
    if (y > 0
        && n > 0)
    {
        yield return null;
    }
    while (y > 0
        && n > 0)
    {
        y--;
        yield return null;
    }
    Debug.Log(y);
}
void Late() { try { } finally { } }", "Try/Catch/Finally", BodyLine + 19);
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
        public void KeepsOutVariablesAcrossYield()
        {
            string code = Lowered("OutVar", @"IEnumerator Foo()
{
    int.TryParse(""5"", out int p);
    yield return null;
    for (int i = 0; i < 2; i++) { int.TryParse(""7"", out var q); yield return null; Debug.Log(q); }
    Debug.Log(p);
}");
            Assert.That(code, Does.Contain("out p);"));
            Assert.That(code, Does.Contain("out q);"));
            Assert.That(code, Does.Match(@"__s\[\d+\] = p;"));
            Assert.That(code, Does.Match(@"__s\[\d+\] = q;"));
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
        public void StopOfFreshCoroutineDropsState()
        {
            // StopCoroutine(Foo()) は作ったばかりの状態を空きに戻す（#174）。変数に入れたものは、あとで開始できるのでふつうに止める
            string code = Lowered("StopFresh", @"private IEnumerator _e;
void Update() { StopCoroutine(Foo()); StopCoroutine((Foo())); _e = Foo(); StopCoroutine(_e); }
IEnumerator Foo() { yield return null; }");
            Assert.That(code, Does.Contain("__co_drop(Foo());"));
            Assert.That(code, Does.Contain("__co_drop((Foo()));"));
            Assert.That(code, Does.Contain("__co_stop(_e);"));
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
        public void StartAndStopByName()
        {
            // 文字列で指定する開始・停止（#174）。value は Foo の引数に渡す。object の値はキャストする
            string code = Lowered("ByName", @"private Coroutine _c; private object _o = 3; private string _n = ""Foo"";
void Start() { StartCoroutine(""Foo""); StartCoroutine(nameof(Bar), 5); _c = StartCoroutine(""Bar"", _o); StopCoroutine(""Foo""); StopCoroutine(_n); }
IEnumerator Foo() { yield return null; }
IEnumerator Bar(int n) { yield return null; }");
            Assert.That(code, Does.Contain("__co_go(__co_named(this.Foo()));"));
            Assert.That(code, Does.Contain("__co_go(__co_named(this.Bar((5))));"));
            Assert.That(code, Does.Contain("_c = __co_start(__co_named(this.Bar((int)(_o))));"));
            Assert.That(code, Does.Contain("__co_stopn(\"Foo\");"));
            Assert.That(code, Does.Contain("__co_stopn(_n);"));
        }

        [Test]
        public void SwitchWithPatternsAcrossYield()
        {
            // パターンのラベルは switch の段（#198）が先に番号の case にするので、yield をまたいでも使える
            CompileOk("IEnumerator Foo(int n) { switch (n) { case var k when k > 1: yield return null; Debug.Log(k); break; case 1 or 2: yield return null; break; default: break; } }");
        }

        [Test]
        public void LocalFunctionCoroutine()
        {
            // ローカル関数のコルーチン（#174）。デリゲートの段がメソッドに移し、外の変数は環境で共有する。WaitUntil も下ごしらえする
            string code = Lowered("LocalCo", @"private bool _go;
void Start() { int n = 0; IEnumerator Blink(int k) { n += k; yield return new WaitUntil(() => _go); n++; Debug.Log(n); } StartCoroutine(Blink(2)); StopCoroutine(Blink(1)); }");
            Assert.That(code, Does.Not.Contain("yield return new"));
            Assert.That(code, Does.Not.Contain("yield return null"));
            Assert.That(code, Does.Not.Contain("WaitUntil"));
            Assert.That(code, Does.Match(@"__co_go\(__lf_Blink__\d+_\d+\("));
            Assert.That(code, Does.Match(@"__co_drop\(__lf_Blink__\d+_\d+\("));
            Assert.That(code, Does.Contain("\"Blink\""), "ログの名前は元の名前");
        }

        [Test]
        public void ErrorsForStartByName()
        {
            const string co = "\nIEnumerator Foo() { yield return null; }\nIEnumerator Bar(int n) { yield return null; }";
            AssertError("private string _n = \"Foo\"; void Start() { StartCoroutine(_n); }" + co, "名前を定数", BodyLine);
            AssertError("void Start() { StartCoroutine(\"Baz\"); }" + co, "Baz というメソッドがありません", BodyLine);
            AssertError("void Start() { StartCoroutine(\"Bar\"); }" + co, "引数の数が合いません", BodyLine);
            AssertError("void Start() { StartCoroutine(\"Foo\", 1); }" + co, "引数の数が合いません", BodyLine);
            AssertError("void Start() { StartCoroutine(\"Bar\", \"x\"); }" + co, "この値は Bar の引数", BodyLine);
        }

        // ---- 同じコルーチンへの再入（#107 の決めたこと 10） ----------------------

        [Test]
        public void ErrorForDirectReentry()
        {
            AssertError("IEnumerator Foo(int n)\n{\n    yield return null;\n    StartCoroutine(Foo(n + 1));\n    Debug.Log(n);\n}", "Foo → Foo", BodyLine + 3);
        }

        [Test]
        public void ErrorForReentryThroughMethod()
        {
            AssertError("IEnumerator Foo()\n{\n    yield return null;\n    Kick();\n}\nvoid Kick() { StartCoroutine(Foo()); }", "Foo → Kick → Foo", BodyLine + 3);
        }

        [Test]
        public void ErrorForReentryThroughFirstSegmentOfAnotherCoroutine()
        {
            AssertError("IEnumerator Foo()\n{\n    StartCoroutine(Bar());\n    yield return null;\n}\nIEnumerator Bar() { StartCoroutine(Foo()); yield return null; }", "Foo → Bar → Foo", BodyLine + 2);
        }

        [Test]
        public void NoErrorWhenStartedAfterAnotherCoroutinesYield()
        {
            // Bar が Foo を開始しても、Foo の最初の区間は yield だけ。Foo が Bar を開始するのは Foo の yield のあとで、Bar の最初の区間も yield だけ
            // （どちらも、相手が動いている途中に相手へ入らない）
            CompileOk("IEnumerator Foo()\n{\n    yield return null;\n    StartCoroutine(Bar());\n}\nIEnumerator Bar() { yield return null; StartCoroutine(Foo()); }");
        }

        [Test]
        public void NoErrorForRecursionWithYieldReturn()
        {
            CompileOk("IEnumerator Walk(int n)\n{\n    int x = n;\n    if (n > 0) yield return StartCoroutine(Walk(n - 1));\n    if (n > 0) yield return Walk(n - 1);\n    Debug.Log(x);\n}");
        }

        [Test]
        public void ReentryGuardIsGenerated()
        {
            string code = Lowered("Guard", "IEnumerator Foo() { yield return null; }");
            Assert.That(code, Does.Contain("if (__co_on[id] != 0)"));
        }

        // ---- 継承・コレクション・外からの呼び出し（#107 の追加テスト案の 10・11・9） ----------------------

        [Test]
        public void InheritanceSharesOneRuntimeInTheBase()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("CoBase.cs", Head + "public class CoBase : UdonSharpBehaviour\n{\n    public void Go() { StartCoroutine(A()); }\n    protected virtual IEnumerator A() { yield return null; }\n}"),
                new TestSource("CoDerived.cs", Head + "public class CoDerived : CoBase\n{\n    public void More() { StartCoroutine(B()); }\n    private IEnumerator B() { yield return null; }\n    protected override IEnumerator A() { yield return null; yield return null; }\n    void OnDisable() { Debug.Log(1); }\n}"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            Assert.That(r["CoDerived"].Uasm, Does.Contain("__co_stepx"));
        }

        /// <summary>
        /// 継承したクラスの OnDisable：U# は同じ名前のメソッドを隠せないので、書いていないクラスには、親をさかのぼって OnDisable がなければ作り、
        /// 子孫が書いていれば virtual にして、子孫の OnDisable を override にする（書き換え後のコードだけ）。どのクラスのプログラムにも _onDisable が入る
        /// </summary>
        [TestCase("public class OdB : UdonSharpBehaviour { CO }", "public class OdD1 : OdB { private void OnDisable() { Debug.Log(1); } }", "public class OdD2 : OdB { }", TestName = "OnDisable only in one derived class")]
        [TestCase("public class OdB : UdonSharpBehaviour { CO }", "public class OdD1 : OdB { protected virtual void OnDisable() { } }", "public class OdD2 : OdD1 { protected override void OnDisable() { base.OnDisable(); } }", TestName = "OnDisable virtual chain below the base")]
        [TestCase("public class OdB : UdonSharpBehaviour { CO protected virtual void OnDisable() { } }", "public class OdD1 : OdB { protected override void OnDisable() { base.OnDisable(); } }", "public class OdD2 : OdB { }", TestName = "OnDisable written as virtual in the base")]
        [TestCase("public class OdB : UdonSharpBehaviour { private void OnDisable() { } }", "public class OdD1 : OdB { CO }", "public class OdD2 : OdD1 { }", TestName = "OnDisable in a base without coroutines")]
        public void EveryClassGetsOnDisable(string b, string d1, string d2)
        {
            const string co = "public void Go() { StartCoroutine(Co()); } IEnumerator Co() { yield return null; }";
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("OdB.cs", Head + b.Replace("CO", co)), new TestSource("OdD1.cs", Head + d1.Replace("CO", co)), new TestSource("OdD2.cs", Head + d2.Replace("CO", co)));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            foreach (string type in new[] { "OdB", "OdD1", "OdD2" })
                Assert.That(r[type].EntryPoints, Does.Contain("_onDisable"), type);
        }

        [Test]
        public void HandlesInListsAreIntTokens()
        {
            string code = Lowered("List", @"private System.Collections.Generic.List<Coroutine> _list = new System.Collections.Generic.List<Coroutine>();
void Start() { Coroutine c = StartCoroutine(Foo()); _list.Add(c); if (_list.Contains(c)) StopCoroutine(_list[0]); }
IEnumerator Foo() { yield return null; }");
            Assert.That(code, Does.Not.Contain("(object)(c)"));
            Assert.That(code, Does.Contain(".Int"));
        }

        [Test]
        public void FrameEventIgnoresCallsInTheSameFrame()
        {
            string code = Lowered("Guard2", "IEnumerator Foo() { yield return null; }");
            Assert.That(code, Does.Contain("__co_ff[__co_fh] < f"));
        }

        [Test]
        public void FrameEventIsReservedOncePerFrame()
        {
            // 次のフレームの予約は、ビヘイビアごとに 1 フレーム 1 回（#174）
            string code = Lowered("FrameOnce", "IEnumerator Foo() { yield return null; }");
            Assert.That(code, Does.Contain("if (__co_fr != f) { __co_fr = f; SendCustomEventDelayedFrames(\"__co_frame\", 1); }"));
            Assert.That(Regex.Matches(code, "SendCustomEventDelayedFrames").Count, Is.EqualTo(1));
            Assert.That(code, Does.Not.Contain("RecursiveMethod"), "戻り値を使う開始は、自分の積み上げで退避する");
        }

        [Test]
        public void ErrorForUnsupportedYieldValue()
        {
            AssertError("IEnumerator Foo()\n{\n    yield return Resources.UnloadUnusedAssets();\n}", "この yield return の値は", BodyLine + 2);
        }

        // ---- 4 段階目：WaitUntil・WaitWhile・WaitForSecondsRealtime・子のコルーチン ----------------------

        [Test]
        public void WaitUntilBecomesALoopOverTheCondition()
        {
            string code = Lowered("WaitUntil", @"private bool _ready;
private bool Ready() => _ready;
IEnumerator Foo()
{
    int n = 0;
    yield return new WaitUntil(() => _ready && n < 3);
    yield return new WaitWhile(Ready);
    yield return new WaitForSecondsRealtime(0.5f);
    yield return 0;
    Debug.Log(n);
}");
            Assert.That(code, Does.Not.Contain("WaitUntil"));
            Assert.That(code, Does.Not.Contain("WaitWhile"));
            Assert.That(code, Does.Not.Contain("WaitForSecondsRealtime"));
            Assert.That(code, Does.Contain("(_ready && n < 3)"));
            Assert.That(code, Does.Contain("Ready()"));
            Assert.That(code, Does.Contain("realtimeSinceStartup"));
        }

        [Test]
        public void CachedWaitForSecondsBecomesSeconds()
        {
            string code = Lowered("Cached", @"private WaitForSeconds _wait = new WaitForSeconds(0.5f);
IEnumerator Foo() { var w = new WaitForSecondsRealtime(1f); yield return _wait; yield return w; }");
            Assert.That(code, Does.Contain("private float _wait = (float)(0.5f);"));
            Assert.That(code, Does.Contain("__co_ws(__s, _wait)"));
        }

        // #203：下ごしらえは、待つ型の名前（using の別名も）・yield・?? を含むクラスだけを見る
        [Test]
        public void AliasedWaitForSecondsBecomesSeconds()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusCoroutine_Alias"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("CoUse.cs", Head + "using W = UnityEngine.WaitForSeconds;\npublic class CoUse : UdonSharpBehaviour\n{\npublic CoHolder holder;\nIEnumerator Foo() { yield return holder.wait; }\nvoid Start() { StartCoroutine(Foo()); }\n}\n"
                        + "public class CoHolder : UdonSharpBehaviour\n{\npublic W wait = new W(0.5f);\n}"));
                Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
                string code = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "CoUse.cs"));
                Assert.That(code, Does.Contain("public float wait = (float)(0.5f);"));
                Assert.That(code, Does.Contain("__co_ws(__s, holder.wait)"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void WaitsForChildCoroutines()
        {
            string code = Lowered("Child", @"private Coroutine _c;
IEnumerator Child() { yield return null; }
IEnumerator Foo() { yield return StartCoroutine(Child()); yield return Child(); _c = StartCoroutine(Child()); yield return _c; }");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(code, @"__co_await\(__s, Child\(\)\)").Count, Is.EqualTo(2));
            Assert.That(code, Does.Contain("__co_await(__s, _c)"));
        }

        [Test]
        public void ErrorForWaitUntilWithBlockLambda()
        {
            AssertError("IEnumerator Foo() { yield return new WaitUntil(() => { return true; }); }", "WaitUntil・WaitWhile の条件は", BodyLine);
        }

        [Test]
        public void ErrorForStoredWaitUntil()
        {
            AssertError("IEnumerator Foo() { WaitUntil w = new WaitUntil(() => true); yield return w; }", "WaitUntil・WaitWhile を変数に入れて", BodyLine);
        }

        [Test]
        public void ErrorForOtherBehavioursStart()
        {
            AssertError("public CoUse other;\nvoid Start() { other.StartCoroutine(Foo()); }\nIEnumerator Foo() { yield return null; }", "ほかのビヘイビアの StartCoroutine", BodyLine + 1);
        }

        // ---- 5 段階目：対応しない書き方（#107 の追加テスト案の 15）。原因のところに 1 つだけ出る ----------------------

        [TestCase("IEnumerator Foo() { try { } finally { yield return null; } }", "この文（try・using・lock")]
        [TestCase("IEnumerator Foo() { yield return null; using (var d = new System.IO.MemoryStream()) { yield return null; yield return null; } }", "この文（try・using・lock")]
        [TestCase("IEnumerator Foo() { yield return null; lock (this) { yield return null; } yield return null; }", "この文（try・using・lock")]
        [TestCase("IEnumerator Foo() { yield return new WaitForEndOfFrame(); }", "WaitForEndOfFrame は未対応です")]
        [TestCase("private WaitForEndOfFrame _w = new WaitForEndOfFrame(); IEnumerator Foo() { yield return _w; }", "WaitForEndOfFrame は未対応です")]
        [TestCase("IEnumerator Foo() { var w = new WaitForFixedUpdate(); yield return w; }", "WaitForFixedUpdate は未対応です")]
        [TestCase("void Start() { IEnumerator e = new int[2].GetEnumerator(); StartCoroutine(e); }", "外部のメソッド（Array.GetEnumerator）")]
        [TestCase("IEnumerator Foo() { yield return transform.GetEnumerator(); }", "外部のメソッド（Transform.GetEnumerator）")]
        [TestCase("System.Collections.Generic.IEnumerator<int> Foo() { yield return 1; }", "を返す yield（列挙のための yield）")]
        [TestCase("IEnumerable Foo() { yield return null; }", "を返す yield（列挙のための yield）")]
        [TestCase("System.Collections.Generic.IEnumerable<int> Foo() { yield return 1; }", "を返す yield（列挙のための yield）")]
        [TestCase("void Start() { IEnumerator e = Foo(); e.MoveNext(); }\nIEnumerator Foo() { yield return null; }", "IEnumerator の MoveNext")]
        [TestCase("public CoUse other; IEnumerator Get() { return other.Foo(); } void Start() { StartCoroutine(Get()); }\npublic IEnumerator Foo() { yield return null; }", "ほかのビヘイビアのコルーチン（other.Foo")]
        [TestCase("public CoUse other; void Start() { StartCoroutine(other.Foo()); }\npublic IEnumerator Foo() { yield return null; }", "ほかのビヘイビアのコルーチン（other.Foo")]
        // #203：コルーチンに関わる名前を含まないクラスでも、名前で選んだ呼び出しは調べる
        [TestCase("void Start() { var e = new int[2].GetEnumerator(); }\n}\npublic class CoOther : UdonSharpBehaviour { void Start() { StartCoroutine(Foo()); } IEnumerator Foo() { yield return null; } ", "外部のメソッド（Array.GetEnumerator）")]
        [TestCase("public CoPeer other; void Start() { var x = other.Foo(); }\n}\npublic class CoPeer : UdonSharpBehaviour { public IEnumerator Foo() { yield return null; } ", "ほかのビヘイビアのコルーチン（other.Foo")]
        [TestCase("static IEnumerator Foo() { yield return null; } void Start() { StartCoroutine(Foo()); }", "static メソッドのコルーチン")]
        [TestCase("void Start() { static IEnumerator L() { yield return null; } StartCoroutine(L()); }", "static のローカル関数")]
        [TestCase("static void S() { IEnumerator L() { yield return null; } } void Start() { }", "static のローカル関数")]
        [TestCase("void Start() { System.Collections.Generic.IEnumerable<int> L() { yield return 1; } }", "列挙のための yield")]
        [TestCase("int P { get { yield return null; } }", "プロパティ・インデクサーの中の yield")]
        [TestCase("private Coroutine[] _cs = new Coroutine[1]; int I() => 0; void Start() { _cs[I()] = _cs[I()] ?? StartCoroutine(Foo()); }\nIEnumerator Foo() { yield return null; }", "?? ・ ??= の左には")]
        public void OneErrorForUnsupportedForm(string body, string message)
        {
            CompileResult r = Compile(body);
            var errors = r.Diagnostics.Where(d => d.StartsWith("Error")).ToList();
            Assert.That(errors.Count, Is.EqualTo(1), body + "\n" + string.Join("\n", r.Diagnostics));
            Assert.That(errors[0], Does.Contain(message).And.Contain($"CoUse.cs({BodyLine},"), body);
        }

        [Test]
        public void ErrorForCoroutineInPlainClass()
        {
            CompileResult r = Compile("void Start() { var p = new Plain(); }\n}\npublic class Plain { public IEnumerator Foo() { yield return null; } ");
            var errors = r.Diagnostics.Where(d => d.StartsWith("Error")).ToList();
            Assert.That(errors.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(errors[0], Does.Contain("UdonSharpBehaviour でないクラスのメソッドのコルーチン").And.Contain($"CoUse.cs({BodyLine + 2},"));
        }

        [Test]
        public void NoErrorForYieldOutsideUdonSharp()
        {
            // UdonSharp Plus が書き換えないもの（MonoBehaviour のコルーチン・列挙のための yield、UdonSharpBehaviour から使われない普通のクラス）は、
            // 同じアセンブリにあっても調べない
            CompileOk("void Start() { }\n}\n"
                + "public class Mono : MonoBehaviour { IEnumerator Start() { yield return new WaitForEndOfFrame(); } System.Collections.Generic.IEnumerable<int> Nums() { yield return 1; } IEnumerator Run() { IEnumerator e = transform.GetEnumerator(); e.MoveNext(); yield return e; } }\n"
                + "public class Unused { public IEnumerator Foo() { yield return null; } System.Collections.Generic.IEnumerable<int> Nums() { yield return 1; } ");
        }

        [Test]
        public void ErrorForCoroutineInInterface()
        {
            CompileResult r = Compile("void Start() { }\n}\npublic interface ICo { IEnumerator Foo(); }\npublic class CoImpl : UdonSharpBehaviour, ICo { public IEnumerator Foo() { yield return null; } ");
            var errors = r.Diagnostics.Where(d => d.StartsWith("Error")).ToList();
            Assert.That(errors.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(errors[0], Does.Contain("インタフェースのメソッドのコルーチン").And.Contain($"CoUse.cs({BodyLine + 2},"));
        }
    }
}
