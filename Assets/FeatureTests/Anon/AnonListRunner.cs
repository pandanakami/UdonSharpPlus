using System;
using System.Collections.Generic;
using System.Linq;
using FeatureTests.Delegate;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Anon
{
    // #71：匿名型のリストを、普通のクラスの中で作る・変える・LINQ に使う・ジェネリックメソッドに渡す
    public class AnListUser
    {
        public int bias = 1;

        public string Run(List<int> src)
        {
            var rows = src.Select(x => new { A = x, B = x + bias }).ToList();
            rows.Add(new { A = 9, B = 0 });
            rows.RemoveAll(r => r.A % 2 == 0);
            rows.Sort((a, b) => b.A - a.A);
            var cp = AnGen.Copy(rows);
            return string.Join("", rows.Select(r => r.A)) + ":" + rows.Where(r => r.B > 2).OrderBy(r => r.B).First().A + ":" + cp.Count + FnGen.CountIf(cp, r => r.B == 0);
        }

        public int Apply(Func<int, int> cb, int i) => cb(i) + bias;

        public int Total(List<int> xs) => xs.Select(x => new { V = x }).Sum(e => e.V);
    }

    // #138：普通のクラスの中で、匿名型のリストを値で比べる
    public class AnEqUser
    {
        public string Run(List<int> src)
        {
            var rows = src.Select(x => new { A = x }).ToList();
            var t = new { A = 3 };
            bool c = rows.Contains(t);
            int i = rows.IndexOf(t, 3);
            bool rm = rows.Remove(t);
            return "" + c + i + rm + rows.Count + rows.Distinct().Count() + rows.Select(e => new { e.A }).Contains(t);
        }
    }

    // #138：ジェネリッククラスの中で、型引数を使う匿名型のリストを値で比べる
    public class AnBag<T>
    {
        public List<T> items = new List<T>();

        public bool Has(T v)
        {
            var rows = items.Select(x => new { x }).ToList();
            return rows.Contains(new { x = v });
        }

        public int Uniq() => items.Select(x => new { x }).Distinct().Count();

        public int Find(T v)
        {
            var rows = items.Select(x => new { x }).ToList();
            return rows.LastIndexOf(new { x = v });
        }
    }

    // #71 の追加テスト：匿名型のリスト（A：機能単体、C：ほかの機能との組み合わせ）。#138：値で比べるメソッド（B）
    public partial class AnonListRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "A1=0FalseTrueTrue00;A2=1,4,True,True13,2;A3=19826,59,986,62,2,035;A4=32,TrueTrueTrue;A5=TrueFalse,14-1-1,41-1,11,1203,TrueTrue3,TrueFalse510120,True132;A6=True,1,2,False,24;A8=n33,Truen2,FalseTrue,1True;A10=bdac,dbca,acbd,acbd;A11=True2,False3,33,True6,32,22,24;A12=4,-3,7,2,4999999999,5000000000,-0.5,-2,-0.25,1000000000.25,1000000000,0;C1=3,3,65;C2=2,6True6,6;C4=203040,2;C5=66,23;C6=9531:3:41,9,21;C8=65,62,3;C9=25,32,True1,8;C10=622s;C11=61;B2=1-1-114-1,4-141-1-1;B3=1,4;B4=TrueTrueFalseFalse,13123,6;B5=31True2,21,2;B6=TrueFalseFalseFalseFalse-1;BC1=True2,11;BC2=TrueFalse32,2;BC3=True5True53True;BC4=TrueFalse22,True1;BC5=2,3,6;BC6=2,3,True,1False";

        public Text display;

        private int _c;

        private int Bump() { _c++; return 2; }

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };

            // A1：空のリスト
            var none = nums.Where(x => x > 99).Select(x => new { A = x }).ToList();
            s += "A1=" + none.Count + none.Any() + (none.FirstOrDefault() == null) + (none.LastOrDefault() == null) + none.Sum(e => e.A) + none.Count(e => e.A > 0) + ";";

            // A2：null の要素
            var withNull = new[] { new { A = 3 }, null, new { A = 1 } }.ToList();
            int nn = 0;
            foreach (var e in withNull)
                if (e == null)
                    nn++;
            withNull.Sort((a, b) => (a == null ? 0 : a.A) - (b == null ? 0 : b.A));
            s += "A2=" + nn + "," + withNull.Where(e => e != null).Sum(e => e.A) + "," + (withNull.FirstOrDefault(e => e == null) == null) + "," + (withNull[0] == null) + withNull[1].A + withNull[2].A + ","
                 + withNull.FindIndex(e => e != null && e.A == 3) + ";";

            // A3：残りの List の操作
            var l3 = nums.Take(3).Select(x => new { A = x }).ToList();
            var more = nums.Skip(4).Select(x => new { A = x }).ToList();
            l3.AddRange(more);
            l3.InsertRange(1, new[] { new { A = 9 }, new { A = 8 } });
            l3.RemoveRange(4, 2);
            var arr3 = l3.ToArray();
            var all3 = l3.FindAll(e => e.A > 5);
            s += "A3=" + string.Join("", l3.Select(e => e.A)) + "," + arr3.Length + arr3[1].A + "," + string.Join("", all3.Select(e => e.A)) + "," + l3.FindLast(e => e.A < 9).A + l3.FindLastIndex(e => e.A > 7) + "," + more.Count;
            l3.Clear();
            s += "," + l3.Count + all3.Count + arr3.Length + ";";

            // A4：リストのコピーは別のリスト、要素は同じインスタンス（配列との行き来も）
            var orig = nums.Take(3).Select(x => new { A = x }).ToList();
            var copy = orig.ToList();
            copy.RemoveAt(0);
            var oarr = orig.ToArray();
            var back = oarr.ToList();
            s += "A4=" + orig.Count + copy.Count + "," + (copy[0] == orig[1]) + (oarr[0] == orig[0]) + (back[2] == orig[2]) + ";";

            // A5（#138）：Contains・IndexOf・LastIndexOf・Remove・Distinct は、C# と同じく値で比べる（引数は 1 回だけ評価する。null どうしは等しい）
            var l5 = nums.Select(x => new { A = x % 3 }).ToList();
            var t5 = new { A = 2 };
            _c = 0;
            s += "A5=" + l5.Contains(t5) + l5.Contains(new { A = 7 }) + "," + l5.IndexOf(t5) + l5.IndexOf(t5, 2) + l5.IndexOf(t5, 2, 2) + l5.IndexOf(new { A = 9 }) + "," + l5.LastIndexOf(t5) + l5.LastIndexOf(t5, 3) + l5.LastIndexOf(t5, 3, 2) + ","
                 + l5.IndexOf(new { A = Bump() }) + _c + "," + string.Join("", l5.Distinct().Select(e => e.A)) + l5.Distinct().Count() + "," + l5.Where(e => e.A > 0).Contains(t5) + l5.ToArray().Contains(new { A = 0 }) + l5.Select(e => new { B = e.A }).Distinct().Count() + ",";
            bool r5 = l5.Remove(t5);
            bool r5b = l5.Remove(new { A = 9 });
            var ln = new[] { new { A = 1 }, null, new { A = 1 }, null }.ToList();
            s += r5 + "" + r5b + l5.Count + string.Join("", l5.Select(e => e.A)) + "," + ln.Contains(null) + ln.IndexOf(null) + ln.LastIndexOf(null) + ln.Distinct().Count() + ";";

            // A6：値で比べたいときの書き方（Equals をラムダの中で使う）
            var target = new { A = 2 };
            var l6 = nums.Select(x => new { A = x % 3 }).ToList();
            s += "A6=" + l6.Any(x => x.Equals(target)) + "," + l6.FindIndex(x => x.Equals(target)) + "," + l6.Count(x => x.Equals(target)) + "," + l6.Exists(x => x.Equals(new { A = 7 })) + "," + l6.RemoveAll(x => x.Equals(target)) + l6.Count + ";";

            // A8：匿名型を Dictionary の値にする
            var byId = nums.Take(3).Select(x => new { Id = x, Name = "n" + x }).ToDictionary(e => e.Id);
            var byName = nums.Take(2).Select(x => new { Id = x, Name = "n" + x }).ToDictionary(e => e.Name, e => e);
            bool got = byId.TryGetValue(2, out var hit);
            bool miss = byId.TryGetValue(9, out var nohit);
            s += "A8=" + byId[3].Name + byId.Count + "," + got + hit.Name + "," + miss + (nohit == null) + "," + byName["n1"].Id + byName.ContainsKey("n2") + ";";

            // A10：並べ替え（同じキーは元の順のまま・ThenBy・外の変数を使う比較）
            var l10 = new[] { new { K = 2, T = "a" }, new { K = 1, T = "b" }, new { K = 2, T = "c" }, new { K = 1, T = "d" } }.ToList();
            int sign = 1;
            string ob = string.Join("", l10.OrderBy(e => e.K).Select(e => e.T));
            string tb = string.Join("", l10.OrderBy(e => e.K).ThenByDescending(e => e.T).Select(e => e.T));
            string od = string.Join("", l10.OrderByDescending(e => e.K).ThenBy(e => e.T).Select(e => e.T));
            l10.Sort((a, b) => a.K != b.K ? b.K - a.K : (a.T[0] - b.T[0]) * sign);
            s += "A10=" + ob + "," + tb + "," + od + "," + string.Join("", l10.Select(e => e.T)) + ";";

            // A11：途中で止まるメソッドの、ラムダを呼ぶ回数
            var l11 = nums.Select(x => new { A = x }).ToList();
            _c = 0; bool a11a = l11.Any(e => { _c++; return e.A > 1; }); int ca = _c;
            _c = 0; bool a11b = l11.All(e => { _c++; return e.A < 3; }); int cb = _c;
            _c = 0; int a11c = l11.First(e => { _c++; return e.A > 2; }).A; int cc = _c;
            _c = 0; bool a11d = l11.FirstOrDefault(e => { _c++; return e.A > 9; }) == null; int cd = _c;
            _c = 0; int a11e = l11.Select(e => { _c++; return e.A; }).Take(2).Sum(); int ce = _c;
            _c = 0; int a11f = l11.Where(e => { _c++; return e.A % 2 == 0; }).First().A; int cf = _c;
            _c = 0; int a11g = l11.Where(e => { _c++; return e.A % 2 == 0; }).Take(2).Count(); int cg = _c;
            s += "A11=" + a11a + ca + "," + a11b + cb + "," + a11c + cc + "," + a11d + cd + "," + a11e + ce + "," + a11f + cf + "," + a11g + cg + ";";

            // A12：数値の集計（int・long・float・double、負の数・大きな値・空）
            var l12 = new[] { new { I = -3, L = 5000000000L, F = 1.5f, D = 0.25 }, new { I = 7, L = -1L, F = -2f, D = 1e9 } }.ToList();
            s += "A12=" + l12.Sum(e => e.I) + "," + l12.Min(e => e.I) + "," + l12.Max(e => e.I) + "," + l12.Average(e => e.I) + "," + l12.Sum(e => e.L) + "," + l12.Max(e => e.L) + "," + l12.Sum(e => e.F) + "," + l12.Min(e => e.F) + ","
                 + l12.Average(e => e.F) + "," + l12.Sum(e => e.D) + "," + l12.Max(e => e.D) + "," + none.Sum(e => e.A * 2L) + ";";

            // C1：匿名型のリストの LINQ の中から、別の匿名型のリストの LINQ を呼ぶ
            var others = nums.Skip(3).Select(x => new { A = x, B = x * 2 }).ToList();
            var l1 = nums.Select(x => new { A = x }).ToList();
            s += "C1=" + l1.Where(r => others.Any(o => o.A == r.A)).Count() + "," + l1.Count(r => others.Where(o => o.B > r.A * 3).Any()) + ","
                 + string.Join("", l1.OrderByDescending(r => others.OrderBy(o => o.B).First().A + r.A).Take(2).Select(r => r.A)) + ";";

            // C2・C3：ジェネリックメソッドに渡す・返してもらう
            var cp = AnGen.Copy(AnGen.Copy(l1));
            s += "C2=" + FnGen.CountIf(l1, r => r.A > 4) + "," + cp.Count + (cp[0] == l1[0]) + cp[5].A + "," + FnGen.Map(l1, r => new { r.A, D = r.A * 2 })[2].D + ";";

            // C4：匿名型を使うデリゲートの変数で絞り、匿名型のリストに射影する
            var lim = new { Min = 2, Max = 4 };
            Predicate<int> inRange = x => x >= lim.Min && x <= lim.Max;
            Func<int, bool> odd = x => x % 2 == 1 && x > lim.Min;
            var c4 = nums.FindAll(inRange).Select(x => new { V = x * 10 }).ToList();
            s += "C4=" + string.Join("", c4.Select(e => e.V)) + "," + nums.Where(odd).Select(x => new { V = x }).ToList().Count + ";";

            // C5：ラムダ・匿名メソッドが返す匿名型のリストを、ジェネリックメソッドで受ける
            List<int> src = nums;
            var b1 = AnGen.Build(() => src.Select(x => new { P = x }).ToList());
            var b2 = AnGen.Build(delegate { return src.Take(2).Select(x => new { P = x + 1 }).ToList(); });
            s += "C5=" + b1.Count + b1[5].P + "," + b2.Count + b2[1].P + ";";

            // C6・C7：普通のクラスの中で使う・コールバックで UdonSharpBehaviour 側の匿名型のリストを読む
            var user = new AnListUser();
            s += "C6=" + user.Run(nums) + "," + user.Apply(i => l1[i].A * 2, 3) + "," + user.Total(l1.Select(r => r.A).ToList()) + ";";

            // C8：匿名型のリストをメンバーにする匿名型（3 層）
            var outer = new { Rows = l1, Filtered = l1.Where(r => r.A > 4).ToList(), Tag = "t" };
            var top = new { Inner = outer, N = outer.Filtered.Count };
            s += "C8=" + outer.Rows.Count + outer.Filtered[0].A + "," + top.Inner.Filtered.Last().A + top.N + "," + top.Inner.Rows.Where(r => r.A < 3).Sum(r => r.A) + ";";

            // C9：匿名型のリストのリスト（外側の Contains・IndexOf は、リストの参照の比較なので使える）
            var groups = new[] { l1, outer.Filtered }.ToList();
            int g = 0;
            foreach (var grp in groups)
                foreach (var r in grp)
                    g += r.A;
            s += "C9=" + groups.Count + groups[1][0].A + "," + g + "," + groups.Contains(l1) + groups.IndexOf(outer.Filtered) + "," + groups.SelectMany(x => x).Count() + ";";

            // C10：partial の別のファイル
            s += "C10=" + PartList(nums) + ";";

            // C11：プリプロセッサ（有効な側だけから作る）
#if true
            var pl = nums.Select(x => new { On = x }).ToList();
#else
            var pl = nums.Select(x => new { Off = x, Other = 1 }).ToList();
#endif
            s += "C11=" + pl.Count + pl[0].On + ";" + EqMore();
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#71 anonymous lists\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#71 anonymous lists"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #71 anon lists " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
