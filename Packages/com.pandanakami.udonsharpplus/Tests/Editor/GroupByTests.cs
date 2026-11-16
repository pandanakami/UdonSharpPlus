using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// LINQ の GroupBy（#140）。グループは object[]（[型の印, キー, 要素の DataList]）。
    /// 実行時の値は、FeatureTests のパネル「#140 GroupBy」（GroupByRunner）で普通の C# と比べる
    /// </summary>
    public class GroupByTests
    {
        private const string Head = @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public enum GKind { A, B }
public class GBox { public string name; public int n; public List<int> items = new List<int>(); }
public class GHolder { public IGrouping<int, int> g; public List<IGrouping<int, int>> gs; }

public static class GUtil
{
    public static int Groups(List<int> xs) => xs.GroupBy(x => x % 2).Count();
    public static int Largest<T>(List<T> xs) => xs.GroupBy(x => x).Max(g => g.Count());
    public static int KeyOf(IGrouping<int, int> g) => g.Key + g.Count();
    public static IGrouping<int, int> Big(List<int> xs) => xs.GroupBy(x => x % 2).OrderByDescending(g => g.Count()).First();
    public static string CountBy<T, K>(List<T> xs, System.Func<T, K> f) => string.Join("""", xs.GroupBy(f).Select(g => g.Count()));
}

public class GroupUse : UdonSharpBehaviour
{
    public IGrouping<int, int> fieldG;
    private List<IGrouping<int, int>> fieldGs;
    private void Start()
    {
        var nums = new List<int> { 1, 2, 3 };
        int[] arr = { 3, 1, 2 };
        var words = new List<string> { ""bb"", ""a"", null };
        var boxes = new List<GBox> { new GBox() };
        var dict = new Dictionary<string, int> { { ""a"", 1 } };
        var kinds = new List<GKind> { GKind.A };
        ";

        private const int UserLine = 30;

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("GroupUse.cs", Head + body + "\n    }\n}"));

        [TestCase("var a = nums.GroupBy(x => x % 2).Select(g => new { g.Key, N = g.Count(), S = g.Sum() }).ToList(); int k = a[0].Key + a[0].N;")]
        [TestCase("foreach (var g in words.GroupBy(w => w == null ? 0 : w.Length)) { int k = g.Key; foreach (var w in g) { string s = w; } }")]
        [TestCase("var groups = nums.GroupBy(x => x > 1, x => x * 10); int c = groups.Count(); var first = groups.First(); bool k = first.Key; var l = first.ToList(); int m = first.Max();")]
        [TestCase("var a = dict.GroupBy(kv => kv.Value % 2).ToList(); var b = dict.GroupBy(kv => kv.Value, kv => kv.Key).Select(g => g.Key + string.Join(\",\", g)).ToList();")]
        [TestCase("var a = boxes.GroupBy(b => b.name).Select(g => g.Sum(b => b.n)).ToList(); var c = boxes.GroupBy(b => b).Count(); var d = arr.GroupBy(x => x).ToArray();")]
        [TestCase("var a = kinds.GroupBy(k => k).Select(g => g.Key).ToList(); var b = words.Where(w => w != null).GroupBy(w => w[0]).Select(g => g.Key).ToList(); var c = nums.GroupBy(x => x > 1).ToList();")]
        [TestCase("foreach (IGrouping<int, int> g in nums.GroupBy(x => x)) { int k = g.Key; } System.Linq.IGrouping<int, int> h = nums.GroupBy(x => x).First();")]
        [TestCase("var d = nums.GroupBy(x => x % 3).ToDictionary(g => g.Key, g => g.Count()); int n = d[1];")]
        [TestCase("var a = nums.GroupBy(x => x % 3).OrderByDescending(g => g.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList(); var b = nums.GroupBy(x => x).Any(g => g.Count() > 1);")]
        [TestCase("int a = GUtil.Groups(nums) + GUtil.Largest(words); var b = nums.GroupBy(x => x).SelectMany(g => g.ToList()).ToList(); var c = Enumerable.Range(0, 5).GroupBy(i => i / 2).Count();")]
        [TestCase("var a = nums.GroupBy(x => x % 2).Select(g => g.OrderBy(x => -x).First()).ToList(); var b = nums.GroupBy(x => x % 2).Select(g => g.Where(x => x > 1).Count()).ToList();")]
        // グループをそのまま外部のメソッド・SelectMany に渡す（要素の DataList を渡す）
        [TestCase("var g = nums.GroupBy(x => x % 2).First(); string a = string.Join(\",\", g); string b = string.Concat(g); string c = string.Join(\"\", nums.GroupBy(x => x).SelectMany(h => h));")]
        // First() などで取り出したグループに、続けて LINQ を書く（以前は「この LINQ のメソッドは未対応」）
        [TestCase("var a = nums.GroupBy(x => x).First().First(); int b = nums.GroupBy(x => x).Last().Count(); var c = nums.GroupBy(x => x).First().Select(x => x).ToList(); int d = boxes.GroupBy(b2 => b2.n).First().First().n;")]
        [TestCase("int a = nums.GroupBy(x => x % 2).ElementAt(0).Where(x => x > 1).Sum() + nums.GroupBy(x => x).OrderBy(g => -g.Key).First().Max();")]
        // IGrouping を引数・戻り値・フィールドで使う。object に入れて戻す。見つからない FirstOrDefault
        [TestCase("var g = nums.GroupBy(x => x).First(); int a = GUtil.KeyOf(g) + GUtil.Big(nums).Key; fieldG = g; fieldGs = nums.GroupBy(x => x).ToList(); int b = fieldG.Key + fieldGs[0].Count();")]
        [TestCase("var h = new GHolder(); h.g = nums.GroupBy(x => x).First(); h.gs = nums.GroupBy(x => x).ToList(); int a = h.g.Key + h.gs.Count + h.gs[0].Sum(); object o = h.g; var back = (IGrouping<int, int>)o; bool n = nums.GroupBy(x => x).FirstOrDefault(x => x.Key > 9) == null;")]
        // グループの中で何段も・GroupBy を入れ子・2 段の GroupBy・要素を匿名型 / タプル / 普通のクラスにする
        [TestCase("var a = nums.GroupBy(x => x % 2).Select(g => g.Where(x => x > 1).Select(x => x * 2).Sum() + g.GroupBy(x => x % 3).Count() + nums.Where(y => y > g.Key).Count()).ToList(); var b = nums.GroupBy(x => x).Select(g => new { g.Key, N = g.Count() }).GroupBy(x => x.N).Count();")]
        [TestCase("var a = nums.GroupBy(x => x % 2, x => new { V = x }).Select(g => g.Sum(e => e.V)).ToList(); var b = nums.GroupBy(x => x, x => (x, x * 2)).Select(g => g.Sum(t => t.Item2)).ToList(); var c = nums.GroupBy(x => x, x => new GBox()).Select(g => g.Count()).ToList(); var d = nums.GroupBy(x => x).Select(g => new { g.Key, Items = g.ToList() }).ToList();")]
        // float / double / long などのキー、デリゲートの変数・static メソッド・ジェネリックメソッドに渡す Func
        [TestCase("var f = new List<float> { 1f, float.NaN }; int a = f.GroupBy(x => x).Count() + new List<double> { 0.5 }.GroupBy(x => x).Count() + nums.GroupBy(x => (long)x).Count() + nums.GroupBy(x => (byte)x).Count();")]
        [TestCase("System.Func<int, int> m = x => x % 2; int a = nums.GroupBy(m).Count() + nums.GroupBy(m, m).Count(); string b = GUtil.CountBy(nums, x => x % 2) + GUtil.CountBy(words, w => w);")]
        public void Compiles(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
        }

        [TestCase("var a = nums.GroupBy(x => new { A = x }).ToList();", "匿名型をキーにする GroupBy は未対応です")]
        [TestCase("var a = nums.GroupBy(x => (x, x)).ToList();", "タプルをキーにする GroupBy は未対応です")]
        [TestCase("var a = nums.GroupBy(x => x, (k, xs) => k).ToList();", "この LINQ のメソッド（この引数の形）は未対応です")]
        [TestCase("var a = nums.GroupBy(x => new UnityEngine.Vector3(x, 0, 0)).ToList();", "この型のキー（UnityEngine.Vector3）は未対応です")]
        [TestCase("var a = nums.GroupBy(x => (decimal)x).ToList();", "この型のキー（decimal）は未対応です")]
        [TestCase("var a = nums.GroupBy(x => x, x => x * 2, (k, xs) => k).ToList();", "この LINQ のメソッド（この引数の形）は未対応です")]
        [TestCase("var a = nums.GroupBy(x => x, EqualityComparer<int>.Default).ToList();", "この LINQ のメソッド（この引数の形）は未対応です")]
        [TestCase("var a = nums.GroupBy(x => x, x => x, EqualityComparer<int>.Default).ToList();", "この LINQ のメソッド（この引数の形）は未対応です")]
        // グループを IEnumerable<T> として扱う・object[] のメソッドを呼ぶ（以前は CS0029、または object[] のメソッドになっていた）
        [TestCase("var g = nums.GroupBy(x => x).First(); IEnumerable<int> e = g;", "グループを System.Collections.Generic.IEnumerable<int> の型として扱うのは未対応です")]
        [TestCase("var g = nums.GroupBy(x => x).First(); IEnumerable<int> e; e = g;", "グループを System.Collections.Generic.IEnumerable<int> の型として扱うのは未対応です")]
        [TestCase("var g = nums.GroupBy(x => x).First(); var e = g.GetEnumerator();", "'GetEnumerator' : グループのこのメソッドは未対応です")]
        [TestCase("var g = nums.GroupBy(x => x).First(); string s = g.ToString();", "'ToString' : グループのこのメソッドは未対応です")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains($"GroupUse.cs({UserLine},")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }

        /// <summary>匿名型・タプルのキーのエラーは 1 件だけで、複数行のつないだ呼び出しでも、キーのラムダの行に出る</summary>
        [TestCase("new { A = x }", "匿名型をキーにする GroupBy は未対応です")]
        [TestCase("(x, 1)", "タプルをキーにする GroupBy は未対応です")]
        public void KeyErrorIsReportedOnceAtTheKeyLine(string key, string message)
        {
            CompileResult r = Compile("var a = nums\n            .Where(x => x > 0)\n            .GroupBy(x => " + key + ")\n            .Select(g => g.Count()).ToList();");
            var errors = r.Diagnostics.Where(d => d.StartsWith("Error")).ToList();
            Assert.That(errors.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(errors[0], Does.Contain(message).And.Contain($"GroupUse.cs({UserLine + 2},"), errors[0]);
        }
    }
}
