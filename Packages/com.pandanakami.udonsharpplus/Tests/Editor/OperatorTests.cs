using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// ユーザー定義の演算子・変換（#105）：宣言を名前の付いた static メソッドに、使う側をその呼び出しに書き換える。
    /// 実行時の値は、FeatureTests のパネル「#105 operators」（OperatorRunner）で普通の C# と比べる
    /// </summary>
    public class OperatorTests
    {
        private const string Types = @"using UdonSharp;

public class Vec
{
    public int x, y;
    public Vec(int x, int y) { this.x = x; this.y = y; }
    public static Vec operator +(Vec a, Vec b) => new Vec(a.x + b.x, a.y + b.y);
    public static Vec operator -(Vec a, Vec b) => new Vec(a.x - b.x, a.y - b.y);
    public static Vec operator -(Vec a) => new Vec(-a.x, -a.y);
    public static Vec operator *(Vec a, int k) => new Vec(a.x * k, a.y * k);
    public static Vec operator *(int k, Vec a) => a * k;
    public static Vec operator ++(Vec a) => new Vec(a.x + 1, a.y + 1);
    public static Vec operator --(Vec a) => new Vec(a.x - 1, a.y - 1);
    public static bool operator ==(Vec a, Vec b) { if ((object)a == null || (object)b == null) return (object)a == (object)b; return a.x == b.x && a.y == b.y; }
    public static bool operator !=(Vec a, Vec b) => !(a == b);
    public static bool operator <(Vec a, Vec b) => a.x < b.x;
    public static bool operator >(Vec a, Vec b) => a.x > b.x;
    public static implicit operator Vec(int v) => new Vec(v, v);
    public static explicit operator int(Vec v) => v.x + v.y;
    public static explicit operator float(Vec v) => v.x * 0.5f;
}

public class GVec<T>
{
    public T v;
    public GVec(T v) { this.v = v; }
    public static GVec<T> operator +(GVec<T> a, GVec<T> b) => b;
    public static implicit operator T(GVec<T> g) => g.v;
}

public static class VecUtil
{
    public static Vec Sum(Vec[] all) { Vec s = 0; foreach (Vec v in all) s += v; return s; }
}

public class OpPeer : UdonSharpBehaviour
{
    public int v = 3;
    public static int operator +(OpPeer a, int b) => a.v + b;
    public static int operator -(OpPeer a) => -a.v;
    public static explicit operator int(OpPeer p) => p.v;
    public static implicit operator float(OpPeer p) => p.v;
}
";

        private const string Head = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

public class OpUse : UdonSharpBehaviour
{
    public OpPeer peer;
    private Vec _field = new Vec(1, 2);
    private Vec[] _arr = { new Vec(1, 1), new Vec(2, 2) };
    private Vec Twice(Vec v) => v * 2;
    private int Take(Vec v) => v.x;

    private void Start()
    {
        var a = new Vec(1, 2); var b = new Vec(3, 4); var list = new List<Vec> { a, b };
        ";

        private const int UserLine = 18;

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("OpTypes.cs", Types), new TestSource("OpUse.cs", Head + body + "\n    }\n}"));

        [TestCase("var c = a + b; var d = a - b; var e = -a; var f = a * 3; var g = 3 * a; bool h = a == b; bool i = a != null; bool j = a < b || a > b;")]
        [TestCase("Vec v = 5; int n = (int)a; float f = (float)a; long l = (int)a; int m = Take(7); Vec w = Twice(3);")]
        [TestCase("a += b; a -= b; a *= 2; ++a; a++; --a; a--; _field += a; _arr[0] += a; _arr[1]++; list[0] += b;")]
        [TestCase("var c = ++a; var d = (a += b); for (Vec v = 0; v < 3; v++) { } Vec s = VecUtil.Sum(_arr);")]
        [TestCase("var sum = list.Aggregate((p, q) => p + q); var big = list.Where(v => v > a).ToList(); Func<Vec, Vec> neg = v => -v; var n = neg(a);")]
        [TestCase("var g = new GVec<int>(1) + new GVec<int>(2); int n = g; var gs = new GVec<string>(\"s\"); string s = gs;")]
        [TestCase("int p = peer + 1; int q = -peer; int r = (int)peer; float f = peer;")]
        [TestCase("var t = (a, 1); bool same = t == (b, 1); var anon = new { V = a + b }; int x = anon.V.x;")]
        [TestCase("var c = a\n            + b\n            + 1; bool e = c\n            == a;")]
        // #105 の追加テスト：foreach の要素の変換（式に現れない）、三項・初期化子・Dictionary・補間文字列の中の変換、ラムダの中の複合代入・++
        [TestCase("foreach (Vec v in new int[] { 1, 2 }) a += v; foreach (int n in _arr) { int m = n; } foreach (float f in list)\n            a += 1;")]
        [TestCase("bool flag = a < b; Vec c = flag ? 1 : 2; Vec d = flag ? a : 3; Vec[] arr = { 1, a }; var l = new List<Vec> { 1, a }; l.Add(2); l[0] = 3; var dic = new Dictionary<string, Vec> { { \"k\", 5 } }; dic[\"k\"] += a; dic[\"k\"]++; string s = $\"{a + b}\";")]
        [TestCase("Action inc = () => a++; Action step = () => { a++; _field += a; }; Func<Vec> pre = () => ++a; inc(); step(); for (;; a++, b--) { break; }")]
        public void Compiles(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
        }

        [TestCase("Vec c = a; var d = c++;", "後置（x++）で書いて値を使うのは未対応です")]
        [TestCase("_arr[Take(a)] += b;", "受け側に、メソッドの呼び出しなどを書くのは未対応です")]
        [TestCase("Vec c = a; ++_arr[Take(c)];", "受け側に、メソッドの呼び出しなどを書くのは未対応です")]
        [TestCase("Take(a++);", "後置（x++）で書いて値を使うのは未対応です")]
        [TestCase("if (a++ == b) { }", "後置（x++）で書いて値を使うのは未対応です")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains($"OpUse.cs({UserLine},")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        [TestCase("public class Flag { public bool on; public static bool operator true(Flag f) => f.on; public static bool operator false(Flag f) => !f.on; }",
            "'Flag' : true / false 演算子は未対応です")]
        [TestCase("public class SB : UdonSharpBehaviour { public int v; public static bool operator ==(SB a, SB b) => true; public static bool operator !=(SB a, SB b) => false; }",
            "'SB' : UdonSharpBehaviour の == / != のオーバーロードは未対応です")]
        [TestCase("public class SB : UdonSharpBehaviour { public int v; public static explicit operator SB(int v) => null; }",
            "'SB' : UdonSharpBehaviour の型への変換演算子は未対応です")]
        [TestCase("public class Bits { public int v; public static Bits operator &(Bits a, Bits b) => a; public static bool operator true(Bits f) => true; public static bool operator false(Bits f) => false; }",
            "true / false 演算子は未対応です")]
        public void ReportsUnsupportedDeclaration(string declaration, string message)
        {
            // 普通のクラスのエラーは、UdonSharpBehaviour から使うときだけ出る（ほかの未対応の書き方と同じ）ので、使う側を置く
            string type = declaration.Split(' ')[2];
            string use = type == "SB" ? "" : $"var x = new {type}();";
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "using UdonSharp;\n" + declaration),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { " + use + " } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains("Decl.cs(2,")),
                declaration + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        [Test]
        public void ShortCircuitWithUserOperatorIsError()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "using UdonSharp;\npublic class Bits { public int v; public static Bits operator &(Bits a, Bits b) => a; public static bool operator true(Bits f) => true; public static bool operator false(Bits f) => false; }"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { var a = new Bits(); var b = a && a; } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("ユーザー定義の演算子を使う && は未対応です") && d.Contains("DeclUse.cs(2,")), string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void OperatorInUdonSharpBehaviourDoesNotCrash()
        {
            // 以前は、UdonSharpBehaviour に演算子を宣言しただけで U# の内部の例外（InvalidCastException）になっていた
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("OpOnly.cs", "using UdonSharp;\npublic class OpOnly : UdonSharpBehaviour { public int v; public static int operator +(OpOnly a, int b) => a.v + b; void Start() { int n = this + 1; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void ErrorInsideOperatorPointsToDeclaration()
        {
            // 演算子の中のコンパイルエラーは、元の宣言の位置に「（クラス Bad の演算子 + の中）」を付けて出る
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "public class Bad\n{\n    public static Bad operator +(Bad a, Bad b) { System.IO.File.Exists(\"x\"); return a; }\n}"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { var b = new Bad() + new Bad(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("Decl.cs(3,") && d.Contains("（クラス Bad の演算子 + の中）")), string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void PlainClassFieldArrayInitializerShorthand()
        {
            // 普通のクラスのフィールドの、配列の初期化子の省略形（#105 のパネルを作っていて見つけた既存の不具合。以前は C# の構文エラー）
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "public class Trail { public int[] n = { 1, 2 }; public string[] s = { \"a\" }; public Trail[] t = { null }; }"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { var t = new Trail(); int k = t.n[1] + t.s.Length + t.t.Length; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void UnusedPlainClassDeclarationErrorIsDropped()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "using UdonSharp;\npublic class Flag { public bool on; public static bool operator true(Flag f) => f.on; public static bool operator false(Flag f) => !f.on; }"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        /// <summary>#105 の追加テスト：いろいろな置き方の宣言・使い方が、エラーにならない</summary>
        [TestCase("namespace N1 { public class V { public int x; public static V operator +(V a, V b) => a; public static V operator +(V a, N2.V b) => a; public static V operator +(V a, int[] b) => a; } }\nnamespace N2 { public class V { public int x; public static V operator +(V a, V b) => b; } }",
            "var a = new N1.V() + new N1.V(); var b = new N2.V() + new N2.V(); var c = new N1.V() + new N2.V(); var d = a + new int[1];")]
        [TestCase("public class G<T> { public T v; public G(T v) { this.v = v; } public static G<T> operator +(G<T> a, G<T> b) => b; public static implicit operator T(G<T> g) => g.v; }",
            "var gi = new G<int>(1) + new G<int>(2); var gf = new G<float>(1f) + new G<float>(2f); int i = gi; float f = gf;")]
        [TestCase("public class V { public int x; public static V operator +(V a, V b) => a;\n#if NOPE\n    public static V operator -(V a) => a;\n#else\n    public static V operator -(V a, V b) => b;\n#endif\n}",
            "var a = new V();\n#if NOPE\n        var z = -a;\n#endif\n        var m = a - a + a;")]
        public void DeclarationsCompile(string declarations, string body)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "using UdonSharp;\n" + declarations),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour\n{\n    void Start()\n    {\n        " + body + "\n    }\n}"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void PartialClassAcrossFilesUsedByTwoBehaviours()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("P1.cs", "public partial class P { public int x; public static P operator +(P a, P b) { var r = new P(); r.x = a.x + b.x; return r; } }"),
                new TestSource("P2.cs", "public partial class P { public static P operator -(P a) => a; public P Dbl() => this + this; }"),
                new TestSource("UseA.cs", "using UdonSharp;\npublic class UseA : UdonSharpBehaviour { void Start() { var p = new P() + new P(); p = -p; p = p.Dbl(); } }"),
                new TestSource("UseB.cs", "using UdonSharp;\npublic class UseB : UdonSharpBehaviour { void Start() { var p = new P(); p += p; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void OperatorOfBaseBehaviourUsedFromDerived()
        {
            // 継承した UdonSharpBehaviour から、基底の演算子を使う（#111 の名前の付け直しの対象にならない）
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("BaseB.cs", "using UdonSharp;\npublic class BaseB : UdonSharpBehaviour { public int v; public static int operator +(BaseB a, int b) => a.v + b; }"),
                new TestSource("DerB.cs", "using UdonSharp;\npublic class DerB : BaseB { void Start() { int n = this + 1; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void BehaviourNullCheckStaysUnityComparison()
        {
            // UdonSharpBehaviour にほかの演算子があっても、== null は書き換えない（破棄したものを null とみなす Unity の比較のまま）
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("SB.cs", "using UdonSharp;\npublic class SB : UdonSharpBehaviour { public SB o; public static int operator +(SB a, int b) => b; void Start() { bool x = o == null; int n = o + 1; } }"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            Assert.That(r["SB"].Uasm, Does.Contain("op_Addition__SB__int"));
            Assert.That(r["SB"].Uasm, Does.Contain("UnityEngineObject.__op_Equality__UnityEngineObject_UnityEngineObject__SystemBoolean"));
            Assert.That(r["SB"].Uasm, Does.Not.Contain("op_Equality__SB"));
        }

        [Test]
        public void AmbiguousConversionIsError()
        {
            // 普通のクラスは object[] に書き換えるので、U# のコンパイルでは型が合ってしまう。C# と同じ曖昧さのエラーを出す
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "public class A { public static implicit operator B(A a) => null; }\npublic class B { public static implicit operator B(A a) => null; }"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { B b = new A(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("CS0457") && d.Contains("DeclUse.cs(2,")), string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void GeneratedNameCollisionIsError()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("Decl.cs", "public class V { public int x; public static V operator +(V a, V b) => a; public static V op_Addition__V__V(V a, V b) => b; }"),
                new TestSource("DeclUse.cs", "using UdonSharp;\npublic class DeclUse : UdonSharpBehaviour { void Start() { var v = new V() + new V(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("メンバーの名前 op_Addition__V__V は、この演算子の書き換えに使う") && d.Contains("Decl.cs(1,")), string.Join("\n", r.Diagnostics));
        }
    }
}
