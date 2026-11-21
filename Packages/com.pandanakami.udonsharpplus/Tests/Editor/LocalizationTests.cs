using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 文言の言語（#67）。ほかのテストは日本語に固定しているので、ここで英語を確かめる
    /// </summary>
    public class LocalizationTests
    {
        private static readonly Regex Japanese = new Regex(@"[぀-ヿ一-鿿　-〿＀-￯]");

        private static CompileResult Compile(bool japanese, params TestSource[] sources)
        {
            UdonSharpPlusSettings.JapaneseOverride = japanese;
            try
            {
                return UdonSharpTestCompiler.Compile(sources);
            }
            finally
            {
                UdonSharpPlusSettings.JapaneseOverride = null;
            }
        }

        private static string Dump(CompileResult r) => string.Join("\n", r.Diagnostics.Concat(r.Logs));

        private static string[] Ours(CompileResult r) => r.Diagnostics.Concat(r.Logs).Where(d => d.Contains("[UdonSharpPlus]")).ToArray();

        [Test]
        public void ErrorsFollowTheLanguage()
        {
            var decl = new TestSource("Decl.cs", "public class Bad { static Bad() { } }");
            var use = new TestSource("Use.cs", "using UdonSharp;\npublic class Use : UdonSharpBehaviour { void Start() { var b = new Bad(); } }");

            CompileResult en = Compile(false, decl, use);
            Assert.That(en.Diagnostics.Any(d => d.Contains("[UdonSharpPlus] 'Bad' : Static constructors are not supported")), Dump(en));
            CompileResult ja = Compile(true, decl, use);
            Assert.That(ja.Diagnostics.Any(d => d.Contains("[UdonSharpPlus] 'Bad' : static コンストラクタは未対応です")), Dump(ja));
        }

        [Test]
        public void SourceMapDescriptionIsEnglish()
        {
            CompileResult r = Compile(false,
                new TestSource("Decl.cs", "public class Bad\n{\n    public static Bad operator +(Bad a, Bad b) { System.IO.File.Exists(\"x\"); return a; }\n}"),
                new TestSource("Use.cs", "using UdonSharp;\npublic class Use : UdonSharpBehaviour { void Start() { var b = new Bad() + new Bad(); } }"));
            Assert.That(r.Diagnostics.Any(d => d.Contains("Decl.cs(3,") && d.EndsWith(" (in operator + of class Bad)")), Dump(r));
        }

        [TestCase(false, "there are no elements (C#: InvalidOperationException)")]
        [TestCase(true, "要素がありません（C# では InvalidOperationException）")]
        public void RuntimeLogIsEmbeddedInTheLanguageAtCompileTime(bool japanese, string message)
        {
            CompileResult r = Compile(japanese, new TestSource("Use.cs",
                "using System.Collections.Generic; using System.Linq; using UdonSharp;\npublic class Use : UdonSharpBehaviour { List<int> l = new List<int>(); void Start() { int f = l.First(); } }"));
            Assert.That(r.HasErrors, Is.False, Dump(r));
            Assert.That(r["Use"].HeapDefaults.Values.Any(v => v != null && v.Contains(message)), string.Join("\n", r["Use"].HeapDefaults.Values));
        }

        [Test]
        public void NoticeIsEnglish()
        {
            UdonSharpPlusSettings.NoticeOverride = rule => rule.Id == "USP001";
            try
            {
                CompileResult r = Compile(false, new TestSource("Use.cs",
                    "using System.Collections.Generic; using System.Linq; using UdonSharp;\npublic class Use : UdonSharpBehaviour { List<int> l = new List<int>(); void Start() { var q = l.Where(x => x > 0); } }"));
                string notice = r.Logs.Single(l => l.Contains(": USP001: "));
                Assert.That(notice, Does.Contain(": USP001: A LINQ result is kept in a variable."));
                Assert.That(notice, Does.EndWith("(see collections.md; you can turn this off in Project Settings > Udon Sharp Plus)"));
                Assert.That(Japanese.IsMatch(notice), Is.False, notice);
            }
            finally
            {
                UdonSharpPlusSettings.NoticeOverride = null;
            }
        }

        [Test]
        public void AllNoticesHaveBothLanguages()
        {
            foreach (Notices.Rule rule in Notices.Rules)
            {
                UdonSharpPlusSettings.JapaneseOverride = false;
                string en = rule.Title + rule.Message + rule.Doc;
                UdonSharpPlusSettings.JapaneseOverride = true;
                string ja = rule.Title + rule.Message;
                UdonSharpPlusSettings.JapaneseOverride = null;
                Assert.That(Japanese.IsMatch(en), Is.False, rule.Id + ": " + en);
                Assert.That(Japanese.IsMatch(ja), Is.True, rule.Id + ": " + ja);
            }
        }

        /// <summary>いろいろな段のエラーが、英語のときに日本語を含まない</summary>
        [TestCase("public class P { public static P operator true(P p) => p; public static P operator false(P p) => p; }", "var p = new P();")]
        [TestCase("public class P { public event System.Action E; }", "var p = new P();")]
        [TestCase("public class P { }", "var t = (1, 2); t.Item1 = 3;")]
        [TestCase("public class P { }", "var a = new { A = 1 }; object o = a;")]
        [TestCase("public class P { }", "var l = new System.Collections.Generic.List<int>(); var g = l.ToLookup(x => x);")]
        [TestCase("public class P { }", "System.Action a = null; a?.Invoke();")]
        [TestCase("public class P { }", "var d = new System.Collections.Generic.Dictionary<(int, int), int>();")]
        [TestCase("public interface I { int X { get; } }", "I i = null;")]
        [TestCase("public class G<T> { public T v; }", "var g = typeof(G<>);")]
        // #205：0.1.5 で足したコルーチン（#107・#174）と switch（#198・#202）のエラー
        [TestCase("public class P { }", "StartCoroutine(W()); System.Collections.IEnumerator W() { yield return new UnityEngine.WaitForEndOfFrame(); }")]
        [TestCase("public class P { }", "static System.Collections.IEnumerator L() { yield return null; } StartCoroutine(L());")]
        [TestCase("public class P { }", "StartCoroutine(C()); System.Collections.IEnumerator C() { StartCoroutine(C()); yield return null; }")]
        [TestCase("public class P { }", "System.Collections.IEnumerator e = new int[2].GetEnumerator(); StartCoroutine(e);")]
        [TestCase("public class P { }", "object o = 1; switch (o) { case int i: break; }")]
        [TestCase("public class P { }", "int n = 1; int m = 1 + (n.GetHashCode() switch { 1 => 2, _ => 3 });")]
        public void EnglishErrorsHaveNoJapanese(string declarations, string body)
        {
            CompileResult r = Compile(false,
                new TestSource("Decl.cs", "using UdonSharp;\n" + declarations),
                new TestSource("Use.cs", "using System.Linq; using UdonSharp;\npublic class Use : UdonSharpBehaviour { void Start() { " + body + " } }"));
            string[] ours = Ours(r);
            Assert.That(ours, Is.Not.Empty, Dump(r));
            foreach (string d in ours)
                Assert.That(Japanese.IsMatch(d), Is.False, d);
        }
    }
}
