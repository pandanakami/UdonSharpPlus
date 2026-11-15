using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace FeatureTests.Anon
{
    // #71：partial の別のファイルで、同じ並び・別の並びの匿名型のリストを作る
    public partial class AnonListRunner
    {
        private string PartList(List<int> src)
        {
            var a = src.Select(x => new { A = x }).ToList();
            var b = src.Select(x => new { B = x, A = "s" }).ToList();
            return a.Count + "" + a.Where(r => r.A > 4).Count() + b.FindIndex(r => r.B == 3) + b[0].A;
        }

        private List<int> _src;
        private int _d;

        private List<int> Src() { _d++; return _src; }
        private int NextI() { _c++; return _c; }

        // #138：匿名型のリストを値で比べるメソッドの追加テスト
        private string EqMore()
        {
            string s = "";
            _src = new List<int> { 1, 2, 3, 1, 2, 3 };
            var r = _src.Select(x => new { A = x }).ToList();
            var t = new { A = 2 };
            var no = new { A = 9 };

            // B2：範囲つきの IndexOf・LastIndexOf（範囲の端・見つからない）
            s += "B2=" + r.IndexOf(t, 0) + r.IndexOf(t, 6) + r.IndexOf(t, 2, 0) + r.IndexOf(t, 1, 1) + r.IndexOf(t, 2, 3) + r.IndexOf(no, 0, 6) + ","
                 + r.LastIndexOf(t, 5) + r.LastIndexOf(t, 0) + r.LastIndexOf(t, 4, 1) + r.LastIndexOf(t, 3, 3) + r.LastIndexOf(t, 5, 0) + r.LastIndexOf(no, 5, 6) + ";";

            // B3：受け側・引数は、左から 1 回ずつ評価する
            var lists = new[] { r };
            _c = 0;
            int b3 = lists[NextI() - 1].IndexOf(new { A = NextI() }, NextI() - 2, NextI());
            s += "B3=" + b3 + "," + _c + ";";

            // B4：Remove は、最初の 1 つだけを消す（null・見つからないとき）
            var r4 = _src.Select(x => new { A = x }).ToList();
            r4.Insert(2, null);
            bool b4a = r4.Remove(t);
            bool b4b = r4.Remove(null);
            bool b4c = r4.Remove(no);
            bool b4d = r4.Remove(null);
            s += "B4=" + b4a + b4b + b4c + b4d + "," + string.Join("", r4.Select(e => e.A)) + "," + r.Count + ";";

            // B5：Distinct（重なり・null・入れ子の匿名型・普通のクラスのメンバー）。最初に出たものを残す
            var item = new AnItem("i", 1);
            var item2 = new AnItem("i", 1);
            var d1 = new[] { new { A = 1 }, null, new { A = 1 }, null, new { A = 2 } }.ToList().Distinct().ToList();
            var d2 = _src.Select(x => new { In = new { K = x % 2 }, T = "t" }).ToList().Distinct().ToList();
            var d3 = new[] { new { R = item }, new { R = item2 }, new { R = item } }.ToList().Distinct().ToList();
            s += "B5=" + d1.Count + d1[0].A + (d1[1] == null) + d1[2].A + "," + d2.Count + d2[0].In.K + "," + d3.Count + ";";

            // B6：値の比べ方（入れ子・null のメンバー・配列／普通のクラス／インタフェースは参照・NaN）
            int[] arr = { 1 };
            IAnShape sq = new AnSquare();
            var m1 = new[] { new { In = new { K = 1 }, S = (string)null, Arr = arr, It = item, Sh = sq, F = float.NaN, D = 0.5 } }.ToList();
            s += "B6=" + m1.Contains(new { In = new { K = 1 }, S = (string)null, Arr = arr, It = item, Sh = sq, F = float.NaN, D = 0.5 })
                 + m1.Contains(new { In = new { K = 2 }, S = (string)null, Arr = arr, It = item, Sh = sq, F = float.NaN, D = 0.5 })
                 + m1.Contains(new { In = new { K = 1 }, S = "", Arr = arr, It = item, Sh = sq, F = float.NaN, D = 0.5 })
                 + m1.Contains(new { In = new { K = 1 }, S = (string)null, Arr = new[] { 1 }, It = item, Sh = sq, F = float.NaN, D = 0.5 })
                 + m1.Contains(new { In = new { K = 1 }, S = (string)null, Arr = arr, It = item2, Sh = sq, F = float.NaN, D = 0.5 })
                 + m1.IndexOf(new { In = new { K = 1 }, S = (string)null, Arr = arr, It = item, Sh = (IAnShape)null, F = float.NaN, D = 0.5 }) + ";";

            // BC1：LINQ の結果への Contains・Distinct は、C# と同じく必要な分だけ前の段を呼ぶ
            _c = 0;
            bool c1 = _src.Select(x => { _c++; return new { A = x }; }).Contains(t);
            int c1n = _c;
            _c = 0;
            int c1b = _src.Select(x => { _c++; return new { A = x }; }).Distinct().Take(1).Count();
            s += "BC1=" + c1 + c1n + "," + c1b + _c + ";";

            // BC2：配列・LINQ の結果・List を受け側にする。受け側の式は 1 回だけ評価する
            _d = 0;
            var arr2 = r.ToArray();
            s += "BC2=" + arr2.Contains(t) + Src().Select(x => new { A = x }).Contains(no) + Src().Select(x => new { A = x }).Distinct().Count() + r.Where(e => e.A > 1).Distinct().Count() + "," + _d + ";";

            // BC3・BC4：普通のクラス・ジェネリッククラスの中
            var bag = new AnBag<string>();
            bag.items.Add("a");
            bag.items.Add("b");
            bag.items.Add("a");
            var bagI = new AnBag<int>();
            bagI.items.Add(5);
            s += "BC3=" + new AnEqUser().Run(_src) + ";";
            s += "BC4=" + bag.Has("b") + bag.Has("z") + bag.Uniq() + bag.Find("a") + "," + bagI.Has(5) + bagI.Uniq() + ";";

            // BC5：入れ子（外側と内側で、同じ匿名型の Contains・Distinct・IndexOf を使う）
            s += "BC5=" + r.Where(e => r.Contains(new { A = e.A + 1 })).Distinct().Count() + "," + r.Distinct().Where(e => r.Distinct().Contains(e)).Count() + "," + r.Count(e => r.IndexOf(e) == r.LastIndexOf(e) - 3) + ";";

            // BC6：Range・Concat・SelectMany・Repeat で作った列
            s += "BC6=" + Enumerable.Range(0, 5).Select(i => new { A = i % 2 }).Distinct().Count() + "," + r.Concat(r).Distinct().Count() + "," + r.SelectMany(e => r).Contains(t) + ","
                 + Enumerable.Repeat(t, 3).Distinct().Count() + Enumerable.Range(1, 3).Select(i => new { A = i }).Contains(no);
            return s;
        }
    }
}
