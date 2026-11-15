using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// タプル（#82）：要素の型の並びごとの普通のクラスに書き換える。
    /// 実行時の値は、FeatureTests のパネル「#82 tuples」（TupleRunner）で普通の C# と比べる
    /// </summary>
    public class TupleTests
    {
        private const string Head = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

public class TBox { public (int, string) pair; public (int a, int b) named = (1, 2); public (int, int) Swap((int, int) p) => (p.Item2, p.Item1); }
public class TGen<T> { public (T, int) Wrap(T v) => (v, 1); public T Get() { var (x, _) = (default(T), 0); return x; } }

public class TupleUse : UdonSharpBehaviour
{
    private (int, string) _field;
    private (int x, int y) _pos = (1, 2);
    private List<(int, string)> _list = new List<(int, string)>();
    private (int, int) Pair() => (1, 2);
    private (int min, int max) Range(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    private void Start()
    {
        var nums = new List<int> { 1, 2, 3 };
        ";

        private static CompileResult Compile(string body) =>
            UdonSharpTestCompiler.Compile(new TestSource("TupleUse.cs", Head + body + "\n    }\n}"));

        [TestCase("var t = (1, \"a\"); int n = t.Item1; string s = t.Item2;")]
        [TestCase("(int x, string y) t = (2, \"b\"); int n = t.x + t.Item1; var u = (a: 1, b: 2); int m = u.a + u.b;")]
        [TestCase("ValueTuple<int, string> vt = default; var w = new ValueTuple<int, int>(1, 2); int n = vt.Item1 + w.Item2;")]
        [TestCase("var (a, b) = Pair(); int c = 1, d = 2; (c, d) = (d, c); (int e, var f) = Range(3, 1); var (_, g) = Pair();")]
        [TestCase("var lst = nums.Select(x => (x, x * 2)).ToList(); foreach (var (k, v) in lst) { int s = k + v; } foreach (var t in lst) { int s = t.Item1; }")]
        [TestCase("bool e = nums.Exists(i => (i, i).Item1 == 0); var q = nums.Select((x, i) => (x, i)).Where(p => p.i > 0).ToList();")]
        [TestCase("var t = (1, 2); bool a = t == (1, 2); bool b = t != Pair(); bool c = t.Equals(Pair()); string s = t.ToString() + t + $\"{t}\";")]
        [TestCase("var nested = (1, (2, \"x\")); int n = nested.Item2.Item1; var (a, (b, c)) = nested; string s = nested.ToString();")]
        [TestCase("var box = new TBox(); box.pair = (1, \"a\"); var sw = box.Swap(box.named); int n = sw.Item1 + box.pair.Item1;")]
        [TestCase("var g = new TGen<string>().Wrap(\"s\"); var h = new TGen<int>().Get(); string s = g.Item1 + h;")]
        [TestCase("var arr = new (int, int)[3]; arr[0] = (1, 2); int n = arr[1].Item1; var arr2 = new[] { (1, \"a\"), (2, \"b\") }; int m = arr2.Length;")]
        [TestCase("_list.Add((1, \"a\")); bool c = _list.Contains((1, \"a\")); int i = _list.IndexOf((1, \"a\")); bool r = _list.Remove((1, \"a\")); var d = _list.Distinct().ToList();")]
        [TestCase("var anon = new { T = (1, 2) }; int n = anon.T.Item1; var mix = (new { A = 1 }, 2); int m = mix.Item1.A; string s = anon.ToString();")]
        [TestCase("Func<int, (int, int)> f = x => (x, x); var t = f(1); Action<(int, string)> a = p => Debug.Log(p.Item2); a((1, \"x\"));")]
        [TestCase("var t = _pos; t = (t.x + 1, t.y); _pos = t; _field = (5, \"f\"); int n = _field.Item1;")]
        [TestCase("var big = (1, 2, 3, 4, 5, 6, 7); int n = big.Item7; var (a, b, c, d, e, f, g) = big;")]
        [TestCase("if (nums.Count > 0) (nums[0], nums[1]) = (nums[1], nums[0]); string s = (1, 2.5f, 'c', true, \"s\").ToString();")]
        [TestCase("var t = (1, 2); t = (t.Item1 + 1, t.Item2); var u = t; u = (0, 0); _pos = (_pos.y, _pos.x);")]
        [TestCase("var d = new Dictionary<string, (int, int)>(); d[\"a\"] = (1, 2); bool h = d.TryGetValue(\"a\", out var v); var e = nums.ToDictionary(x => x, x => (x, x)); int n = e[1].Item2 + v.Item1;")]
        public void Compiles(string body)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics, Is.Empty, body + "\n" + string.Join("\n", r.Diagnostics));
        }

        [TestCase("var t = (1, 2); t.Item1 = 5;", "タプルの要素への代入は未対応です")]
        [TestCase("_pos.x++;", "タプルの要素への代入は未対応です")]
        [TestCase("var t = (1, 2); object o = t;", "object・インタフェースとして使うこと")]
        [TestCase("Debug.Log((1, 2));", "object・インタフェースとして使うこと")]
        [TestCase("var t = (1, 2); int h = t.GetHashCode();", "'GetHashCode' : タプルの GetHashCode は未対応です")]
        [TestCase("var d = new Dictionary<(int, int), int>();", "タプルをキーにする Dictionary は未対応です")]
        [TestCase("var d = nums.ToDictionary(x => (x, x));", "タプルをキーにする Dictionary は未対応です")]
        [TestCase("var t = (1, 2); switch (t) { default: break; }", "タプルの switch は未対応です")]
        [TestCase("var t = (1, 2); bool b = t is (1, 2);", "タプルのパターン（is）は未対応です")]
        [TestCase("(int, int)? n = null;", "null 許容のタプル")]
        [TestCase("var dict = new Dictionary<string, int>(); foreach (var (k, v) in dict) { }", "タプル以外の分解")]
        [TestCase("var t = (1, 2); (long, long) u = t;", "要素の型を変えるタプルの変換は未対応です")]
        [TestCase("var big = (1, 2, 3, 4, 5, 6, 7, 8);", "要素が 8 個以上のタプルは未対応です")]
        [TestCase("int a, b; for ((a, b) = (0, 0); a < 1; a++) { }", "タプルの分解は、1 つの文として書いてください")]
        [TestCase("var l = new List<(int, int)>(); l.Sort();", "タプルの大小の比較は未対応です")]
        [TestCase("var t = (1, 2); t.Item1 += 3;", "タプルの要素への代入は未対応です")]
        [TestCase("var t = (1, 2); int.TryParse(\"1\", out t.Item1);", "タプルの要素への代入は未対応です")]
        [TestCase("var arr = new (int, int)[1]; arr[0].Item2 = 1;", "タプルの要素への代入は未対応です")]
        [TestCase("_list[0].Item1 = 1;", "タプルの要素への代入は未対応です")]
        [TestCase("var t = (1, 2); int c = t.CompareTo((1, 2));", "タプルの CompareTo は未対応です")]
        [TestCase("var t = (1, 2); IComparable c = t;", "object・インタフェースとして使うこと")]
        [TestCase("var t = (1, 2); var r = t switch { _ => 1 };", "タプルの switch は未対応です")]
        [TestCase("bool b = nums.Any(x => { var (p, q) = (x, x); return p == q; }) && nums.Select(x => (x, 1)).Any(t => t == (1, 1)); int a; int z = ((a, _) = (1, 2)).Item1;", "タプルの分解は、1 つの文として書いてください")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = Compile(body);
            Assert.That(r.Diagnostics.Any(d => d.Contains(message) && d.Contains("TupleUse.cs(21,")),
                body + "\n" + (r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics)));
        }
    }
}
