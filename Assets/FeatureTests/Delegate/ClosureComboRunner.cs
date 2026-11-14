using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #129: クロージャ（外の変数を使うラムダ）と、ほかの機能（再帰・別の UdonSharpBehaviour・インタフェース・ジェネリクス・
    // List / Dictionary・LINQ・[RecursiveMethod]・ループ・いろいろな型と演算子・switch・継承・partial・イベント）の組み合わせを
    // Udon 上で実行し、普通の C# の結果と比べて OK/NG を表示する
    public partial class ClosureComboRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "120,55;215,113;1006,12;8,4,12,8,2,4,2;2,3,20,4;123;0,1,10,11;3,True,xy,b,35,4398046511104,2,none,RerunButton,66,7,8,8,7,16,5,1.5;n2:big,30,400,n0:small,-1,6;5,8,-2,1008,10,5;1607,6,43;1,2";

        public ClosureComboRelay relay;
        public ClosureComboDerived derived;
        public Text display;

        private Calc _later;

        // C3：Start で作り、別のイベント（1 フレームあとの Run・Interact）で呼ぶ
        private void Start()
        {
            int startVal = 41;
            _later = x => x + startVal;
            startVal++;
            SendCustomEventDelayedFrames(nameof(Run), 1);
        }

        public override void Interact() { Run(); }

        // A6：再帰しながらクロージャを作る（呼び出しごとに別の環境）
        [RecursiveMethod]
        private int Nest(int depth, Calc acc)
        {
            if (depth == 0)
                return acc(0);
            Calc next = x => acc(x * 10 + depth);
            return Nest(depth - 1, next);
        }

        private string Scenario()
        {
            string s = "";

            // A1：自分を呼ぶローカルのラムダ
            Calc fact = null;
            fact = n => n <= 1 ? 1 : n * fact(n - 1);
            Calc fib = null;
            fib = n => n < 2 ? n : fib(n - 1) + fib(n - 2);
            s += fact(5) + "," + fib(10) + ";";

            // A2：外の変数を使うラムダから、同じ型の別の環境のラムダを呼び、戻ってから自分の変数を使う（別の UdonSharpBehaviour を通る場合も）
            int a2 = 100;
            int mul = 3;
            Calc inner = x => x * mul;
            Calc outerA = x => { int before = a2; int r = inner(x); return before + r + a2; };
            Calc viaRelay = x => relay.CallBack(y => y + a2 + x, x) + x;
            s += outerA(5) + "," + viaRelay(2) + ";";

            // A3：インタフェース型の変数（UdonSharpBehaviour の実装から、普通のクラスの実装に入れ替える）
            IClosureTarget target = relay;
            Calc viaIf = x => target.Take(y => y + 1, x);
            s += viaIf(5) + ",";
            target = new ClosureTaker();
            s += viaIf(5) + ";";

            // A4：ジェネリッククラス・Dictionary・配列・List を、ラムダの中で書き換える
            var box = new ClosureBox<int>(5);
            var dict = new Dictionary<string, int>();
            int[] arrA = new int[3];
            Calc fill = x => { box.value += x; dict["k"] = x * 2; arrA[x % 3] = x; return box.value; };
            fill(1); fill(2);
            s += box.value + "," + dict["k"] + "," + arrA[1] + arrA[2] + "," + fill(0) + ",";
            var lst = new List<int>();
            Calc add = x => { lst.Add(x); return lst.Count; };
            add(3); add(4);
            s += lst.Count + "," + lst[1] + ",";
            lst = new List<int> { 9 };
            s += add(1) + ";";

            // A5：ラムダの中の LINQ で外の変数を使う・元のメソッドの LINQ でも使う
            int threshold = 2;
            var nums = new List<int> { 1, 2, 3, 4 };
            Calc countOver = x => nums.Where(v => v > threshold + x).Count();
            Calc sumMul = x => nums.Select(v => v * x + threshold).Sum();
            s += countOver(0) + ",";
            threshold = 0;
            s += countOver(1) + "," + sumMul(2) + "," + nums.Count(v => v > threshold) + ";";

            // A6：[RecursiveMethod] のメソッドが、再帰しながら引数を使うクロージャを作る
            s += Nest(3, x => x) + ";";

            // A7：入れ子のループの、外と内の両方の変数
            var grid = new Calc[4];
            for (int i = 0; i < 2; i++)
            {
                int ii = i;
                for (int j = 0; j < 2; j++)
                {
                    int jj = j;
                    grid[i * 2 + j] = x => x + ii * 10 + jj;
                }
            }
            s += grid[0](0) + "," + grid[1](0) + "," + grid[2](0) + "," + grid[3](0) + ";";

            // B1：いろいろな型と演算子（値を使うところも）
            float f = 1.5f; bool flag = false; string str = null; char ch = 'a'; byte by = 25; long lg = 1L << 40; ClosureKind kind = ClosureKind.None;
            Tick ops = () => { f *= 2; flag = !flag; str ??= "x"; str += "y"; ch++; by += 10; lg <<= 2; kind = ClosureKind.Ice; };
            ops();
            s += f + "," + flag + "," + str + "," + ch + "," + by + "," + lg + "," + (int)kind + ",";
            GameObject go = null;
            ShowText goName = () => go == null ? "none" : go.name;
            s += goName() + ",";
            go = gameObject;
            s += goName() + ",";
            int cnt = 5;
            Calc vc = x => (cnt += x) * 10 + cnt++;
            s += vc(1) + "," + cnt + "," + (++cnt) + "," + (cnt--) + "," + cnt + ",";
            int md = 17;
            Tick m2 = () => { md %= 5; md <<= 3; };
            m2();
            float fv = 0.5f;
            Calc fpost = x => (int)(fv++ * 10);
            s += md + "," + fpost(0) + "," + fv + ";";

            // B2：文字列補間・switch 文・三項演算子 / B3：switch の中のブロックで宣言した変数
            int sw = 2; string nm = "n";
            ShowText interp = () => $"{nm}{sw}:{(sw > 1 ? "big" : "small")}";
            Calc swc = x => { switch (x + sw) { case 3: return 30; case 4: return 40; default: return -1; } };
            Calc swe = x => x + sw == 3 ? 300 : x + sw == 4 ? 400 : -100;   // switch 式は UdonSharp が未対応
            s += interp() + "," + swc(1) + "," + swe(2) + ",";
            sw = 0;
            s += interp() + "," + swc(1) + ",";
            Calc fromSwitch = null;
            switch (sw)
            {
                case 0:
                {
                    int k3 = 5;
                    fromSwitch = x => x + k3;
                    break;
                }
                default:
                    break;
            }
            s += fromSwitch(1) + ";";

            // C1：普通のクラスのフィールド・デリゲートの配列・インタフェースのメソッドの引数・別の UdonSharpBehaviour に入れて、向こうで呼ぶ
            int bonusC = 3;
            var bag = new ClosureBag();
            bag.calc = x => x + bonusC;
            var arrC = new Calc[] { x => x * bonusC, x => x - bonusC };
            bonusC = 4;
            s += bag.Use(1) + "," + arrC[0](2) + "," + arrC[1](2) + "," + relay.Take(x => x * bonusC, 2) + ","
                 + ((IClosureTarget)new ClosureTaker()).Take(x => x + bonusC, 1) + ",";
            relay.Store(x => x + bonusC);
            bonusC = 5;
            s += relay.CallStored(0) + ";";

            // C2：基底と派生の両方・partial のもう 1 つのファイル / C3：Start で作ったもの
            s += derived.DerivedMake(3) + "," + PartMake(4) + "," + _later(1) + ";";

            // C4：兄弟のブロックの、同じ名前の変数
            Calc s1, s2;
            {
                int kk = 1;
                s1 = x => x + kk;
            }
            {
                int kk = 2;
                s2 = x => x + kk;
            }
            s += s1(0) + "," + s2(0);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#129 closure combo\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#129 closure combo"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #129 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
