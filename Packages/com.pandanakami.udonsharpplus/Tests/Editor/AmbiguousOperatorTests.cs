using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 演算子の段の、曖昧な演算子・変換のエラー（CS0034・CS0035・CS0457）が、ファイル全体の GetDiagnostics で探していたとき
    /// （#165 より前）と同じものが同じ順に出ること。今は、書き換えの問い合わせで曖昧さの印を見つけたファイルだけを調べる
    /// </summary>
    public class AmbiguousOperatorTests
    {
        private const string Decl = @"
using System; using System.Linq; using System.Collections.Generic;
public class A { public static implicit operator B(A a) => null; public static A operator +(A a, B b) => a; public static A operator -(A a) => a; public static A operator ++(A a) => a; public int this[B b] => 0; }
public class B { public static implicit operator B(A a) => null; public static A operator +(A a, B b) => a; public A Prop { get; set; } public void Add(B b) { } public System.Collections.IEnumerator GetEnumerator() => null; }
public class D : A { }
public class T { public static bool operator true(T t) => true; public static bool operator false(T t) => false; public static T operator &(T a, T b) => a; }
public class N1 { public static implicit operator int(N1 n) => 0; public static implicit operator long(N1 n) => 0; }";

        // 1 つずつ別のファイルにする（曖昧さの印は、ファイルごとに見つける）
        private static readonly string[] Bodies =
        {
            "B b = new A();",
            "A a = null; foreach (B b in new A[0]) { }",
            "A a = null; B b = a ?? (B)null;",
            "A a = null; B b = true ? a : null;",
            "A a = null; var bs = new List<B> { a };",
            "A a = null; var x = new B { Prop = a };",
            "A a = null; A q = null; int n = q[a];",
            "A a = null; B b = null; b = b ?? a;",
            "A a = null; a = a + a;",
            "A a = null; a++;",
            "A a = null; B b = null; var c = a + b;",
            "ulong u = 1; int i = 1; var c = u + i;",
            "ulong u = 1; int i = 1; bool c = u > i;",
            "ulong u = 1; int i = 1; u += i;",
            "A a = null; B b = null; a += b;",
            "N1 n = null; long l = n; var m = n + 1; double d = n;",
            "Func<A, B> f = x => x;",
            "A a = null; Func<B> f = delegate { return a; };",
            "A a = null; Action<B> g = null; g(a);",
            "A a = null; B[] arr = { a }; arr[0] = a;",
            "A a = null; B b = (B)(object)a; B c = (B)a;",
            "A a = null; var t = (x: (B)a, y: 1);",
            "A a = null; switch (a) { case B bb: break; }",
            "A a = null; Take(a);",
            "A a = null; var s = $\"{a + (B)null}\";",
            "A a = null; B Local() => a;",
            "A a = null; var q = new[] { a }.Select(e => (B)e).ToList();",
            "A a = null; var q = new[] { a }.Select(e => { B r = e; return r; }).ToList();",
            "A a = null; for (B b = a; b != null; ) { } while ((B)a != null) { }",
            "A a = null; B b = checked(a); object o = (B)a;",
            "D d = null; B b = d;",
            "T t = null; var r = t && (T)(object)(new A() + new B());",
            "A a = null; var y = a++ + (B)a;",
            "int ok = 1 + 2; var v = new A() + new B();",
            "var nothing = 1 + 2;",
        };

        private static readonly string[] Members =
        {
            "B f = new A();",
            "B Prop => new A();",
            "static B Ret() { return new A(); }",
            "void M(B b = null) { } void N() { M(new A()); }",
            "public P() : this(new A()) { } public P(B b) { }",
            "A a; B this[int i] => a;",
            "event Action<B> E; void M() { E?.Invoke(new A()); }",
            "public static bool operator true(Q7 q) => true; public static bool operator false(Q7 q) => false;",
        };

        private static readonly HashSet<string> Ids = new HashSet<string> { "CS0034", "CS0035", "CS0457" };

        [Test]
        public void SameErrorsAsWholeFileDiagnostics()
        {
            var sources = new List<string> { Decl };
            sources.AddRange(Bodies.Select((b, i) => $"public class P{i} {{ static void Take(B b) {{ }} void M() {{ {b} }} }}"));
            sources.AddRange(Members.Select((m, i) => $"public class Q{i} {{ {m} }}".Replace("public P(", $"public Q{i}(")));
            List<SyntaxTree> trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToList();
            var references = (IEnumerable<MetadataReference>)UdonSharpInternals.GetMetadataReferences.Invoke(null, null);

            // 前の作り：ファイルごとに、宣言の検査のエラーのうしろに、ファイル全体の GetDiagnostics の曖昧さのエラーを並べる
            var compilation = CSharpCompilation.Create("AmbiguityProbe", trees, references);
            var expected = new List<string>();
            foreach (SyntaxTree tree in trees)
            {
                expected.AddRange(compilation.GetSemanticModel(tree).GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error && Ids.Contains(d.Id))
                    .Select(d => Describe(trees, d.Location, $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")));
            }
            Assert.That(expected.Count(e => e.Contains("CS0457")), Is.GreaterThan(25));
            Assert.That(expected.Count(e => e.Contains("CS0034")), Is.GreaterThan(5));

            var diagnostics = new InterfaceLowering.LoweringDiagnostics();
            lock (InterfaceLowering.ProcessLock)
            {
                Compilations.Clear();
                try
                {
                    OperatorLowering.Run(trees, diagnostics, references);
                }
                finally
                {
                    Compilations.Clear();
                }
            }
            var errors = diagnostics.Errors.ToList();
            List<string> actual = errors.Where(e => Ids.Any(id => e.Message.StartsWith(id + ":"))).Select(e => Describe(trees, e.Location, e.Message)).ToList();
            Assert.That(actual, Is.EqualTo(expected));

            // 宣言の検査のエラー（最後のファイル Q7 の true 演算子）は、前のファイルの曖昧さのエラーのうしろに並ぶ（前の作りと同じ順）
            int last = errors.FindLastIndex(e => e.Location.SourceTree != trees[trees.Count - 1] && e.Message.StartsWith("CS0"));
            int trueOperator = errors.FindIndex(e => e.Location.SourceTree == trees[trees.Count - 1] && !e.Message.StartsWith("CS0"));
            Assert.That(trueOperator, Is.GreaterThan(last));
            Assert.That(last, Is.GreaterThan(25));
        }

        private static string Describe(List<SyntaxTree> trees, Location location, string message) =>
            "#" + trees.IndexOf(location.SourceTree) + " " + location.SourceSpan + " " + message;
    }
}
