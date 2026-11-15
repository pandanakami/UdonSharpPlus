using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Syntax
{
    // #82 の追加テスト：ほかの機能との組み合わせ（partial の別のファイル）
    public partial class TupleMoreRunner
    {
        private string More()
        {
            string s = "";
            var nums = new List<int> { 3, 1, 2 };

            // C1：デリゲート（Action・Func・自作のデリゲート、ラムダ・匿名メソッド・ローカル関数・メソッド・別の UdonSharpBehaviour）
            Func<(int, int), int> sum = p => p.Item1 + p.Item2;
            TpMaker mk = x => (x, x * 2);
            TpMaker mk2 = delegate (int x) { return (x, 0); };
            TpMaker mk3 = Local;
            (int, int) LocalFn(int x) => (x + 1, x + 2);
            Func<int, (int, int)> lf = LocalFn;
            string log = "";
            Action<(string, int)> act = p => log += p.Item1 + p.Item2;
            act(("a", 1));
            Func<int, (int, string)> remote = peer.Pair;
            s += "C1=" + sum((2, 3)) + mk(4) + mk2(5) + mk3(6) + lf(1) + log + remote(7) + ";";

            // C2：LINQ（添字付き Select でタプルにし、SelectMany・Reverse・Concat・TakeWhile・Aggregate・Distinct へつなぐ。入れ子）
            var idx = nums.Select((v, i) => (v, i)).ToList();
            s += "C2=" + string.Join("", idx.Select(t => t.v * 10 + t.i)) + "," + string.Join("", idx.SelectMany(t => new[] { (t.v, 0), (t.i, 1) }).Select(t => t.Item1)) + ","
                 + string.Join("", idx.Select(t => t).Reverse().Select(t => t.v)) + "," + string.Join("", idx.Concat(idx.Take(1)).TakeWhile(t => t.v > 0).Select(t => t.i)) + "," + idx.Aggregate((0, 0), (a, t) => (a.Item1 + t.v, a.Item2 + t.i)) + ","
                 + idx.Select(t => (t.v % 2, 0)).Distinct().Count() + "," + idx.Count(t => idx.Any(u => u.v == t.v + 1)) + ";";

            // C3：匿名型（メンバーにタプル・タプルの要素に匿名型・相互の射影）
            var an = new { P = (1, 2), Q = "q" };
            var ta = (new { A = 1, B = "b" }, 3);
            var rows = idx.Select(t => new { t.v, Pair = t }).ToList();
            var back = rows.Select(r => (r.v, r.Pair.i)).ToList();
            s += "C3=" + an + an.Equals(new { P = (1, 2), Q = "q" }) + "," + ta + ta.Equals((new { A = 1, B = "b" }, 3)) + "," + rows[2].Pair + back[1] + back.Contains((2, 2)) + ";";

            // C4：ジェネリクス（(T, U) を何段も渡す・返す、ジェネリッククラスのフィールド・List）
            var flip = TpGen2.Flip(TpGen2.Flip((1, "x")));
            var tag = TpGen2.Tag(TpGen2.Tag("t", 1), 2);
            var h = new TpHolder<(int, int)>();
            h.items.Add((1, 2));
            h.items.Add(TpGen2.Id((3, 4)));
            var tw = new TpHolder<string>().Twice("w");
            s += "C4=" + flip + tag + tag.Item1.Item1 + "," + h.value + h.items.Count + h.items[1] + h.Twice((5, 6)).Item2 + tw + ";";

            // C5：普通のクラス・インタフェース（引数・戻り値にタプル）
            ITpShape shape = new TpRect();
            var store = new TpStore();
            store.Put(1, "one");
            store.Put(2, "two");
            s += "C5=" + shape.Size() + shape.Area((4, 5)) + shape.Area(shape.Size()) + "," + store.last + store.rows.Count + store.rows[0].name + ";";

            // C6：コレクション（タプルを値にする Dictionary・ToDictionary の値・TryGetValue・GetRange・AddRange）
            bool hit = store.byName.TryGetValue("two", out var tv);
            var dict = nums.ToDictionary(v => v, v => (v, v * v));
            var part = store.rows.GetRange(1, 1);
            part.AddRange(store.rows);
            s += "C6=" + store.byName["one"] + hit + tv + dict[3] + dict.Count + "," + part.Count + part[0] + part[2].id + ";";

            // C7：Range・Repeat・Empty
            var rg = Enumerable.Range(1, 3).Select(i => (i, i * i)).ToList();
            var rp = Enumerable.Repeat((1, "r"), 2).ToList();
            s += "C7=" + rg[2] + rg.Count + "," + rp[1] + rp.Distinct().Count() + "," + Enumerable.Empty<(int, string)>().Count() + Enumerable.Empty<(int, int)>().FirstOrDefault();
            return s;
        }
    }
}
