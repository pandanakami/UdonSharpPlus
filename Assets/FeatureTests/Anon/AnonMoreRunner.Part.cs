using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Anon
{
    // #70：partial の別のファイルで、並びの違う匿名型を作る（生成する番号・置く場所・再利用が安定する）
    public partial class AnonMoreRunner
    {
        private string PartMore()
        {
            var p = new { Score = 3, Name = "a" };
            var q = new { Name = "a", Score = 3 };
            return p + "," + q + "," + p.Equals(q) + "," + q.Equals(new { Name = "a", Score = 3 });
        }
        // #71：匿名型のリスト（LINQ の射影・配列から ToList・List / LINQ のメソッド）
        private string ListMore()
        {
            string s = "";
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };

            var l1 = new[] { new { A = 1 }, new { A = 2 } }.ToList();
            int t = 0; foreach (var e in l1) t += e.A;
            l1.Add(new { A = 9 }); l1.Insert(0, new { A = 0 }); l1.RemoveAt(1);
            s += "L1=" + t + "," + l1.Count + "," + l1[0].A + l1[1].A + l1[2].A + ";";

            var r = nums.Select(x => new { A = x, B = "b" + x }).ToList();
            var f = r.Find(e => e.A == 2); var fi = r.FindIndex(e => e.A == 3); var ex = r.Exists(e => e.A > 5);
            var none = r.Find(e => e.A > 99);
            r.Sort((a, b) => b.A - a.A); int top = r[0].A;
            int rem = r.RemoveAll(e => e.A % 2 == 0); r.Reverse();
            s += "L2=" + f.B + "," + fi + "," + ex + "," + (none == null) + "," + top + "," + rem + "," + r[0].A + r.Count + ";";

            var r3 = nums.Select(x => new { A = x }).ToList();
            var first = r3[0]; r3[1] = new { A = 50 };
            s += "L3=" + r3.Exists(e => e.A == first.A) + "" + r3.FindIndex(e => e.A == 3) + r3[1].A + ";";

            var arr = nums.Select(x => new { A = x }).ToArray(); int ta = 0;
            for (int i = 0; i < arr.Length; i++) ta += arr[i].A;
            s += "L4=" + ta + "," + arr.Length + ";";

            var r5 = nums.Select(x => new { A = x }).ToList();
            var fo = r5.FirstOrDefault(e => e.A > 99);
            s += "L5=" + r5.First().A + "," + (fo == null) + "," + r5.Last().A + "," + r5.Any(e => e.A > 5) + "," + r5.Count(e => e.A > 2) + ";";

            var r6 = nums.Select(x => new { A = x, B = "k" + (x % 3) }).OrderBy(e => e.B).ThenByDescending(e => e.A).ToList();
            string o6 = ""; foreach (var e in r6) o6 += e.A;
            s += "L6=" + o6 + "," + r6.Sum(e => e.A) + "," + r6.Max(e => e.A) + ";";

            var r7 = nums.Select(x => new { A = x }).ToList();
            var g = r7.GetRange(1, 2); var st = r7.Skip(4).Take(5).ToList(); string o7 = "";
            foreach (var e in g) o7 += e;
            foreach (var e in st) o7 += e.A;
            s += "L7=" + o7 + ";";

            s += "L8=" + r5.Count;
            return s;
        }
    }
}
