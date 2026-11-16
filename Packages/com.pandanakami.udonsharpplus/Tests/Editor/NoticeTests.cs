using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>コンパイル時の注意（Info のログ、Redmine #118）</summary>
    public class NoticeTests
    {
        private const string Head = @"using System; using System.Collections.Generic; using System.Linq; using UdonSharp; using UnityEngine;
public interface IShape { int Area(); }
public class Plain { public Vector3 pos; public int v; public Plain Next() => this; public void Touch() { v++; } public static bool operator ==(Plain a, Plain b) => (object)a == (object)b; public static bool operator !=(Plain a, Plain b) => !(a == b); }
public class Use : UdonSharpBehaviour {
    List<int> list = new List<int>(); Dictionary<string, int> dict = new Dictionary<string, int>(); IShape shape; Plain plain = new Plain(); Dictionary<int, (int, int)> td = new Dictionary<int, (int, int)>(); List<Plain> plains = new List<Plain>();
    void M() { } static void S() { }
    void Start() {
        ";

        private const int UserLine = 8;

        private static CompileResult Compile(string id, params TestSource[] sources)
        {
            UdonSharpPlusSettings.NoticeOverride = r => r.Id == id;
            try
            {
                return UdonSharpTestCompiler.Compile(sources);
            }
            finally
            {
                UdonSharpPlusSettings.NoticeOverride = null;
            }
        }

        private static CompileResult CompileBody(string id, string body) =>
            Compile(id, new TestSource("Use.cs", Head + body + "\n    }\n}"));

        private static string[] Notices(CompileResult r, string id) => r.Logs.Where(l => l.Contains(": " + id + ": ")).ToArray();

        private static string Dump(CompileResult r) => string.Join("\n", r.Diagnostics.Concat(r.Logs));

        [TestCase("USP001", "var q = list.Where(x => x > 1);")]
        [TestCase("USP001", "var q = list.ToList(); q.Clear(); var p = list.Select(x => x).OrderBy(x => x);")]
        [TestCase("USP002", "bool b = list.Select(x => x * 2).Concat(list).Any();")]
        [TestCase("USP003", "foreach (int x in list) list.Add(x);")]
        [TestCase("USP003", "foreach (string k in dict.Keys) { dict[k] = 1; }")]
        [TestCase("USP003", "foreach (var kv in dict) dict.Remove(kv.Key);")]
        [TestCase("USP004", "foreach (var kv in dict) { }")]
        [TestCase("USP004", "int n = dict.Count(kv => kv.Value > 0);")]
        [TestCase("USP005", "bool b = shape != null;")]
        [TestCase("USP005", "bool b = null == shape;")]
        [TestCase("USP005", "bool b = shape is null;")]
        [TestCase("USP006", "plain.pos.Normalize();")]
        [TestCase("USP006", "plain.pos.Set(1, 2, 3);")]
        [TestCase("USP007", "Action a = M;")]
        [TestCase("USP007", "Action a = () => M();")]
        [TestCase("USP007", "void Local() { } Action a = Local;")]
        [TestCase("USP008", "Action a = S; a -= S;")]
        [TestCase("USP009", "td.TryGetValue(1, out var t);")]
        [TestCase("USP010", "bool b = plains.Contains(plain);")]
        [TestCase("USP011", "int f = list.First();")]
        [TestCase("USP011", "int s = list.Aggregate((p, q) => p + q);")]
        [TestCase("USP012", "int n = shape.Area();")]
        [TestCase("USP013", "plain.Next().Touch();")]
        [TestCase("USP014", "Func<int, int> f = x => x;")]
        [TestCase("USP016", "int k = 1; Func<int> f = () => k;")]
        [TestCase("USP016", "Action a = M;")]
        public void Reports(string id, string body)
        {
            CompileResult r = CompileBody(id, body);
            string[] found = Notices(r, id);
            Assert.That(found.Length, Is.EqualTo(1), body + "\n" + Dump(r));
            Assert.That(found[0], Does.StartWith("Log: [UdonSharpPlus] <a href=\"Assets/UdonSharpPlusTests/Use.cs\" line=\"" + UserLine + "\">Assets/UdonSharpPlusTests/Use.cs(" + UserLine + ","));
            Assert.That(found[0], Does.Contain("Project Settings > Udon Sharp Plus"));
        }

        [TestCase("USP001", "var q = list.Where(x => x > 1).ToList(); int c = list.Where(x => x > 1).Count(); foreach (int x in list.Where(y => y > 0)) { }")]
        [TestCase("USP002", "bool b = list.Select(x => x * 2).Any(); bool c = list.Concat(list).Any();")]
        [TestCase("USP003", "foreach (int x in list) { var other = new List<int>(); other.Add(x); } foreach (int x in list) { int y = list[0]; }")]
        [TestCase("USP004", "foreach (int x in list) { } int n = list.Count(x => x > 0);")]
        [TestCase("USP005", "bool b = plain != null; bool c = list != null;")]
        [TestCase("USP006", "float m = plain.pos.magnitude; string s = plain.pos.ToString(); Vector3 v = Vector3.zero; v.Normalize();")]
        [TestCase("USP007", "int c = list.Count(x => x > 1); list.ForEach(x => M()); list.Sort((a, b) => a - b); SendCustomEvent(nameof(M));")]
        [TestCase("USP008", "Action a = S; a += S;")]
        [TestCase("USP009", "dict.TryGetValue(\"a\", out int v);")]
        [TestCase("USP010", "bool b = list.Contains(1);")]
        [TestCase("USP011", "int f = list.FirstOrDefault(); int n = list.Count();")]
        [TestCase("USP012", "IShape s = shape;")]
        [TestCase("USP013", "plain.Touch(); plain.Next();")]
        [TestCase("USP014", "int n = list.Count(x => x > 0);")]
        [TestCase("USP016", "int n = list.Count(x => x > 0);")]
        public void DoesNotReport(string id, string body)
        {
            CompileResult r = CompileBody(id, body);
            Assert.That(Notices(r, id), Is.Empty, body + "\n" + Dump(r));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, Dump(r));
        }

        [Test]
        public void SameNoticeIsReportedOnceAtFirstUse()
        {
            CompileResult r = Compile("USP001",
                new TestSource("A.cs", "using System.Collections.Generic; using System.Linq; using UdonSharp;\npublic class A : UdonSharpBehaviour { List<int> l = new List<int>();\n    void Start() {\n        var q = l.Where(x => x > 0);\n        var p = l.Select(x => x);\n    } }"),
                new TestSource("B.cs", "using System.Collections.Generic; using System.Linq; using UdonSharp;\npublic class B : UdonSharpBehaviour { List<int> l = new List<int>(); void Start() { var q = l.Where(x => x > 0); } }"));
            string[] found = Notices(r, "USP001");
            Assert.That(found.Length, Is.EqualTo(1), Dump(r));
            Assert.That(found[0], Does.Contain("Assets/UdonSharpPlusTests/A.cs(4,"));
        }

        [Test]
        public void UnusedPlainClassIsNotReported()
        {
            const string decl = "using System.Collections.Generic; using System.Linq;\npublic class Helper { public int F() { var l = new List<int>(); var q = l.Where(x => x > 0); return 0; } }";
            CompileResult unused = Compile("USP001", new TestSource("Helper.cs", decl),
                new TestSource("Use.cs", "using UdonSharp;\npublic class Use : UdonSharpBehaviour { void Start() { } }"));
            Assert.That(Notices(unused, "USP001"), Is.Empty, Dump(unused));

            CompileResult used = Compile("USP001", new TestSource("Helper.cs", decl),
                new TestSource("Use.cs", "using UdonSharp;\npublic class Use : UdonSharpBehaviour { void Start() { int n = new Helper().F(); } }"));
            string[] found = Notices(used, "USP001");
            Assert.That(found.Length, Is.EqualTo(1), Dump(used));
            Assert.That(found[0], Does.Contain("Helper.cs(2,"));
        }

        [Test]
        public void NothingIsReportedWhenThereAreErrors()
        {
            CompileResult r = CompileBody("USP001", "var q = list.Where(x => x > 1); var bad = list.ToLookup(x => x);");
            Assert.That(r.Diagnostics.Any(d => d.StartsWith("Error")), Dump(r));
            Assert.That(Notices(r, "USP001"), Is.Empty, Dump(r));
        }

        [Test]
        public void RewrittenFileColumnNotice()
        {
            CompileResult r = CompileBody("USP015", "var q = list.Where(x => x > 1).ToList();");
            string[] found = Notices(r, "USP015");
            Assert.That(found.Length, Is.EqualTo(1), Dump(r));
            Assert.That(found[0], Does.Contain("Assets/UdonSharpPlusTests/Use.cs("));
        }

        [Test]
        public void DisabledNoticesAreNotReported()
        {
            UdonSharpPlusSettings.NoticeOverride = _ => false;
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Use.cs", Head + "var q = list.Where(x => x > 1); bool b = shape != null;\n    }\n}"));
                Assert.That(r.Logs.Where(l => l.Contains(": USP")), Is.Empty, Dump(r));
            }
            finally
            {
                UdonSharpPlusSettings.NoticeOverride = null;
            }
        }

        [Test]
        public void DefaultOnNotices()
        {
            Assert.That(UdonSharpPlus.Notices.Rules.Where(r => r.DefaultOn).Select(r => r.Id), Is.EqualTo(new[] { "USP001", "USP003", "USP005", "USP007" }));
            Assert.That(UdonSharpPlus.Notices.Rules.Select(r => r.Id).Distinct().Count(), Is.EqualTo(UdonSharpPlus.Notices.Rules.Length));
        }

        /// <summary>設定ページの「?」のリンク先（notices.md#usp001）に、各 ID の見出しがあること（#152）</summary>
        [Test]
        public void EveryNoticeHasAHeadingInTheDocument()
        {
            string path = System.IO.Path.Combine("Packages", "com.pandanakami.udonsharpplus", "Documentation~", "notices.md");
            string text = System.IO.File.ReadAllText(path);
            foreach (var rule in UdonSharpPlus.Notices.Rules)
            {
                Assert.That(text, Does.Contain("\n### " + rule.Id + "\n"), rule.Id);
                Assert.That(UdonSharpPlus.UdonSharpPlusSettings.NoticeDocUrl(rule.Id), Does.EndWith("/notices.md#" + rule.Id.ToLowerInvariant()));
            }
        }
    }
}
