using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// LINQ の制限を減らす（#137）：添字付きのラムダ（#142）・SelectMany（#143）・足したメソッド（#144）・
    /// Enumerable.Range / Repeat / Empty（#145）・LINQ の結果をそのまま渡す（#146）。
    /// 実行時の値は、FeatureTests のパネル「#137 LINQ more」（LinqMoreRunner）で普通の C# と比べる
    /// </summary>
    public class LinqMoreTests
    {
        private const string Head = @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public enum Kind { A, B }
public class Box { public string name; public List<int> items = new List<int>(); public string[] tags = new string[0]; }

public class LinqMore : UdonSharpBehaviour
{
    private List<int> ItemsOf(Box b) => b.items;
    private void Take(IEnumerable<int> xs) { }

    private void Start()
    {
        var nums = new List<int> { 1, 2, 3 };
        int[] arr = { 3, 1, 2 };
        var words = new List<string> { ""b"", ""a"" };
        var boxes = new List<Box> { new Box() };
        var dict = new Dictionary<string, int> { { ""a"", 1 } };
        var kinds = new List<Kind> { Kind.A };
        var groups = new List<UnityEngine.UI.ToggleGroup>();
        var objs = new List<UnityEngine.GameObject>();
        ";

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("LinqMore.cs", Head + body + "\n    }\n}"));

        // #142：添字付きのラムダ
        [TestCase("int a = nums.Select((x, i) => x * i).Sum(); var b = nums.Where((x, i) => i % 2 == 0).ToList();")]
        [TestCase("int a = arr.TakeWhile((x, i) => i < 2).Count(); int b = arr.SkipWhile((x, i) => i < 1).First();")]
        [TestCase("var a = dict.Select((kv, i) => kv.Key + i).ToList(); var b = dict.Where((kv, i) => kv.Value > i).ToList();")]
        // #143：SelectMany
        [TestCase("int a = boxes.SelectMany(b => b.items).Sum(); var t = boxes.SelectMany(b => b.tags).ToList();")]
        [TestCase("var a = boxes.SelectMany(b => b.items, (b, i) => b.name + i).ToList(); var c = boxes.SelectMany(ItemsOf).ToArray();")]
        [TestCase("var a = boxes.SelectMany(b => b.items.Where(i => i > 1)).ToList(); var c = boxes.SelectMany((b, i) => b.items.Select(x => x + i)).ToList();")]
        [TestCase("var a = boxes.SelectMany(b => { if (b.name == null) return new List<int>(); return b.items; }).Take(2).ToList();")]
        [TestCase("var a = dict.SelectMany(kv => new[] { kv.Key, kv.Key + kv.Value }).ToList(); var c = boxes.Take(1).SelectMany(b => b.items).OrderBy(i => i).ToList();")]
        // #144：足したメソッド
        [TestCase("var a = arr.Reverse().ToList(); int b = nums.Where(x => x > 1).Reverse().First(); var c = dict.Reverse().ToList();")]
        [TestCase("int a = nums.Concat(arr).Count(); int b = arr.Concat(nums.Where(x => x > 1)).Sum(); var c = words.Concat(words).Distinct().ToList();")]
        [TestCase("int a = nums.Append(7).Last(); int b = nums.Prepend(0).First(); var c = words.Append(\"z\").Prepend(\"0\").ToList();")]
        [TestCase("int a = nums.TakeWhile(x => x < 3).Sum(); int b = nums.SkipWhile(x => x < 2).TakeWhile(x => x < 9).Count();")]
        [TestCase("int a = nums.ElementAt(1); int b = nums.ElementAtOrDefault(9); string c = words.Where(w => w != null).ElementAtOrDefault(5);")]
        [TestCase("int a = nums.Single(x => x == 3); int b = nums.SingleOrDefault(x => x > 9); int c = nums.Where(x => x == 1).Single();")]
        [TestCase("int a = nums.Aggregate((x, y) => x * y); string b = words.Aggregate(\"\", (s, w) => s + w); long c = nums.Aggregate(0L, (s, x) => s + x);")]
        // #145：Enumerable.Range・Repeat・Empty
        [TestCase("int a = Enumerable.Range(1, 5).Sum(); var b = Enumerable.Range(0, 4).Select(i => i * i).ToList(); var r = Enumerable.Range(3, 3); foreach (int x in r) a += x;")]
        [TestCase("var a = Enumerable.Repeat(\"ab\", 3).ToArray(); int b = Enumerable.Empty<int>().Count(); foreach (string s in Enumerable.Repeat(\"x\", 2)) b++; int c = (Enumerable.Range(0, 3)).Count();")]
        // #146：LINQ の結果・List を、外部のメソッドの IEnumerable<T> の引数に渡す
        [TestCase("string a = string.Join(\",\", words.Select(w => w + \"!\")); string b = string.Join(\"-\", nums.Where(x => x > 1)); string c = string.Join(\"+\", words);")]
        [TestCase("string a = string.Join(\",\", nums); string b = string.Concat(words.Select(w => w + w)); string c = string.Join(\",\", Enumerable.Range(1, 3));")]
        public void Compiles(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics, Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
        }

        // 対応していない形は、元の行でエラーになる
        [TestCase("var a = words.SelectMany(w => w).ToList();", "'SelectMany' : 集まりを返すラムダは")]
        [TestCase("var a = boxes.SelectMany(b => { return b.tags; }).ToList();", "'SelectMany' : 集まりを返すラムダは")]
        [TestCase("var a = nums.Concat(dict.Values).ToList();", "'Concat' : つなぐ相手は")]
        [TestCase("int a = nums.Aggregate(0, (s, x) => s + x, s => s * 2);", "'Aggregate' : この引数の形は未対応です")]
        [TestCase("var a = nums.GroupBy(x => x % 2).ToList();", "'GroupBy' : この LINQ のメソッド")]
        [TestCase("Take(nums.Where(x => x > 1));", "LINQ の結果をそのまま渡すのは未対応です")]
        [TestCase("string a = string.Join(\",\", kinds.Where(k => k == Kind.A));", "LINQ の結果をそのまま渡すのは未対応です")]
        [TestCase("string a = string.Join(\",\", boxes);", "として渡すのは未対応です")]
        [TestCase("var a = nums.Union(arr).ToList();", "'Union' : この LINQ のメソッド")]
        [TestCase("var a = nums.Except(arr).ToList();", "'Except' : この LINQ のメソッド")]
        [TestCase("var a = nums.Intersect(arr).ToList();", "'Intersect' : この LINQ のメソッド")]
        [TestCase("bool a = nums.SequenceEqual(arr);", "'SequenceEqual' : この LINQ のメソッド")]
        [TestCase("var a = nums.DefaultIfEmpty().ToList();", "'DefaultIfEmpty' : この LINQ のメソッド")]
        [TestCase("var a = nums.Zip(arr, (x, y) => x + y).ToList();", "'Zip' : この LINQ のメソッド")]
        [TestCase("var a = nums.Join(arr, x => x, y => y, (x, y) => x + y).ToList();", "'Join' : この LINQ のメソッド")]
        [TestCase("var a = nums.Select((x, i) => (x, i)).ToList();", "タプル")]
        [TestCase("var a = groups.SelectMany(g => g.ActiveToggles()).ToList();", "'SelectMany' : 集まりを返すラムダは")]
        [TestCase("string a = string.Join(\",\", objs);", "として渡すのは未対応です")]
        [TestCase("var rows = nums.Select(x => new { A = x }).ToList(); string a = string.Join(\",\", rows);", "として渡すのは未対応です")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains("LinqMore.cs(23,")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }
    }
}
