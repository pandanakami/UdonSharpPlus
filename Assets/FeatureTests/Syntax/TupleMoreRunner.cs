using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #82 の追加テスト：インタフェースの引数・戻り値にタプル
    public interface ITpShape
    {
        (int w, int h) Size();
        int Area((int w, int h) s);
    }

    public class TpRect : ITpShape
    {
        public (int w, int h) size = (2, 3);
        public (int w, int h) Size() => size;
        public int Area((int w, int h) s) => s.w * s.h;
    }

    // 普通のクラス：フィールド（初期化子なし）・List・Dictionary の値
    public class TpStore
    {
        public (int, string) last;
        public List<(int id, string name)> rows = new List<(int id, string name)>();
        public Dictionary<string, (int, int)> byName = new Dictionary<string, (int, int)>();

        public void Put(int id, string name)
        {
            rows.Add((id, name));
            byName[name] = (id, id * 10);
            last = (id, name);
        }
    }

    public class TpHolder<T>
    {
        public T value;                              // T がタプルなら、既定値は各要素が既定値のタプル
        [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */ public List<T> items = new List<T>();
        public (T, T) Twice(T v) => (v, v);
    }

    public static class TpGen2
    {
        public static (U, T) Flip<T, U>((T, U) p) => (p.Item2, p.Item1);
        public static (T, int) Tag<T>(T v, int n) => (v, n);
        public static T Id<T>(T v) => v;
    }

    public delegate (int, int) TpMaker(int x);

    // #82 の追加テスト（U：機能単体、C：組み合わせ）
    public partial class TupleMoreRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "U1=(101, m!)(1, m),nn(2, (4, nn)),(6, 5)s5,5;U3=FalseTrueTrue,TrueFalseFalse;U4=TrueTrueFalseFalse;U5=78003,1202;U6=0True(0, False),0True,(0, 0),0True,(0, 0)(0, 0)(0, 0);U7=12(1, 1)(4, 4),(7, 7);U8=(, (Infinity, NaN), (1.00, 2.00, 3.00), -0.5)|(Infinity, NaN)|<(1, 2)(Infinity, NaN);U9=TrueFalse02-120-1,True3(1, a),213;U10=c,a,b|a2,b2,c1;U11=123.54True6(7, 8),123.54True678,TrueTrue8;C1=5(4, 8)(5, 0)(6, -6)(2, 3)a1(7, p7);C2=301122,301122,213,0120,(6, 3),2,2;C3={ P = (1, 2), Q = q }True,({ A = 1, B = b }, 3)True,(2, 2)(1, 1)True;C4=(1, x)((t, 1), 2)t,(0, 0)2(3, 4)(5, 6)(w, w);C5=(2, 3)206,(2, two)2one;C6=(1, 10)True(2, 20)(3, 9)3,3(2, two)2;C7=(3, 9)3,(1, r)1,0(0, 0)";

        public Text display;
        public TupleMorePeer peer;

        public (int, string) publicPair;             // public のフィールド（初期化子なし）
        private (float, bool) _privatePair;
        private (int, int)[] _arr = new (int, int)[2];
        private int _c;
        private int[] _slots = new int[4];

        private int Next() => ++_c;
        private int Slot() { _c++; return _c - 1; }
        private (int, int) Make(int a, int b) { _c++; return (a, b); }
        private (int, int) Local(int x) => (x, -x);

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 3, 1, 2 };

            // U1：別の UdonSharpBehaviour に渡す・返してもらう（呼び先で作り直しても、呼び元は変わらない）
            var mine = (1, "m");
            var got = peer.Echo(mine);
            var nest = peer.Nest((1, (2, "n")));
            var swapped = peer.Swap((5, 6));
            s += "U1=" + got + mine + "," + nest.Item2.Item2 + nest + "," + swapped + peer.lastSeen + "," + peer.FromItem(new TpRect().Size()) + ";";

            // U3：== と Equals の違い（NaN・参照型）
            var n1 = (float.NaN, 1);
            var n2 = (float.NaN, 1);
            var rect = new TpRect();
            var r1 = (rect, "a");
            var r2 = (rect, "a");
            var r3 = (new TpRect(), "a");
            s += "U3=" + (n1 == n2) + n1.Equals(n2) + (n1 != n2) + "," + (r1 == r2) + (r1 == r3) + r1.Equals(r3) + ";";

            // U4：名前だけ違うタプルは同じ型、型・順・数が違えば別の型（Equals が false）
            (int a, string b) x1 = (1, "s");
            (int c, string d) x2 = (1, "s");
            var x3 = ("s", 1);
            var x4 = (1L, "s");
            s += "U4=" + x1.Equals(x2) + (x1 == x2) + x1.Equals(x3) + x1.Equals(x4) + ";";

            // U5：分解の評価の順・回数（右辺は 1 回、受け側の添字は左から）
            _c = 0;
            (_slots[Slot()], _slots[Slot()]) = Make(7, 8);
            int c5 = _c;
            _c = 0;
            var (p5, q5) = (Next(), Next() * 10);
            s += "U5=" + string.Join("", _slots) + c5 + "," + p5 + q5 + _c + ";";

            // U6：既定値（public / private のフィールド・普通のクラスのフィールド・配列・ジェネリック・LINQ の OrDefault）
            var store = new TpStore();
            var holder = new TpHolder<(int, string)>();
            s += "U6=" + publicPair.Item1 + (publicPair.Item2 == null) + _privatePair + "," + store.last.Item1 + (store.last.Item2 == null) + "," + _arr[1] + "," + holder.value.Item1 + (holder.value.Item2 == null) + ","
                 + Enumerable.Empty<(int, int)>().FirstOrDefault() + nums.Select(v => (v, 0)).FirstOrDefault(t => t.v > 9) + nums.Select(v => (v, 0)).LastOrDefault(t => t.v > 9) + ";";

            // U7：値型のようなコピー（ローカル・引数・戻り値・List・配列）。参照型の要素の中身は共有される
            var o7 = (1, rect);
            var c7 = o7;
            c7 = (2, c7.Item2);
            var list7 = new List<(int, int)> { (1, 1) };
            var got7 = list7[0];
            got7 = (9, 9);
            _arr[0] = (4, 4);
            var a7 = _arr[0];
            a7 = (5, 5);
            rect.size = (7, 7);
            s += "U7=" + o7.Item1 + c7.Item1 + list7[0] + _arr[0] + "," + o7.Item2.size + ";";

            // U8：ToString（null の string・入れ子・NaN・Infinity・Vector3・+=）
            string ns = null;
            var t8 = (ns, (double.PositiveInfinity, float.NaN), new Vector3(1, 2, 3), -0.5);
            string acc = "<";
            acc += (1, 2);
            acc += t8.Item2;
            s += "U8=" + t8 + "|" + $"{t8.Item2}" + "|" + acc + ";";

            // U9：List の値の比較（各形・重なり・見つからない・範囲の端）・Distinct は最初に出たものを残す
            var l9 = new List<(int, string)> { (1, "a"), (2, "b"), (1, "a"), (3, "c") };
            s += "U9=" + l9.Contains((1, "a")) + l9.Contains((9, "z")) + l9.IndexOf((1, "a")) + l9.IndexOf((1, "a"), 1) + l9.IndexOf((1, "a"), 1, 1) + l9.LastIndexOf((1, "a")) + l9.LastIndexOf((1, "a"), 1)
                 + l9.IndexOf((3, "c"), 4) + "," + l9.Remove((1, "a")) + l9.Count + l9[1] + "," + string.Join("", l9.Distinct().Select(t => t.Item1)) + ";";

            // U10：Sort（比べ方を渡す）・OrderBy / ThenBy に名前を使う
            var l10 = new List<(string name, int score)> { ("b", 2), ("a", 2), ("c", 1) };
            l10.Sort((p, q) => p.score != q.score ? p.score - q.score : string.Compare(p.name, q.name));
            string sorted = string.Join(",", l10.Select(t => t.name));
            string ob = string.Join(",", l10.OrderByDescending(t => t.score).ThenBy(t => t.name).Select(t => t.name + t.score));
            s += "U10=" + sorted + "|" + ob + ";";

            // U11：7 個の要素（各要素・分解・Equals・ToString・List・LINQ）
            var seven = (1, "2", 3.5f, '4', true, 6L, (7, 8));
            var (e1, e2, e3, e4, e5, e6, (e7a, e7b)) = seven;
            var sevens = new List<(int, string, float, char, bool, long, (int, int))> { seven };
            s += "U11=" + seven.Item1 + seven.Item2 + seven.Item3 + seven.Item4 + seven.Item5 + seven.Item6 + seven.Item7 + "," + e1 + e2 + e3 + e4 + e5 + e6 + e7a + e7b + ","
                 + seven.Equals((1, "2", 3.5f, '4', true, 6L, (7, 8))) + sevens.Contains(seven) + sevens.Select(t => t.Item7.Item2).Sum() + ";";

            s += More();
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#82 tuples (more)\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#82 tuples (more)"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #82 tuples more " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
