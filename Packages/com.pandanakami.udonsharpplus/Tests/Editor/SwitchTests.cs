using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// パターンのラベルを含む switch 文（#198）：UdonSharp はパターンのラベルを黙って読み飛ばすので、if で決めた番号の switch に書き換える。
    /// 実行時の結果は、FeatureTests のパネル「#198 switch patterns」で普通の Unity と比べる
    /// </summary>
    public class SwitchTests
    {
        private const string Head = "using UdonSharp;\nusing UnityEngine;\n";

        // SwUse.cs の 1〜2 行目は using、3 行目はクラス、4 行目は {。本体は 5 行目から
        private const int BodyLine = 5;

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("SwUse.cs", Head + "public class SwUse : UdonSharpBehaviour\n{\n" + body + "\n}"));

        private static void AssertError(string body, string message, int line)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains($"SwUse.cs({line},")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        private static string Lowered(string name, string body)
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusSwitch_" + name));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("SwUse.cs", Head + "public class SwUse : UdonSharpBehaviour\n{\n" + body + "\n}"));
                Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
                return File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "SwUse.cs"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        private const string Patterns = @"public string Pick(string k, int n)
{
    switch (k)
    {
        case ""b"" when n > 0:
            return ""g"";
        case ""b"":
            return ""b"";
        default:
            return ""d"";
    }
}
public string Range(int n)
{
    switch (n)
    {
        case < 0: return ""neg"";
        case 0 or 1: return ""small"";
        case >= 2 and < 10: return ""mid"";
        case var big when big % 2 == 0: return ""even"" + big;
        case int odd: return ""odd"" + odd;
    }
}";

        [Test]
        public void PatternLabelsBecomeNumberedCases()
        {
            string marked = Lowered("Patterns", Patterns);
            // 条件の前の、元のラベルの位置の印（#205）。ここでは印を除いて形を見る
            Assert.That(marked, Does.Match(@"if \(/\*cus@\d+\*/\(__sw0 == \(""b""\)\) && \(n > 0\)\) __sc0 = 0; else if \(/\*cus@\d+\*/__sw0 == "));
            string code = System.Text.RegularExpressions.Regex.Replace(marked, @"/\*cus@\d+\*/", "");
            Assert.That(code, Does.Contain("var __sw0 = (k); int __sc0 = -1; if ((__sw0 == (\"b\")) && (n > 0)) __sc0 = 0; else if (__sw0 == (\"b\")) __sc0 = 1; switch (__sc0)"));
            Assert.That(code, Does.Contain("if (__sw1 < (0)) __sc1 = 0; else if ((__sw1 == (0)) || (__sw1 == (1))) __sc1 = 1;"));
            Assert.That(code, Does.Contain("else if ((true) && (__sw1 % 2 == 0)) __sc1 = 3;"), "when の中の変数は、switch の式の一時変数を読む");
            Assert.That(code, Does.Contain("case 3: { int big = __sw1;"));
            Assert.That(code, Does.Contain("default: { int odd = __sw1;"), "必ず合うもの（case int odd:）は default");
            Assert.That(code, Does.Not.Contain(" when "));
        }

        [Test]
        public void LinesAreKept()
        {
            // パターンの switch のうしろの行が、元と同じ行に残る
            string body = Patterns + "\npublic int Marker() { return 12345; }";
            string code = Lowered("Lines", body);
            string original = Head + "public class SwUse : UdonSharpBehaviour\n{\n" + body + "\n}";
            int Line(string s, string text) => s.Substring(0, s.IndexOf(text)).Count(c => c == '\n');
            Assert.That(Line(code, "return 12345;"), Is.EqualTo(Line(original, "return 12345;")));
            Assert.That(Line(code, "return \"odd\" + odd;"), Is.EqualTo(Line(original, "return \"odd\" + odd;")));
        }

        [Test]
        public void BreakAndContinueKeepTheirMeaning()
        {
            // 中身は番号の switch に残るので、break は switch を、continue は外のループを抜ける
            Assert.That(Compile(@"public int Sum(int[] a)
{
    int s = 0;
    for (int i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case < 0: continue;
            case 0 when i == 0: break;
            default: s += a[i]; break;
        }
        s++;
    }
    return s;
}").Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty);
        }

        [Test]
        public void CatchAllBecomesDefault()
        {
            // 必ず合う case（var x）がある switch は終わりまで行かない。default も一緒にあっても、default が 2 つにならない
            string code = Lowered("CatchAll", @"public int F(int n)
{
    switch (n)
    {
        case 1 when n > 0: return 1;
        case var x: return x;
        default: return 0;
    }
}");
            Assert.That(code, Does.Contain("default: { int x = __sw0; return x; }"));
            Assert.That(code, Does.Contain("case -2: return 0;"));
        }

        [Test]
        public void PlainSwitchIsUntouched()
        {
            // パターンのない switch は書き換えない（ほかに書き換えるものもないので、書き換え後のファイルは出ない）
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusSwitch_Plain"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("SwUse.cs", Head + "public class SwUse : UdonSharpBehaviour\n{\npublic int F(int n) { switch (n) { case 1: return 2; default: return 0; } }\n}"));
                Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty);
                Assert.That(File.Exists(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "SwUse.cs")), Is.False);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ---- switch 式（#202） ----

        [Test]
        public void SwitchExpressionOnVariableBecomesTernary()
        {
            // 調べる式が引数なら、そのまま何度も読む。パターンの変数・when は、その引数を読む
            string code = Lowered("ExprSimple", "public string F(int n) => n switch { < 0 => \"neg\", 0 => \"zero\", var x when x > 100 => \"big\" + x, _ => \"pos\" };");
            Assert.That(code, Does.Contain("((n) < (0)) ? (string)(\"neg\") : ((n) == (0)) ? (string)(\"zero\") : ((true) && ((n) > 100)) ? (string)(\"big\" + (n)) : (string)(\"pos\"))"));
            Assert.That(code, Does.Not.Contain(" switch "));
        }

        [Test]
        public void SwitchExpressionOnCallUsesTemporary()
        {
            // 調べる式がメソッドの呼び出しなら、文の前で一時変数に入れる（1 回だけ評価する）
            string code = Lowered("ExprHoist", @"private int _c;
int Next() { _c++; return _c; }
public int G() { return Next() switch { 1 => 10, _ => 0 }; }
public int H() { var y = Next() switch { 2 => 20, _ => 0 }; return y; }
public int P => Next() switch { 3 => 30, _ => 0 };");
            Assert.That(code, Does.Contain("{ { var __se0 = (Next()); return ((__se0 == (1)) ? (int)(10) : (int)(0)); }"));
            Assert.That(code, Does.Contain("var __se1 = (Next()); var y = ((__se1 == (2)) ? (int)(20) : (int)(0));"));
            Assert.That(code, Does.Contain("public int P { get { var __se2 = (Next()); return ((__se2 == (3)) ? (int)(30) : (int)(0)); } }"));
        }

        [Test]
        public void SwitchExpressionWithoutCatchAllCallsNoMatch()
        {
            // どの腕にも合わないとき（C# では例外）は、エラーのログを出すメソッドを呼び、返る null の配列の添字を読んで、その場で止める。
            // 呼び出しの前の印で、実行時例外を元の switch 式の位置に出す（#205）
            string code = Lowered("ExprNoMatch", "public int H(int n) => n switch { 1 => 10, 2 => 20 };");
            Assert.That(code, Does.Match(@"\(int\)/\*cus@\d+\*/__sw_nomatch_\d+\(\)\[0\]\)"));
            Assert.That(code, Does.Match(@"private static object\[\] __sw_nomatch_\d+\(\) \{ UnityEngine\.Debug\.LogError\(""[^""]*""\); return null; \}"));
        }

        [Test]
        public void NestedSwitchExpressionAndLines()
        {
            // 腕の中の switch 式も書き換える。腕を並べた行は、元の行に残る
            string body = @"public string N(int a, int b) => a switch
{
    0 => b switch { 0 => ""00"", _ => ""0x"" },
    _ => ""x"",
};
public int Marker() { return 777; }";
            string code = Lowered("ExprNested", body);
            Assert.That(code, Does.Contain("((b) == (0)) ? (string)(\"00\") : (string)(\"0x\")"));
            string original = Head + "public class SwUse : UdonSharpBehaviour\n{\n" + body + "\n}";
            int Line(string s, string text) => s.Substring(0, s.IndexOf(text)).Count(c => c == '\n');
            Assert.That(Line(code, "return 777;"), Is.EqualTo(Line(original, "return 777;")));
            Assert.That(Line(code, "(string)(\"x\")"), Is.EqualTo(Line(original, "_ => \"x\"")));
        }

        [Test]
        public void WhenWithOnlyPatternVariable()
        {
            // when の条件がパターンの変数だけのときも、置き換える
            string code = Lowered("WhenOnly", "public int F(bool f) { switch (f) { case var b when b: return 1; default: return 0; } }");
            Assert.That(code, Does.Contain("(true) && (__sw0)"));
        }

        [TestCase("int Next() => 1; public int K() => 1 + (Next() switch { 1 => 1, _ => 2 });", "調べる式を変数・引数・フィールド・定数にしてください")]
        [TestCase("public int K(object o) => o switch { 1 => 1, _ => 2 };", "の switch 式は未対応です")]
        [TestCase("public int F(object o) { switch (o) { case 1 when o != null: return 1; } return 0; }", "の switch では、パターン・when の付いた case は未対応です")]
        [TestCase("public int F(string s) { switch (s) { case { Length: 0 }: return 1; } return 0; }", "このパターン（プロパティ・位置のパターンなど）は未対応です")]
        [TestCase("public int F(int n) { switch (n) { case long l: return 1; } return 0; }", "switch の式と違う型の型パターン")]
        public void Errors(string body, string message)
        {
            AssertError(body, message, BodyLine);
        }

        /// <summary>
        /// 条件の判定は switch の行にまとめて置くが、その中の実行時例外の位置（デバッグ情報）は、元の case のラベルの行を指す（#205。印で戻す）
        /// </summary>
        [Test]
        public void ConditionsPointToTheirLabelLines()
        {
            // 行番号は 1 始まり（@" の直後の改行が 1 行目）
            const string source = @"
using UdonSharp;
namespace UdonSharpPlusTests.SwDebug
{
    public class SwUser : UdonSharpBehaviour
    {
        public int z;
        public string W(int n)
        {
            switch (n)
            {
                case var k when 10 / z > k: return ""a"";
                case 1 or 2: return ""b"";
                default: return ""c"";
            }
        }
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("SwUser.cs", source));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            DebugMethodInfo w = r["SwUser"].DebugMethods.FirstOrDefault(m => m.MethodName == "W");
            Assert.That(w, Is.Not.Null, string.Join(" / ", r["SwUser"].DebugMethods));
            int[] lines = w.Positions.Select(p => p.Line + 1).ToArray();
            Assert.That(lines, Does.Contain(12), "when の条件：" + w);
            Assert.That(lines, Does.Contain(13), "case 1 or 2 の条件：" + w);
        }
    }
}
