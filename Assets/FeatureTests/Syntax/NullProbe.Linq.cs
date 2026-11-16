using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Syntax
{
    // #105：演算子・変換の中で例外になる普通のクラス
    public class NpVec
    {
        public int v;
        public NpVec(int v) { this.v = v; }
        public static NpVec operator +(NpVec a, NpVec b) => new NpVec(a.v + b.v);   // ここ（67：b が null）
        public static implicit operator NpVec(int n) => new NpVec(10 / n);          // ここ（69：n が 0）
    }

    // #71・#142〜#146：C# なら例外になる LINQ・匿名型のリストの使い方。Udon でも、元の行で実行時例外になって止まることを確かめる。
    // 止まる行は「ここ」の行（コンソールの「Udon runtime exception detected!」の位置で確かめる）
    public partial class NullProbe
    {
        private (int, int) TupleDiv(int d)
        {
            int one = 1;
            return (one / d, 0);   // ここ
        }

        private void RunLinq()
        {
            var nums = new List<int> { 1, 2, 3 };
            var empty = new List<int>();
            switch (caseId)
            {
                case 31:
                    observed = "" + nums.ElementAt(3);   // ここ
                    break;
                case 32:
                    observed = "" + nums.Where(x => x > 0).ElementAt(-1);   // ここ
                    break;
                case 33:
                    observed = "" + empty.Single();   // ここ
                    break;
                case 34:
                    observed = "" + nums.Single();   // ここ
                    break;
                case 35:
                    observed = "" + nums.Single(x => x > 1);   // ここ
                    break;
                case 36:
                    observed = "" + empty.Aggregate((a, b) => a + b);   // ここ
                    break;
                case 37:
                    observed = "" + Enumerable.Range(0, caseId - 38).Count();   // ここ
                    break;
                case 38:
                    observed = "" + Enumerable.Repeat("x", caseId - 39).Count();   // ここ
                    break;
                case 39:
                    observed = "" + Enumerable.Range(int.MaxValue - 1, 3).Count();   // ここ
                    break;
                case 40:
                {
                    var lists = new List<List<int>> { nums, null };
                    observed = "" + lists.SelectMany(l => l).Count();   // ここ
                    break;
                }
                case 41:
                {
                    List<int> nothing = null;
                    observed = "" + nums.Concat(nothing).Count();   // ここ
                    break;
                }
                case 42:
                {
                    List<string> nothing = null;
                    observed = string.Join(",", nothing);   // ここ
                    break;
                }
                case 43:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows[3].A;   // ここ
                    break;
                }
                case 44:
                {
                    var rows = new[] { new { A = 1 }, null }.ToList();
                    observed = "" + rows.Where(r => r.A > 0).Count();   // ここ
                    break;
                }
                case 45:
                {
                    var rows = nums.Select(x => new { K = x % 2, V = x }).ToList();
                    var d = rows.ToDictionary(r => r.K);   // ここ
                    observed = "" + d.Count;
                    break;
                }
                case 46:
                    observed = "" + nums.Select((x, i) => x / i).Sum();   // ここ
                    break;
                case 47:
                    observed = string.Join(",", nums.Select(x => 10 / (x - 1)));   // ここ
                    break;
                case 48:
                    observed = "" + nums.SelectMany(x => nums, (x, y) => x / (y - 1)).Sum();   // ここ
                    break;
                case 49:
                {
                    var rows = empty.Select(x => new { A = x }).ToList();
                    observed = "" + rows.First().A;   // ここ
                    break;
                }
                case 50:
                    observed = "" + nums.SingleOrDefault(x => x > 1);   // ここ
                    break;
                // #138：匿名型のリストの、範囲の合わない IndexOf・LastIndexOf（C# では ArgumentOutOfRangeException）
                case 51:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.IndexOf(new { A = 1 }, -1);   // ここ
                    break;
                }
                case 52:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.IndexOf(new { A = 1 }, 4);   // ここ
                    break;
                }
                case 53:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.IndexOf(new { A = 1 }, 1, 3);   // ここ
                    break;
                }
                case 54:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.IndexOf(new { A = 1 }, 0, -1);   // ここ
                    break;
                }
                case 55:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.LastIndexOf(new { A = 1 }, 3);   // ここ
                    break;
                }
                case 56:
                {
                    var rows = nums.Select(x => new { A = x }).ToList();
                    observed = "" + rows.LastIndexOf(new { A = 1 }, 1, 3);   // ここ
                    break;
                }
                // #141：匿名メソッドの中の例外は、本体の元の行
                case 57:
                    observed = "" + nums.Count(delegate (int x)
                    {
                        int zero = x - x;
                        return 10 / zero > 0;   // ここ
                    });
                    break;
                case 58:
                {
                    List<int> nothing = null;
                    observed = "" + nums.SelectMany(delegate (int x) { return nothing; }).Count();   // ここ
                    break;
                }
                case 59:
                    nums.ForEach(delegate (int x)
                    {
                        observed = "" + 10 / (x - 1);   // ここ
                    });
                    break;
                // #82：タプルの中の例外は、元の行
                case 60:
                {
                    int zero = nums.Count - 3;
                    var t = (1,
                        10 / zero);   // ここ
                    observed = "" + t.Item2;
                    break;
                }
                case 61:
                {
                    NullTarget nt = null;
                    var t = (nt.v, 2);   // ここ
                    observed = "" + t.Item1;
                    break;
                }
                case 62:
                    observed = "" + TupleDiv(0).Item1;
                    break;
                case 63:
                {
                    var (a, b) = (1, nums[5]);   // ここ
                    observed = "" + a + b;
                    break;
                }
                case 64:
                {
                    var rows = nums.Select(x => (x, 10 / (x - 1))).ToList();   // ここ
                    observed = "" + rows.Count;
                    break;
                }
                case 65:
                {
                    var rows = nums.Select(x => (x, 0)).ToList();
                    observed = "" + rows.IndexOf((1, 0), 4);   // ここ
                    break;
                }
                case 66:
                {
                    int zero = nums.Count - 3;
                    observed = "" + ((1, 2) == (1, 2 / zero));   // ここ
                    break;
                }
                // #105：ユーザー定義の演算子・変換の中の例外は、元の行
                case 67:
                {
                    var a = new NpVec(1);
                    NpVec none = null;
                    observed = "" + (a + none).v;
                    break;
                }
                case 68:
                {
                    int zero = nums.Count - 3;
                    var c = new NpVec(1)
                        + new NpVec(10 / zero);   // ここ
                    observed = "" + c.v;
                    break;
                }
                case 69:
                {
                    NpVec v = 0;
                    observed = "" + v.v;
                    break;
                }
                // #140：GroupBy のキーのラムダ・グループの LINQ の中の例外は、その行
                case 70:
                {
                    var g70 = nums.GroupBy(x => 10 / (x - x)).ToList();   // ここ
                    observed = "" + g70.Count;
                    break;
                }
                case 71:
                {
                    var g71 = nums.GroupBy(x => x % 2).ToList();
                    observed = "" + g71.Select(g => g.Sum() / (g.Key - g.Key)).First();   // ここ
                    break;
                }
                case 72:
                {
                    // 複数行のつないだ呼び出しの、要素のラムダの中
                    var g72 = nums
                        .Where(x => x > 0)
                        .GroupBy(x => x % 2,
                                 x => 10 / (x - x))   // ここ
                        .ToList();
                    observed = "" + g72.Count;
                    break;
                }
            }
        }
    }
}
