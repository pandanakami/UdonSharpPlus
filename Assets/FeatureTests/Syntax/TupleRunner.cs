using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #82：普通のクラスの中のタプル（フィールド・引数・戻り値・既定値）
    public class TpBox
    {
        public (int, string) pair;                 // 初期化子なし：C# と同じく (0, null)
        public (int lo, int hi) range = (1, 9);

        public (int, int) Swap((int, int) p) => (p.Item2, p.Item1);

        public (int min, int max) MinMax(List<int> l)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (int x in l)
            {
                if (x < lo) lo = x;
                if (x > hi) hi = x;
            }
            return (lo, hi);
        }

        public string Describe() => pair.Item1 + "," + (pair.Item2 == null) + "," + range;
    }

    // ジェネリッククラス・ジェネリックメソッド（型引数を使うタプル）
    public class TpGen<T>
    {
        public (T, int) Wrap(T v) => (v, 1);
        public T First((T, T) p) => p.Item1;
        public (T value, bool ok) Try(List<T> l) => l.Count > 0 ? (l[0], true) : (default(T), false);
    }

    public static class TpUtil
    {
        public static (T, U) Make<T, U>(T a, U b) => (a, b);
        public static T Pick<T>((T, T) p, bool second) => second ? p.Item2 : p.Item1;
    }

    // #82 タプル
    public class TupleRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "T1=1a2b2,74;T2=(1, a)|(1, (2.5, x), True, c, )|(2, b)|(1, 2);T3=TrueTrueTrueTrueFalseTrue;T4=3210202120,12.5x;T5=122,5002;T6=(1, 2)(11, 2)(4, 4);T7=0True,0,0False2,(0, 0),(0, 0);T8=bob90,bob90cid80,240,31,True2False2;T9=0,True,(1, 9),(2, 1),18,(w, 1)f,0False(5, True),(1, m)4;T10=2{ Pos = (1, 2), Name = n },12({ A = 1 }, 2),(3, 9)82;T11=728(1, 2, 3, 4, 5, 6, 7)";

        public Text display;

        private (int x, int y) _pos = (3, 4);
        private (string, int) _unset;
        private List<(string name, int score)> _scores = new List<(string name, int score)>();
        private int _c;

        private int Next() => ++_c;
        private (int, int) Pair() => (10, 20);
        private (int q, int r) DivMod(int a, int b) => (a / b, a % b);

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 5, 3, 8, 1 };

            // T1：作る・要素を読む（Item1・名前）・名前の省略（変数名から）
            var t = (1, "a");
            (int n, string w) named = (2, "b");
            int cnt = 7;
            var inferred = (cnt, nums.Count);
            s += "T1=" + t.Item1 + t.Item2 + named.n + named.w + named.Item1 + "," + inferred.cnt + inferred.Count + ";";

            // T2：ToString・連結・補間（入れ子・null・float・bool・char）
            string ns = null;
            var nested = (1, (2.5f, "x"), true, 'c', ns);
            s += "T2=" + t.ToString() + "|" + nested + "|" + $"{named}" + "|" + (1, 2) + ";";

            // T3：==・!=・Equals（名前が違っても同じ型）
            (int a, int b) p1 = (1, 2);
            (int c, int d) p2 = (1, 2);
            var p3 = (1, 3);
            s += "T3=" + (p1 == p2) + (p1 != p3) + (p1 == (1, 2)) + p1.Equals(p2) + p1.Equals(p3) + (nested == (1, (2.5f, "x"), true, 'c', null)) + ";";

            // T4：分解（var・型を書く・既存の変数への代入・入れ替え・捨てる・入れ子）
            var (q, r) = DivMod(17, 5);
            (int x1, var y1) = Pair();
            int u = 1, v = 2;
            (u, v) = (v, u);
            var (_, second) = Pair();
            var (o1, (o2, o3), _, _, _) = nested;
            s += "T4=" + q + r + x1 + y1 + u + v + second + "," + o1 + o2 + o3 + ";";

            // T5：右辺は 1 回だけ評価し、全部取り出してから代入する（左から）
            _c = 0;
            var (e1, e2) = (Next(), Next());
            int[] arr = { 0, 0, 0 };
            int idx = 0;
            (arr[idx], idx) = (5, 2);
            s += "T5=" + e1 + e2 + _c + "," + string.Join("", arr) + idx + ";";

            // T6：値型のように、丸ごとの代入はコピー（作り直して代入しても、元は変わらない）
            var orig = (1, 2);
            var copy = orig;
            copy = (copy.Item1 + 10, copy.Item2);
            _pos = (_pos.x + 1, _pos.y);
            s += "T6=" + orig + copy + _pos + ";";

            // T7：既定値（default・初期化子のないフィールド・new (int, string)[n]・new ValueTuple<,>()）
            (int, string) def = default;
            var arrT = new (int, bool)[2];
            var vt = new ValueTuple<int, int>();
            s += "T7=" + def.Item1 + (def.Item2 == null) + "," + _unset.Item2 + _unset.Item1 + "," + arrT[1].Item1 + arrT[1].Item2 + arrT.Length + "," + vt + "," + default((int, int)) + ";";

            // T8：List・LINQ（射影・Where・OrderBy・Sum・foreach の分解・値の比較）
            _scores.Clear();
            _scores.Add(("ann", 70));
            _scores.Add(("bob", 90));
            _scores.Add(("cid", 80));
            var best = _scores.OrderByDescending(e => e.score).First();
            string ord = "";
            foreach (var (nm, sc) in _scores.Where(e => e.score >= 80))
                ord += nm + sc;
            var pairs = nums.Select((x, i) => (x, i)).Where(e => e.x > 2).ToList();
            s += "T8=" + best.name + best.score + "," + ord + "," + _scores.Sum(e => e.score) + "," + pairs.Count + pairs[1].i + ","
                 + _scores.Contains(("bob", 90)) + _scores.IndexOf(("cid", 80)) + _scores.Contains(("bob", 1)) + nums.Select(x => (x % 2, 0)).Distinct().Count() + ";";

            // T9：メソッドの引数・戻り値・普通のクラス・ジェネリック
            var box = new TpBox();
            var mm = box.MinMax(nums);
            var gen = new TpGen<string>();
            var tryEmpty = new TpGen<int>().Try(new List<int>());
            var tryOne = new TpGen<int>().Try(nums);
            s += "T9=" + box.Describe() + "," + box.Swap((1, 2)) + "," + mm.min + mm.max + "," + gen.Wrap("w") + gen.First(("f", "g")) + "," + tryEmpty.value + tryEmpty.ok + tryOne + ","
                 + TpUtil.Make(1, "m") + TpUtil.Pick((3, 4), true) + ";";

            // T10：匿名型・デリゲート・ラムダの中
            var anon = new { Pos = (1, 2), Name = "n" };
            var tup = (new { A = 1 }, 2);
            Func<int, (int, int)> sq = x => (x, x * x);
            int tot = 0;
            nums.ForEach(x => { var (a1, b1) = sq(x); tot += b1 - a1; });
            s += "T10=" + anon.Pos.Item2 + anon + "," + tup.Item1.A + tup.Item2 + tup + "," + sq(3) + tot + ";";

            // T11：7 個の要素・入れ子の分解
            var seven = (1, 2, 3, 4, 5, 6, 7);
            var (s1, s2, s3, s4, s5, s6, s7) = seven;
            s += "T11=" + seven.Item7 + (s1 + s2 + s3 + s4 + s5 + s6 + s7) + seven;
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#82 tuples\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#82 tuples"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #82 tuples " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
