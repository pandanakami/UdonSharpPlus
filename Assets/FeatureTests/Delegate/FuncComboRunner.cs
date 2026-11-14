using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #125 の追加テスト：List・LINQ のメソッドにデリゲートを渡す（評価の回数・渡したときの値・呼ぶ回数）と、ほかの機能との組み合わせ
    public partial class FuncComboRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "True,4,2;False,0,False,112;44414466,1,3;634152,60,1.75,30,6,210,False,k2;a1b1a2b2,1,15,222;2,5,-3,642;4,26;39,3,4,16,210,69,42;2,7,5,7,2,a!,5;1,3,1,1;120,55,3;2003,6,1001,6";

        public FuncPeer peer;
        public FuncPeerDerived derived;
        public MtTarget target;
        public Text display;

        private int _gets;
        private Predicate<int> _fieldPred;

        private Predicate<int> GetPred() { _gets++; return x => x > 4; }
        private Func<int, bool> GetFn() { _gets++; return x => x > 2; }

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var list = new List<int> { 1, 2, 3, 4, 5, 6 };
            var two = new List<int> { 1, 2 };

            // L2：渡すデリゲートの式は 1 回だけ評価する
            _gets = 0;
            bool ex = list.Exists(GetPred());
            int cnt = list.Where(GetFn()).Count();
            s += ex + "," + cnt + "," + _gets + ";";

            // L3：呼ばれた中で元の変数・フィールドを書き換えても、渡したときのデリゲートを最後まで呼ぶ
            int swCalls = 0;
            Predicate<int> other = x => { swCalls += 100; return false; };
            Predicate<int> sw = null;
            sw = x => { swCalls++; sw = other; return false; };
            bool swAny = list.Exists(sw);
            _fieldPred = x => { swCalls++; _fieldPred = other; return x > 100; };
            int fc = list.FindAll(_fieldPred).Count;
            bool afterSw = sw(0);
            s += swAny + "," + fc + "," + afterSw + "," + swCalls + ";";

            // L4：呼ぶ回数（途中でやめるもの・全部見るもの）
            int calls = 0;
            Predicate<int> over3 = x => { calls++; return x > 3; };
            Func<int, bool> fOver3 = x => { calls++; return x > 3; };
            string cs = "";
            list.Exists(over3);
            cs += calls;
            calls = 0;
            list.Find(over3);
            cs += calls;
            calls = 0;
            list.Any(fOver3);
            cs += calls;
            calls = 0;
            list.All(fOver3);
            cs += calls;
            calls = 0;
            list.First(fOver3);
            cs += calls;
            calls = 0;
            int taken = list.Where(fOver3).Take(1).Count();
            cs += calls;
            calls = 0;
            list.Count(fOver3);
            cs += calls;
            var copy = list.ToList();
            calls = 0;
            copy.RemoveAll(over3);
            cs += calls;
            s += cs + "," + taken + "," + copy.Count + ";";

            // L6・L7：LINQ の役割ごとに別の Func の変数（1 つのクエリに複数の変数）
            Func<int, bool> filter = x => x % 2 == 0;
            Func<int, int> k1 = x => x % 3;
            Func<int, int> k2 = x => -x;
            Func<int, int> map = x => x * 10;
            Func<int, float> half = x => x / 2f;
            Func<int, string> keyOf = x => "k" + x;
            string ord = "";
            foreach (int v in list.OrderBy(k1).ThenBy(k2))
                ord += v;
            var dict = list.ToDictionary(keyOf, map);
            s += ord + "," + list.Where(filter).OrderBy(k2).Select(map).First() + "," + list.Average(half) + "," + dict["k3"] + "," + dict.Count + ","
                 + list.Select(map).Sum() + "," + list.All(filter) + "," + list.Where(filter).Select(keyOf).First() + ";";

            // C1：つないだデリゲート（#122）を渡す。全部が呼ばれ、判定・変換には最後の戻り値を使う
            string mlog = "";
            Action<int> act = x => mlog += "a" + x;
            act += x => mlog += "b" + x;
            two.ForEach(act);
            int side = 0;
            Predicate<int> mp = x => { side++; return true; };
            mp += x => { side += 10; return x > 1; };
            Func<int, int> mf = x => { side += 100; return x; };
            mf += x => x * 5;
            s += mlog + "," + two.FindAll(mp).Count + "," + two.Select(mf).Sum() + "," + side + ";";

            // C2：外の変数を使うラムダ（#121）。渡す前の変更・呼ばれている途中の変更・ループの回ごとの変数
            int th = 1;
            Func<int, bool> gt = x => x > th;
            th = 4;
            int c1 = list.Count(gt);
            Func<int, bool> moving = x => { th--; return x > th; };
            th = 3;
            int c2 = list.Count(moving);
            var preds = new Func<int, bool>[3];
            for (int i = 0; i < 3; i++)
            {
                int bound = i * 2;
                preds[i] = x => x > bound;
            }
            string c3 = "";
            foreach (Func<int, bool> p in preds)
                c3 += list.Count(p);
            s += c1 + "," + c2 + "," + th + "," + c3 + ";";

            // C3：ローカル関数・匿名メソッド（#123）→ Predicate / Func → FindAll / LINQ
            int minLf = 2;
            bool Big(int x) => x > minLf;
            Predicate<int> bigP = Big;
            Func<int, bool> bigF = Big;
            Func<int, int> anonF = delegate (int x) { return x + minLf; };
            s += list.FindAll(bigP).Count + "," + list.Where(bigF).Select(anonF).Sum() + ";";

            // C4：インスタンスのメソッド（#124）。普通のクラス・UdonSharpBehaviour、public / private、作ったときの対象、virtual / override / base
            var bx = new FnBox(2);
            Func<int, int> get = bx.Get;
            Predicate<int> over = bx.MakeOver();
            FnBox moved = bx;
            Func<int, int> g2 = moved.Get;
            moved = new FnBox(100);
            bx.v = 3;
            peer.hp = 10;
            derived.hp = 1;
            Func<int, int> pub = peer.Pub;
            Func<int, int> priv = peer.MakePriv();
            FuncPeer asBase = derived;
            Func<int, int> rate = asBase.Rate;
            s += list.Select(get).Sum() + "," + list.FindAll(over).Count + "," + list.Select(g2).First() + "," + list.Select(pub).Max() + ","
                 + list.Select(priv).Sum() + "," + list.Select(rate).Sum() + "," + list.Select(derived.BaseRate()).Sum() + ";";

            // C5：インタフェース（普通のクラスの実装・UdonSharpBehaviour の実装）・ジェネリッククラス（具体型ごと・中の Func<T, T>）
            target.hp = 7;
            ICmShape sh1 = new MtShapeB();
            ICmShape sh2 = target;
            Func<int> a1 = sh1.Area;
            Func<int> a2 = sh2.Area;
            var gi = new MtGen<int>(5, 1);
            var gs = new MtGen<string>("a", 2);
            Func<int, int> ci = gi.Count;
            Func<int, int> cs2 = gs.Count;
            var hi = new FnHolder<int>();
            hi.f = x => x + 1;
            var hs = new FnHolder<string>();
            hs.f = t => t + "!";
            s += a1() + "," + a2() + "," + two.Select(ci).Sum() + "," + two.Select(cs2).Sum() + "," + hi.Apply(1) + "," + hs.Apply("a") + "," + hi.Map(two).Sum() + ";";

            // C6：入れ子（デリゲートの中から、同じ型のデリゲートを List・LINQ に渡す）
            var groups = new List<int> { 1, 2, 3 };
            Func<int, bool> innerF = y => y % 2 == 0;
            Func<int, bool> outerF = x => list.Any(innerF) && list.Count(innerF) == x;
            Predicate<int> innerP = y => y > 4;
            Predicate<int> outerP = x => list.Exists(innerP) && list.FindAll(innerP).Count == x;
            s += groups.Count(outerF) + "," + groups.First(outerF) + "," + groups.FindAll(outerP).Count + "," + groups.FindIndex(outerP) + ";";

            // C7：自分を呼ぶ Func・同じ型の別のデリゲートを呼ぶ Func
            Func<int, int> fact = null;
            fact = n => n <= 1 ? 1 : n * fact(n - 1);
            Func<int, int> fib = null;
            Func<int, int> fibBody = n => n < 2 ? n : fib(n - 1) + fib(n - 2);
            fib = fibBody;
            s += fact(5) + "," + fib(10) + "," + two.Select(fact).Sum() + ";";

            // C8：partial の別のファイル・別の UdonSharpBehaviour
            s += PartUse();
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#125 List / LINQ combo\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#125 List / LINQ combo"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #125 combo " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
