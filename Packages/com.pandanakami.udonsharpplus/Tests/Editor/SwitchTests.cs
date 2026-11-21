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
            string code = Lowered("Patterns", Patterns);
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

        [TestCase("public int F(object o) { switch (o) { case 1 when o != null: return 1; } return 0; }", "の switch では、パターン・when の付いた case は未対応です")]
        [TestCase("public int F(string s) { switch (s) { case { Length: 0 }: return 1; } return 0; }", "このパターン（プロパティ・位置のパターンなど）は未対応です")]
        [TestCase("public int F(int n) { switch (n) { case long l: return 1; } return 0; }", "switch の式と違う型の型パターン")]
        public void Errors(string body, string message)
        {
            AssertError(body, message, BodyLine);
        }
    }
}
