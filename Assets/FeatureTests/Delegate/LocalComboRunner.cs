using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #123: ローカル関数・匿名メソッドと、ほかの機能（+= / -=・List・LINQ・インタフェース・継承・普通のクラス・ジェネリクス・
    // [RecursiveMethod]・コレクション・partial・制御構造・別の UdonSharpBehaviour であとから呼ぶ）の組み合わせを Udon 上で実行し、
    // 普通の C# の結果と比べて OK/NG を表示する
    public partial class LocalComboRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "3LMN,M,LMN,12,2,null;2,30,1234,2;9,3,2,12,11,10,11;155,107,23,4,5,t;6,True,True,5;3,4,5,2,2,5;2,202;42,1,201,3,6,4,-25;9";

        public LocalComboKeeper keeper;
        public LocalComboDerived derived;
        public Text display;

        private string _log = "";

        // 9：Start で作り、別の UdonSharpBehaviour に渡して、あとのフレーム（Run）で呼ぶ
        private void Start()
        {
            int startK = 7;
            int LaterAdd(int x) => x + startK;
            startK++;
            keeper.StoreLater(LaterAdd);
            SendCustomEventDelayedFrames(nameof(Run), 1);
        }

        public override void Interact() { Run(); }

        private string Trace(Calc c)
        {
            if (c == null)
                return "null";
            _log = "";
            c(0);
            return _log;
        }

        private Calc MakeLogged(int k)
        {
            int Add(int x) { _log += k; return x + k; }
            return Add;
        }

        // 5：[RecursiveMethod] のメソッドの中で、引数を使い、デリゲートを呼ぶローカル関数を作る
        [RecursiveMethod]
        private int Walk(int depth, Calc acc)
        {
            if (depth == 0)
                return acc(0);
            int Step(int x) => acc(x) + depth;
            return Walk(depth - 1, Step);
        }

        private string Scenario()
        {
            string s = "";

            // 1：ローカル関数と匿名メソッドを同じものにつなぐ・外す（同じ値だけ外れる・別の環境は外れない・元の値は変わらない）
            int k = 1;
            int AddK(int x) { _log += "L"; return x + k; }
            Calc chain = AddK;
            chain += delegate (int x) { _log += "M"; return x * 2; };
            Calc anonRef = delegate (int x) { _log += "N"; return x; };
            chain += anonRef;
            Calc before = chain;
            _log = "";
            s += chain(3) + _log + ",";
            chain -= anonRef;
            chain -= AddK;
            s += Trace(chain) + "," + Trace(before) + ",";
            Calc a1 = MakeLogged(1), a2 = MakeLogged(2);
            Calc both = a1 + a2;
            both -= MakeLogged(1);
            s += Trace(both) + ",";
            both -= a1;
            s += Trace(both) + ",";
            // 同じ場所・同じ範囲で 2 回作ったラムダは、C# と同じく同じもの（-= で外れる）
            Calc loopChain = null;
            int q = 5;
            for (int i = 0; i < 2; i++)
            {
                Calc made = x => x + q;
                if (i == 0)
                    loopChain += made;
                else
                    loopChain -= made;
            }
            s += Trace(loopChain) + ";";

            // 2：List・LINQ に、外の変数を使わないローカル関数を渡す
            var nums = new List<int> { 1, 2, 3, 4 };
            static bool IsEven(int x) => x % 2 == 0;
            int Sq(int x) => x * x;
            void LogNum(int x) { _log += x; }
            _log = "";
            nums.ForEach(LogNum);
            s += nums.Where(IsEven).Count() + "," + nums.Select(Sq).Sum() + "," + _log + "," + nums.Count(x => IsEven(x) && Sq(x) > k) + ";";

            // 3：インタフェース・override・base（派生の UdonSharpBehaviour の中）
            s += derived.Run(new LocalSquare(3)) + ";";

            // 4：普通のクラス・ジェネリッククラスの中のローカル関数
            var calc = new LocalCalc(100);
            s += calc.Fib(10) + "," + calc.Nested(3) + "," + calc.SameName(2) + ",";
            s += new LocalBox<int>(5).CountTimes(4) + "," + new LocalBox<int>(5).Get() + "," + new LocalBox<string>("t").Get() + ";";

            // 5：再帰しながらローカル関数を作る・相互再帰・デリゲートを通した再帰
            bool IsEvenR(int n) => n == 0 || IsOddR(n - 1);
            bool IsOddR(int n) => n != 0 && IsEvenR(n - 1);
            Calc countDown = null;
            int Down(int n) => n <= 0 ? 0 : 1 + countDown(n - 1);
            countDown = Down;
            s += Walk(3, x => x) + "," + IsEvenR(10) + "," + IsOddR(7) + "," + countDown(5) + ";";

            // 6：コレクションを書き換える（直接・匿名メソッド・ラムダから。途中で別のインスタンスに入れ替える）
            var lst = new List<int>();
            var dict = new Dictionary<string, int>();
            int[] arr = new int[3];
            void Put(int x) { lst.Add(x); dict["k" + x] = x * 2; arr[x % 3] += x; }
            Put(1);
            Tick put2 = delegate { Put(2); };
            put2();
            Calc viaD = x => { Put(x); return lst.Count; };
            s += viaD(4) + "," + dict["k2"] + "," + arr[1] + "," + arr[2] + ",";
            lst = new List<int> { 9 };
            Put(5);
            s += lst.Count + "," + lst[1] + ";";

            // 7：partial の 2 つのファイルの同じ名前のローカル関数
            int Helper(int x) => x + 1;
            Calc h = Helper;
            s += h(1) + "," + PartRun() + ";";

            // 8：switch（{ } で囲む）・while の 1 回ごとの変数・foreach・匿名メソッドの中の break / continue / return
            int fromSwitch = 0;
            switch (k)
            {
                case 1:
                {
                    int Twice(int x) => x * 2;
                    fromSwitch = Twice(21);
                    break;
                }
            }
            var fs = new Calc[3];
            int w = 0;
            while (w < 3)
            {
                int captured = w;
                int Get(int x) => x + captured * 100;
                fs[w] = Get;
                w++;
            }
            var fe = new Calc[3];
            foreach (int v in new int[] { 1, 2 })
            {
                int L() => v * 3;
                fe[v] = x => L() + x;
            }
            Calc early = delegate (int x)
            {
                int t = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (i == x)
                        return t;
                    if (i % 2 == 0)
                        continue;
                    t += i;
                    if (t > 20)
                        break;
                }
                return -t;
            };
            s += fromSwitch + "," + fs[0](1) + "," + fs[2](1) + "," + fe[1](0) + "," + fe[2](0) + "," + early(5) + "," + early(20) + ";";

            // 9：Start で作って別の UdonSharpBehaviour に渡したもの
            s += keeper.CallLater(1);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#123 local function combo\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#123 local function combo"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #123 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
