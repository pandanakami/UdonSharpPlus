using System;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 機能ごとの負荷の目安（#151）。書き換え後の UASM にある、重いとされる命令の数を、操作ごとに固定する。
    /// 操作を書いたコードと書かないコードの差で数える。デリゲートなどの補助メソッドは、1 つのプログラムに 1 回だけ入り、
    /// 最初に使った操作の差に補助メソッドの中の命令が入る（= その操作を 1 回通ったときの数）。
    /// 数は「コードにある命令」で、ループは 1 回通るものとして数える。書き換えを変えて増減したら、Documentation~/cost.md の表も直す
    /// </summary>
    public class CostTests
    {
        private const string Head = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class P { public int x; public P(int x) { this.x = x; } public int Get() => x; }
public interface IFoo { int F(); }
public class FooPlain : IFoo { public int F() => 1; }
public class Money { public int v; public Money(int v) { this.v = v; }
    public static Money operator +(Money a, Money b) => new Money(a.v + b.v); }
public static class U { public static T Id<T>(T t) => t; }

public class Cost : UdonSharpBehaviour
{
    private int Twice(int x) => x * 2;
    private bool Big(int x) => x > 1;

    public void Run()
    {
        int n = 0;
        BODY
    }
}";

        // 数える命令：SendCustomEvent・GetProgramVariable・SetProgramVariable・new object[]・DataList・DataDictionary・DataToken・string の連結・
        // ほかの配列の生成（DataToken[] など。#162）。最後のものは ArrayPattern で数える
        private static readonly string[] Patterns =
        {
            "__SendCustomEvent__", "__GetProgramVariable__", "__SetProgramVariable__", "SystemObjectArray.__ctor",
            "DataDataList.__ctor", "DataDataDictionary.__ctor", "DataDataToken.__ctor", "SystemString.__Concat",
        };

        // object[] のほかの配列の生成（new DataToken[n] など）
        private const string ArrayPattern = "Array.__ctor";

        private const string Nums = "var nums = new List<int> { 1, 2, 3 };";

        private static int[] Count(string body)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Cost.cs", Head.Replace("BODY", body)));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, body);
            var counts = new int[Patterns.Length + 1];
            foreach (string raw in r["Cost"].Uasm.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("EXTERN", StringComparison.Ordinal))
                    continue;
                for (int i = 0; i < Patterns.Length; i++)
                    if (line.Contains(Patterns[i]))
                        counts[i]++;
                if (line.Contains(ArrayPattern) && !line.Contains("SystemObjectArray.__ctor"))
                    counts[Patterns.Length]++;
            }
            return counts;
        }

        // 期待する差（Send, Get, Set, object[], DataList, DataDictionary, DataToken, 連結, ほかの配列）
        // setup は、測る操作に必要な前の準備（その分は差に入れない）
        [TestCase("P q = new P(2);", "", "0,0,0,1,0,0,0,0,0", TestName = "plain class new")]
        [TestCase("n = p.Get();", "P p = new P(1);", "0,0,0,0,0,0,0,0,0", TestName = "plain class method call")]
        [TestCase("n = p.x;", "P p = new P(1);", "0,0,0,0,0,0,0,0,0", TestName = "plain class field read")]
        [TestCase("p.x = 5;", "P p = new P(1);", "0,0,0,0,0,0,0,0,0", TestName = "plain class field write")]
        [TestCase("n = foo.F();", "IFoo foo = new FooPlain();", "1,1,0,0,0,0,0,0,0", TestName = "interface call")]
        [TestCase("Func<int, int> f = x => x + 1;", "", "0,0,0,1,0,0,0,0,0", TestName = "lambda create")]
        [TestCase("Func<int, int> h = x => x + n;", "", "0,0,0,3,0,0,0,0,0", TestName = "lambda create (captures a variable)")]
        // デリゲートの呼び出しの補助メソッドは、相手が 1 つのとき（__one）と複数のとき（__many）の 2 つの道を持つので（#170）、
        // Get・Set はコードに 2 組ある（呼ぶたびに通るのは片方）。new object[] は、+= / -= の補助メソッドの 2 つと、
        // UdonSharp の RecursiveMethod の退避用の配列を大きくし直すもの（入れ子が深いときだけ通る）
        [TestCase("n = f(1);", "Func<int, int> f = x => x + 1;", "1,2,4,3,0,0,0,0,0", TestName = "delegate call (lambda), first use")]
        [TestCase("n = f(2);", "Func<int, int> f = x => x + 1; n = f(1);", "0,0,0,0,0,0,0,0,0", TestName = "delegate call (lambda), second use")]
        [TestCase("n = g(1);", "Func<int, int> g = Twice;", "1,2,4,3,0,0,0,0,0", TestName = "delegate call (behaviour method)")]
        [TestCase("var t = (1, 2); n = t.Item1;", "", "0,0,0,1,0,0,0,0,0", TestName = "tuple")]
        [TestCase("var a = new { A = 1 }; n = a.A;", "", "0,0,0,1,0,0,0,0,0", TestName = "anonymous type")]
        [TestCase("var l = new List<int>(); l.Add(1);", "", "0,0,0,0,1,0,0,0,0", TestName = "List new")]
        [TestCase("n = nums[0];", Nums, "0,0,0,0,0,0,0,0,0", TestName = "List read")]
        [TestCase("foreach (int x in nums) n += x;", Nums, "0,0,0,0,0,0,0,0,0", TestName = "List foreach")]
        [TestCase("d[\"a\"] = 1; n = d[\"a\"];", "var d = new Dictionary<string, int>();", "0,0,0,0,0,0,0,0,0", TestName = "Dictionary set and get")]
        [TestCase("var r = nums.Where(x => x > 1).ToList();", Nums, "0,0,0,0,1,0,1,0,0", TestName = "LINQ Where.ToList")]
        [TestCase("n = nums.Select(x => x * 2).Sum();", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ Select.Sum")]
        // GroupBy のグループ（object[]・DataList）は、使い回す置き場（#171）にないときだけ作る（グループの数がいちばん多かったときを超えたとき）
        [TestCase("n = nums.GroupBy(x => x % 2).Count();", Nums, "0,0,0,1,1,0,5,0,0", TestName = "LINQ GroupBy.Count")]
        [TestCase("Money s = m + m;", "Money m = new Money(1);", "0,0,0,0,0,0,0,0,0", TestName = "operator")]
        [TestCase("n = U.Id(3);", "", "0,0,0,0,0,0,0,0,0", TestName = "generic method")]
        [TestCase("string s = \"a\" + n;", "", "0,0,0,0,0,0,0,1,0", TestName = "string concat")]
        // LINQ を軽く書ける形（ラムダはループに埋め込むので、デリゲートの値は作らない）
        [TestCase("var r = nums.Where(x => x > 1).Select(x => x * 2).ToList();", Nums, "0,0,0,0,1,0,1,0,0", TestName = "LINQ chain makes one list")]
        [TestCase("bool b = nums.Any(x => x > 1);", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ Any")]
        [TestCase("n = nums.Count(x => x > 1);", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ Count")]
        [TestCase("n = nums.First(x => x > 1);", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ First")]
        [TestCase("n = nums.Where(x => x > n).Count();", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ lambda captures a variable")]
        [TestCase("n = nums.Where(x => { if (x > 1) return true; return false; }).Count();", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ block lambda")]
        [TestCase("n = nums.Where(Big).Count();", Nums, "0,0,0,0,0,0,0,0,0", TestName = "LINQ method group")]
        [TestCase("nums.ForEach(x => n += x);", Nums, "0,0,0,0,0,0,0,0,0", TestName = "List.ForEach lambda")]
        [TestCase("bool b = nums.Exists(x => x > 1);", Nums, "0,0,0,0,0,0,0,0,0", TestName = "List.Exists lambda")]
        // List.Sort・タプルのリストの foreach・並べ替えの LINQ（#162）
        // List.Sort の配列 2 つは、使い回す作業用の配列を大きく作り直すところ（16 個を超える要素を初めて並べ替えたときだけ通る。#171）
        [TestCase("nums.Sort((a, b) => b - a);", Nums, "0,0,0,0,0,0,0,0,2", TestName = "List.Sort lambda")]
        [TestCase("nums.Sort(cmp);", Nums + " Comparison<int> cmp = (a, b) => b - a;", "1,2,6,3,0,0,0,0,2", TestName = "List.Sort with a delegate variable")]
        [TestCase("foreach (var t in tl) n += t.Item1;", "var tl = new List<(int, int)> { (1, 2) };", "0,0,0,0,0,0,0,0,0", TestName = "tuple List foreach")]
        [TestCase("foreach (var (a, b) in tl) n += a + b;", "var tl = new List<(int, int)> { (1, 2) };", "0,0,0,0,0,0,0,0,0", TestName = "tuple List foreach with deconstruction")]
        [TestCase("var r = nums.OrderBy(x => -x).ToList();", Nums, "0,0,0,0,1,0,4,0,0", TestName = "LINQ OrderBy.ToList")]
        [TestCase("n = nums.OrderBy(x => -x).First();", Nums, "0,0,0,0,0,0,3,0,0", TestName = "LINQ OrderBy.First")]
        [TestCase("var l = new List<int> { 1, 2, 3 };", "", "0,0,0,0,1,0,0,0,1", TestName = "List new with initializer")]
        [TestCase("var d = new Dictionary<string, int> { { \"a\", 1 } };", "", "0,0,0,0,0,1,0,0,2", TestName = "Dictionary new with initializer")]
        [TestCase("int[] a = nums.ToArray();", Nums, "0,0,0,0,0,0,0,0,1", TestName = "List ToArray")]
        [TestCase("string s = string.Join(\",\", nums);", Nums, "0,0,0,0,0,0,0,0,1", TestName = "string.Join of a List")]
        // 重くなる形：デリゲートの変数を LINQ に渡すと、デリゲートの呼び出しになる
        [TestCase("n = nums.Select(f).Sum();", Nums + " Func<int, int> f = x => x * 2;", "1,2,4,3,0,0,0,0,0", TestName = "LINQ with a delegate variable")]
        public void CountsHeavyInstructions(string operation, string setup, string expected)
        {
            int[] before = Count(setup + " n = 1;");
            int[] after = Count(setup + " " + operation);
            string actual = string.Join(",", after.Select((v, i) => v - before[i]));
            Assert.That(actual, Is.EqualTo(expected),
                "順番：SendCustomEvent, GetProgramVariable, SetProgramVariable, new object[], DataList, DataDictionary, DataToken, 連結, ほかの配列");
        }
    }
}
