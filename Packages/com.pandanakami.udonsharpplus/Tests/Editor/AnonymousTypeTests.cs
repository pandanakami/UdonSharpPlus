using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 匿名型（Redmine #70）。書き換え後のソースと、エラーにする書き方を確かめる
    /// （動きそのものは FeatureTests の Anon パネルで確かめる）
    /// </summary>
    public class AnonymousTypeTests
    {
        private const string Other = @"
namespace UdonSharpPlusTests.Anon
{
    public class Other { public int v; }
}";

        private static string User(string body, string members = "") => @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Anon
{
    public class User : UdonSharpBehaviour
    {
        " + members + @"
        private void Start()
        {
            " + body + @"
        }
    }
}";

        private static CompileResult CompileUser(string body, string members = "") =>
            UdonSharpTestCompiler.Compile(new TestSource("Other.cs", Other), new TestSource("User.cs", User(body, members)));

        private const string Features = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class Item
{
    public string Name = ""a"";
    public int Hp = 3;

    public string Describe()
    {
        var p = new { Name, Hp };
        return p.ToString();
    }
}

public class AnonUser : UdonSharpBehaviour
{
    private int _bonus = 2;

    private void Start()
    {
        var p = new { Name = ""a"", Score = 1 };
        var q = new { Name = ""b"", Score = 2 };
        var item = new Item();
        var r = new { item.Name, item.Hp, _bonus };
        var nested = new { A = 1, Inner = new { B = ""x"" } };
        string s = ""x"" + p + $""{q}"" + p.ToString() + nested.Inner.B + r.Hp;
        bool eq = p.Equals(q) || p == q || p != null;
        var list = new List<int> { 1, 2 };
        var proj = list.Select(x => new { N = x, Sq = x * x }).Where(a => a.Sq > 1).ToList();
        var arr = new[] { new { K = ""a"" }, new { K = ""b"" } };
        Func<int, int> f = x => x + p.Score;
        int t = new
        {
            Y = 1,
            Z = 2,
        }.Z;
    }
}";

        /// <summary>匿名型は、メンバーの並びごとの普通のクラスになる。同じ並びは 1 つ。行は変わらない</summary>
        [Test]
        public void CompilesAndKeepsLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusAnonymous"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("AnonUser.cs", Features));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "AnonUser.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(Features.Split('\n').Length), lowered);
                Assert.That(lowered, Does.Not.Contain("new {"), lowered);
                Assert.That(lowered, Does.Not.Contain("new\n"), lowered);
                // p と q は同じ並び（Name・Score）なので同じクラス
                Assert.That(System.Text.RegularExpressions.Regex.Matches(lowered, @"public class __Anon_\d+ \{ public string Name; public int Score;").Count, Is.EqualTo(1), lowered);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        [TestCase("var p = new { A = 1 }; Debug.Log(p);", "匿名型を object・インタフェースとして使うことは未対応です")]
        [TestCase("var p = new { A = 1 }; object o = p;", "匿名型を object・インタフェースとして使うことは未対応です")]
        [TestCase("var p = new { A = 1 }; int h = p.GetHashCode();", "'GetHashCode' : 匿名型の GetHashCode は未対応です")]
        [TestCase("var p = new { A = 1 }; var t = p.GetType();", "'GetType' : 匿名型の GetType は未対応です")]
        [TestCase("var p = new { Item = new Other() }; string s = p.ToString();", "匿名型の ToString は、メンバー 'Item' の型")]
        [TestCase("var p = new { Item = new Other() }; string s = \"x\" + p;", "匿名型の文字列への変換は、メンバー 'Item' の型")]
        [TestCase("System.Func<int, int> f = null; var p = new { f }; bool b = p.Equals(p);", "匿名型の Equals は、メンバー 'f' がデリゲートで")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = CompileUser(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message)), r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics));
        }

        /// <summary>object にする書き方のエラーは、元の行に出る</summary>
        [Test]
        public void ErrorPointsToOriginalLine()
        {
            const string src = @"using UdonSharp;
using UnityEngine;

public class AnonError : UdonSharpBehaviour
{
    private void Start()
    {
        var p = new
        {
            A = 1,
            B = ""x"",
        };
        Debug.Log(p);
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("AnonError.cs", src));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("AnonError.cs(13,"), "Debug.Log の行（13 行目）");
        }

        [TestCase("var r = nums.Select(x => new Pt(x, x * x)).Where(a => a.sq > 3).Select(a => new Pt(a.x, 1)).OrderByDescending(b => b.x).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Where(a => a.sq > 3).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Where(a => a.sq > 3).Select(a => new { a.x, label = a.sq }).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Select(a => new { a.x, label = a.sq }).OrderByDescending(b => b.x).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).OrderByDescending(b => b.x).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Where(a => a.sq > 3).Select(a => new { a.x, label = \"n\" + a.x }).OrderByDescending(b => b.x).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Select(a => new { a.x, label = \"n\" + a.x }).ToList();")]
        [TestCase("var r = nums.Select(x => new { x, sq = x * x }).Select(a => new { a.x, label = a.sq + 1 }).ToList();")]
        public void ProbeLinqChain(string body)
        {
            const string head = @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class Pt { public int x; public int sq; public Pt(int x, int sq) { this.x = x; this.sq = sq; } }

public class ChainProbe : UdonSharpBehaviour
{
    private void Start()
    {
        var nums = new List<int> { 1, 2, 3 };
        ";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("ChainProbe.cs", head + body + "\n    }\n}"));
            Assert.That(r.Diagnostics, Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
        }

        /// <summary>匿名型を、ジェネリックメソッドの型引数にする（推論で T が匿名型になる）</summary>
        [Test]
        public void AnonymousTypeAsGenericMethodArgument()
        {
            const string src = @"using System.Collections.Generic;
using UdonSharp;

public class GenericArg : UdonSharpBehaviour
{
    private static T Id<T>(T x) => x;
    private static List<T> One<T>(T x) { var l = new List<T>(); l.Add(x); return l; }

    private void Start()
    {
        var p = Id(new { A = 1, B = ""b"" });
        var l = One(new { A = 2 });
        int s = p.A + l[0].A;
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("GenericArg.cs", src));
            Assert.That(r.Diagnostics, Is.Empty, string.Join("\n", r.Diagnostics));
        }

        /// <summary>ジェネリッククラスの中の匿名型：型引数ごとのコピーで、メンバーの型が決まる</summary>
        [Test]
        public void AnonymousTypeUsingTypeParameterInGenericClass()
        {
            const string src = @"using UdonSharp;

public class Box<T>
{
    public T value;
    public string Show() => new { value }.ToString();
}

public class GenericAnon : UdonSharpBehaviour
{
    private void Start()
    {
        var a = new Box<int>();
        var b = new Box<string>();
        string s = a.Show() + b.Show();
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("GenericAnon.cs", src));
            Assert.That(r.Diagnostics, Is.Empty, string.Join("\n", r.Diagnostics));
        }

        // ---- #70 の追加テスト ----

        private static string CompileLowered(string name, string source, string[] defines = null, params TestSource[] more)
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusAnon" + name));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                var sources = new System.Collections.Generic.List<TestSource> { new TestSource(name + ".cs", source) };
                sources.AddRange(more);
                CompileResult r = UdonSharpTestCompiler.Compile(defines ?? UdonSharpTestCompiler.EditorDefines, dir, sources.ToArray());
                Assert.That(r.Diagnostics, Is.Empty, string.Join("\n", r.Diagnostics));
                // 書き換えたファイルを、名前の順に全部つなぐ（生成するクラスは、最初に使ったファイルに置かれる）
                string folder = Path.Combine(dir, "Assets", "UdonSharpPlusTests");
                return string.Join("\n/*----*/\n", Directory.GetFiles(folder, "*.cs").OrderBy(f => f, System.StringComparer.Ordinal).Select(File.ReadAllText));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ---- #135・#136：普通のクラスの既存の不具合 ----

        /// <summary>#135：コンストラクタ・メソッドの引数がキーワードと同じ名前</summary>
        [Test]
        public void KeywordParameterNamesInPlainClass()
        {
            const string src = @"using UdonSharp;

public class Kw2 { public int c; public Kw2(int @class) { this.c = @class; } }
public class Kw3 { public int c; public void Set(int @event, string @string) { c = @event + @string.Length; } }

public class KwUse : UdonSharpBehaviour
{
    private void Start()
    {
        var k = new Kw2(1);
        var j = new Kw3();
        j.Set(2, ""ab"");
    }
}";
            string lowered = CompileLowered("KwUse", src);
            Assert.That(lowered, Does.Contain("int @class"), lowered);
            Assert.That(lowered, Does.Contain("int @event"), lowered);
        }

        /// <summary>#136：フィールドの型を global:: つきで書く（グローバル名前空間のインタフェース）</summary>
        [Test]
        public void GlobalAliasedInterfaceFieldInPlainClass()
        {
            const string src = @"using UdonSharp;

public interface IFoo { int A(); }
public class Foo : IFoo { public int A() => 1; }
public class Holder2 { public global::IFoo f; }

public class GUse : UdonSharpBehaviour
{
    private void Start()
    {
        var h = new Holder2();
        h.f = new Foo();
        int v = h.f.A();
    }
}";
            CompileLowered("GUse", src);
        }

        /// <summary>名前・型・並びのどれかが違えば別のクラス。同じなら 1 つ（型の同一性）</summary>
        [Test]
        public void DifferentShapesMakeDifferentClasses()
        {
            const string src = @"using UdonSharp;

public class Shapes : UdonSharpBehaviour
{
    private void Start()
    {
        var a1 = new { A = 1, B = 2 };
        var a2 = new { A = 1, B = 2 };
        var order = new { B = 2, A = 1 };
        var type = new { A = 1L, B = 2 };
        var kind = new { A = 1, B = ""2"" };
        var name = new { a = 1, B = 2 };
        var fewer = new { A = 1 };
    }
}";
            string lowered = CompileLowered("Shapes", src);
            Assert.That(System.Text.RegularExpressions.Regex.Matches(lowered, @"public class __Anon_\d+ \{").Count, Is.EqualTo(6), lowered);
        }

        /// <summary>キーワード・日本語の名前のメンバー、プロパティ・フィールドから省略した名前</summary>
        [Test]
        public void KeywordAndJapaneseMemberNames()
        {
            const string src = @"using UdonSharp;

public class Other
{
    public string Label { get { return ""x""; } }
}

public class Names : UdonSharpBehaviour
{
    private int _f = 1;

    private void Start()
    {
        var o = new Other();
        string @event = ""e"";
        var p = new { @class = 3, 名前 = ""n"", @event, o.Label, this._f };
        string s = p.@class + p.名前 + p.@event + p.Label + p._f + p;
    }
}";
            string lowered = CompileLowered("Names", src);
            Assert.That(lowered, Does.Contain("__kw_class"), lowered);
            Assert.That(lowered, Does.Contain("class = "), "ToString には元の名前");
        }

        /// <summary>プリプロセッサ：有効な側だけから作り、行は変わらない</summary>
        [Test]
        public void PreprocessorKeepsLinesAndUsesActiveSide()
        {
            const string src = @"using UdonSharp;

public class Pp : UdonSharpBehaviour
{
    private void Start()
    {
#region 匿名型
#pragma warning disable 0219
#if UNITY_EDITOR
        var a = new { Src = ""editor"", N = 1 };
#else
        var a = new { Other = 2 };
#endif
#if false
        var b = new { Unused = 3 };
#endif
#pragma warning restore 0219
#endregion
        string s = a.ToString();
    }
}";
            string lowered = CompileLowered("Pp", src);
            Assert.That(lowered.Split('\n').Length, Is.EqualTo(src.Split('\n').Length), lowered);
            Assert.That(lowered, Does.Contain("Src"), lowered);
            // 無効な側のソースは trivia のまま残るが、その匿名型のクラスは作らない
            Assert.That(lowered, Does.Not.Contain("public int Unused"), lowered);
            Assert.That(lowered, Does.Not.Contain("public int Other"), lowered);
        }

        [TestCase("var p = new { T = (1, 2) };", "タプルは未対応です", "Tup.cs(8,")]
        [TestCase("var p = (new { A = 1 }, 2);", "タプルは未対応です", "Tup.cs(8,")]
        public void TuplesAreStillErrors(string body, string message, string location)
        {
            string src = "using UdonSharp;\n\npublic class Tup : UdonSharpBehaviour\n{\n    private void Start()\n    {\n        int before = 0;\n        " + body + "\n    }\n}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Tup.cs", src));
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains(location)), string.Join("\n", r.Diagnostics));
        }

        /// <summary>匿名型のメンバーに、this・普通のクラス・インタフェース・UdonSharpBehaviour・ジェネリッククラス・配列を使ったときの ToString のエラー</summary>
        [TestCase("var p = new { Self = this }; string s = p.ToString();", "匿名型の ToString は、メンバー 'Self' の型")]
        [TestCase("IShape sh = null; var p = new { sh }; string s = p.ToString();", "匿名型の ToString は、メンバー 'sh' の型")]
        [TestCase("var p = new { b = new Box<int>() }; string s = \"\" + p;", "匿名型の文字列への変換は、メンバー 'b' の型")]
        [TestCase("var p = new { arr = new int[1] }; string s = p.ToString();", "匿名型の ToString は、メンバー 'arr' の型")]
        [TestCase("var p = new { list = new List<int>() }; string s = $\"{p}\";", "匿名型の文字列への変換は、メンバー 'list' の型")]
        public void ToStringWithUnsupportedMembers(string body, string message)
        {
            const string types = "using System.Collections.Generic;\nnamespace UdonSharpPlusTests.Anon\n{\n    public interface IShape { int Area(); }\n    public class Box<T> { public T v; }\n}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Types.cs", types), new TestSource("User.cs", User(body)));
            Assert.That(r.Diagnostics.Any(d => d.Contains(message)), r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics));
        }

        /// <summary>メンバーに普通のクラス・インタフェースを使った匿名型の読み出しと Equals は通る</summary>
        [Test]
        public void ReferenceMembersCompile()
        {
            const string src = @"using UdonSharp;

public interface IShape { int Area(); }
public class Sq : IShape { public int Area() => 4; }
public class Holder { public int hp = 1; }

public class Refs : UdonSharpBehaviour
{
    private void Start()
    {
        var h = new Holder();
        IShape sh = new Sq();
        var a = new { h, sh, arr = new int[2] };
        var b = new { h, sh, arr = a.arr };
        bool e = a.Equals(b) && a.h.hp == 1 && a.sh.Area() == 4;
    }
}";
            CompileLowered("Refs", src);
        }

        /// <summary>
        /// 複数のファイル・複数の UdonSharpBehaviour・partial：同じ並びは 1 つのクラス、並びの違うものは別のクラス。
        /// 生成するクラスの番号と置く場所は、同じソースを 2 回コンパイルしても同じ（ファイルの処理の順番で変わらない）
        /// </summary>
        [Test]
        public void MultipleFilesAndBehavioursAreStable()
        {
            const string first = @"using UdonSharp;

public partial class Multi : UdonSharpBehaviour
{
    private void Start()
    {
        var p = new { Name = ""a"", Score = 1 };
        var q = new { Score = 1, Name = ""a"" };
        string s = p.ToString() + q + Other();
    }
}";
            const string part = @"public partial class Multi
{
    private string Other()
    {
        var p = new { Name = ""b"", Score = 2 };
        var r = new { Id = 3 };
        return p.ToString() + r;
    }
}";
            const string second = @"using UdonSharp;

public class Second : UdonSharpBehaviour
{
    private void Start()
    {
        var p = new { Score = 5, Name = ""c"" };
        string s = p.ToString();
    }
}";
            string a1 = CompileLowered("Multi", first, null, new TestSource("MultiPart.cs", part), new TestSource("Second.cs", second));
            string a2 = CompileLowered("Multi", first, null, new TestSource("MultiPart.cs", part), new TestSource("Second.cs", second));
            Assert.That(a2, Is.EqualTo(a1));
            // 並びは 3 種類（Name・Score、Score・Name、Id）。Name・Score と Score・Name は別のクラス
            Assert.That(System.Text.RegularExpressions.Regex.Matches(a1, @"public class __Anon_\d+ \{").Count, Is.EqualTo(3), a1);
            Assert.That(System.Text.RegularExpressions.Regex.Matches(a1, @"public class __Anon_\d+ \{ public string Name; public int Score;").Count, Is.EqualTo(1), a1);
            Assert.That(System.Text.RegularExpressions.Regex.Matches(a1, @"public class __Anon_\d+ \{ public int Score; public string Name;").Count, Is.EqualTo(1), a1);
        }

        /// <summary>ラムダ・普通のクラス・ジェネリクスのコピー・partial・ローカル関数の中のエラーは、元のファイル・元の行に出る</summary>
        [TestCase("Lambda", "Err.cs(11,")]
        [TestCase("Plain", "Types.cs(6,")]
        [TestCase("Generic", "Types.cs(11,")]
        [TestCase("Local", "Err.cs(12,")]
        [TestCase("Partial", "ErrPart.cs(5,")]
        public void ErrorsPointToOriginalLocation(string where, string location)
        {
            string types = "using UdonSharp;\nusing UnityEngine;\n\npublic class Plain\n{\n    public void Run() { var p = new { A = 1 }; " + (where == "Plain" ? "Debug.Log(p);" : "p.ToString();") + " }\n}\n\npublic class Gen<T>\n{\n    public void Run(T v) { var p = new { v }; " + (where == "Generic" ? "Debug.Log(p);" : "p.ToString();") + " }\n}";
            string main = "using System.Collections.Generic;\nusing System.Linq;\nusing UdonSharp;\nusing UnityEngine;\n\npublic partial class Err : UdonSharpBehaviour\n{\n    private void Start()\n    {\n        var nums = new List<int> { 1 };\n"
                + (where == "Lambda" ? "        var q = nums.Select(x => { var p = new { x }; Debug.Log(p); return x; }).ToList();\n" : "        var q = nums.Select(x => new { x }).ToList();\n")
                + (where == "Local" ? "        void F() { var p = new { A = 1 }; Debug.Log(p); }\n        F();\n" : "")
                + "        new Plain().Run();\n        new Gen<int>().Run(1);\n        Other();\n    }\n}";
            string part = "public partial class Err\n{\n    private void Other()\n    {\n        var p = new { A = 1 }; " + (where == "Partial" ? "UnityEngine.Debug.Log(p);" : "p.ToString();") + "\n    }\n}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Types.cs", types), new TestSource("Err.cs", main), new TestSource("ErrPart.cs", part));
            Assert.That(r.Diagnostics.Count(d => d.Contains(location)), Is.GreaterThanOrEqualTo(1), string.Join("\n", r.Diagnostics));
        }
    }
}
