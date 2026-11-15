using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #141：普通のクラスの中で、List・LINQ の引数に匿名メソッドを直接書く
    public class AmUser
    {
        public string Run(List<int> l)
        {
            int k = 2;
            return l.FindAll(delegate (int x) { return x > k; }).Count + "" + l.Where(delegate (int x) { return x % 2 == 1; }).Sum();
        }
    }

    // ジェネリックメソッドの中
    public static class AmGen
    {
        public static int CountAll<T>(List<T> l) => l.Count(delegate (T x) { return true; }) + l.FindAll(delegate { return true; }).Count;
    }

    // #141 の追加テスト：List・LINQ の引数に直接書いた匿名メソッド（delegate (int x) { ... }）
    public class AnonMethodRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "M1=21,123456,6;M2=54321,True,2531;M3=False662;M4=one,two,three,n4,n9,n9;M5=136101521,216,54,3;M6=True2,False3,33,23,False0;M7=123,23,x1,x2,z3;MC1=10,31,52;MC2=21,345;MC3=26,26;MC4=962;MC5=49,126;MC6=3;MC7=341;MC8=5";

        public Text display;

        private int _c;

        private bool Over(int x) => x > 5;

        private int CountOver(List<int> l, int limit) => l.Count(delegate (int x) { return x > limit; });

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };
            var boxes = new List<LmBox>
            {
                new LmBox("e", new List<int>(), new string[0]),
                new LmBox("x", new List<int> { 1, 2 }, new[] { "p", "q" }),
                new LmBox("z", new List<int> { 3 }, new[] { "r" }),
            };

            // M1：ForEach（戻り値なし。呼ぶ順・全部の要素・外の変数への書き込み）
            int sum = 0;
            string order = "";
            nums.ForEach(delegate (int x) { sum += x; order += x; });
            int cnt = 0;
            nums.ForEach(delegate { cnt++; });
            s += "M1=" + sum + "," + order + "," + cnt + ";";

            // M2：Sort（引数 2 つ）・RemoveAll
            var l2 = new List<int> { 3, 1, 2, 5, 4 };
            _c = 0;
            l2.Sort(delegate (int a, int b) { _c++; return b - a; });
            bool compared = _c > 0;
            string sorted = string.Join("", l2);
            int removed = l2.RemoveAll(delegate (int x) { return x % 2 == 0; });
            s += "M2=" + sorted + "," + compared + "," + removed + string.Join("", l2) + ";";

            // M3：引数の並びを省いた形（引数 1 つ・2 つの場所）
            var l3 = new List<int> { 2, 1 };
            l3.Sort(delegate { return 0; });
            s += "M3=" + nums.Exists(delegate { return false; }) + nums.FindAll(delegate { return true; }).Count + nums.Count(delegate { return true; }) + l3.Count + ";";

            // M4：複数の return・if / switch / for・ローカル変数・break / continue
            var kinds = nums.Select(delegate (int x)
            {
                if (x == 1)
                    return "one";
                switch (x)
                {
                    case 2:
                        return "two";
                    case 3:
                    {
                        string t = "th";
                        return t + "ree";
                    }
                }
                int acc = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (i % 2 == 0)
                        continue;
                    if (i > x)
                        break;
                    acc += i;
                }
                return "n" + acc;
            }).ToList();
            s += "M4=" + string.Join(",", kinds) + ";";

            // M5：外の変数の読み書き（次の呼び出しに見える）・foreach の変数・メソッドの引数
            int run = 0;
            var seen = new List<int>();
            var m5 = nums.Select(delegate (int x) { run += x; seen.Add(run); return run; }).ToList();
            string m5b = "";
            foreach (int k in nums.Take(2))
                m5b += nums.Count(delegate (int x) { return x > k; });
            s += "M5=" + string.Join("", m5) + "," + run + seen.Count + "," + m5b + "," + CountOver(nums, 3) + ";";

            // M6：途中で止まるメソッドの、匿名メソッドを呼ぶ回数（空のリストも）
            _c = 0; bool a6 = nums.Any(delegate (int x) { _c++; return x > 1; }); int c6a = _c;
            _c = 0; bool b6 = nums.All(delegate (int x) { _c++; return x < 3; }); int c6b = _c;
            _c = 0; int f6 = nums.First(delegate (int x) { _c++; return x > 2; }); int c6c = _c;
            _c = 0; int t6 = nums.TakeWhile(delegate (int x) { _c++; return x < 3; }).Count(); int c6d = _c;
            _c = 0; bool e6 = new List<int>().Any(delegate (int x) { _c++; return true; }); int c6e = _c;
            s += "M6=" + a6 + c6a + "," + b6 + c6b + "," + f6 + c6c + "," + t6 + c6d + "," + e6 + c6e + ";";

            // M7：SelectMany（List・LINQ の結果を返す匿名メソッド、結果を作る側の匿名メソッド、空の内側）
            s += "M7=" + string.Join("", boxes.SelectMany(delegate (LmBox b) { return b.items; })) + "," + string.Join("", boxes.SelectMany(delegate (LmBox b) { return b.items.Where(i => i > 1); })) + ","
                 + string.Join(",", boxes.SelectMany(b => b.items, delegate (LmBox b, int i) { return b.name + i; })) + ";";

            // MC1：添字付き（#142）
            s += "MC1=" + string.Join(",", nums.Where(delegate (int x, int i) { return i % 2 == 0; }).Select(delegate (int x, int i) { return x * 10 + i; })) + ";";

            // MC2：Aggregate・SkipWhile・TakeWhile・string.Join につなぐ
            s += "MC2=" + nums.Aggregate(0, delegate (int a, int x) { return a + x; }) + "," + string.Join("", nums.SkipWhile(delegate (int x) { return x < 3; }).TakeWhile(delegate (int x) { return x < 6; })) + ";";

            // MC3：デリゲートの変数に入れて渡す形（#125）と、直接書く形で、結果・呼ぶ回数が同じ
            Func<int, bool> big = delegate (int x) { _c++; return x > 4; };
            _c = 0; int v1 = nums.Count(big); int cv1 = _c;
            _c = 0; int v2 = nums.Count(delegate (int x) { _c++; return x > 4; }); int cv2 = _c;
            s += "MC3=" + v1 + cv1 + "," + v2 + cv2 + ";";

            // MC4：匿名メソッドの中で匿名型を作る・読む
            var rows = nums.Select(delegate (int x) { var p = new { A = x, B = x * x }; return p; }).ToList();
            s += "MC4=" + rows[2].B + rows.Count(delegate { return true; }) + rows.FindAll(r => r.A > 4).Count + ";";

            // MC5：普通のクラス・ジェネリックメソッドの中
            s += "MC5=" + new AmUser().Run(nums) + "," + AmGen.CountAll(nums) + AmGen.CountAll(boxes) + ";";

            // MC6：匿名メソッドの中から、別の LINQ と匿名メソッドを呼ぶ（引数・ローカル変数が混ざらない）
            s += "MC6=" + nums.Count(delegate (int x) { int local = x * 2; return nums.Any(delegate (int y) { int inner = y + 1; return inner == local; }); }) + ";";

            // MC7：インタフェース・普通のクラス・UdonSharpBehaviour のメソッドを、匿名メソッドから呼ぶ
            FeatureTests.Anon.IAnShape sh = new FeatureTests.Anon.AnSquare();
            var it = new FeatureTests.Anon.AnItem("n", 2);
            s += "MC7=" + nums.Count(delegate (int x) { return x > sh.Area() - 6; }) + nums.Count(delegate (int x) { return x > it.Hp; }) + nums.Count(delegate (int x) { return Over(x); }) + ";";

            // MC8：プリプロセッサ
#if true
            s += "MC8=" + nums.Count(delegate (int x) { return x > 1; });
#else
            s += "MC8=" + nums.Count(delegate (int x) { return x > 100; }) + "off";
#endif
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#141 anonymous methods\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#141 anonymous methods"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #141 anon methods " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
