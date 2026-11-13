using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #126: デリゲートと、ほかの機能（インタフェース・普通のクラス・ジェネリクス・List / Dictionary・ラムダ・LINQ・継承・
    // [RecursiveMethod]・値型・Unity の型など）の組み合わせを Udon 上で実行し、普通の C# の結果と比べて OK/NG を表示する
    public class DelegateComboRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（エディタで Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "48,8,2009,1016;43;26,30,30;36,9,5;6,4,10,7;14,9,2;5101,32017;14,120,1003,1004;3,10,Unit,43;7,55;17,13;sa,3,xy;2.5;2;15;9,10,10|True,True";

        public DelegateComboUnit unit;
        public DelegateComboDerived derived;
        public Text display;

        // D2：public のデリゲート型のフィールド（シリアライズしない。Play モードでエラーが出ないこと）
        public Calc publicCalc;
        public Calc[] publicCalcs;

        private Calc _calc;
        private Calc _rec;
        private string _log = "";

        private static int Square(int x) => x * x;
        private static int HitTwo(IComboTarget t) => t.Hit(2);
        private static ComboItem NewItem(int v) => new ComboItem(v * 10);
        private static int SumItemValues(ComboItem[] items) { int s = 0; foreach (ComboItem i in items) s += i.v; return s; }
        private static int BoxValue(ComboBox<int> box) => box.value;
        private static int SumL(List<int> list) => list.Sum();
        private static List<int> MakeL(int n) { var l = new List<int>(); for (int i = 0; i < n; i++) l.Add(i); return l; }
        private static int SumD(Dictionary<string, int> dict) => dict["a"] + dict["b"];
        private static Vector3 Up(Vector3 v) => v + Vector3.up * 2f;
        private static int SumA(int[] values) { int s = 0; foreach (int x in values) s += x; return s; }
        private static string NameOfGo(GameObject go) => go.name;
        private static int ReadUnit(UdonSharpBehaviour b) => ((DelegateComboUnit)b).v;
        private static int Seven() => 7;
        private static int Sum5(int a, int b, int c, int d, int e) => a + b * 2 + c * 3 + d * 4 + e * 5;
        private static int ApplyPlus(Calc c, int x) => c(x) + 1;
        private static string Over(string s) => "s" + s;
        private static int Over(int x) => x + 1;
        private void LogObject(object o) { _log += o; }

        [RecursiveMethod]
        private int Fact(int n) => n <= 1 ? 1 : n * _rec(n - 1);

        [RecursiveMethod]
        private int SumDown(int n) => n == 0 ? 0 : _calc(n) + SumDown(n - 1);

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            _log = "";
            unit.v = 50;
            unit.interactCount = 0;
            string s = "";

            // A1：インタフェースを引数に持つデリゲート、インタフェースのメソッドにデリゲートを渡す（UdonSharpBehaviour・普通のクラスの実装）
            HitTarget ht = HitTwo;
            var item = new ComboItem(10);
            s += ht(unit) + "," + ht(item) + ",";
            IComboTarget t1 = unit;
            IComboTarget t2 = item;
            t1.SetCalc(Square); t2.SetCalc(Square);
            s += t1.UseCalc(3) + "," + t2.UseCalc(4) + ";";

            // A2：インタフェースの実装メソッドをデリゲートに入れる（UdonSharpBehaviour の中で）
            s += unit.HitViaDelegate(5) + ";";

            // A3：デリゲートを受け取る static メソッド、普通のクラスを返す・配列を受け取るデリゲート
            MakeItem mi = NewItem;
            SumItems si = SumItemValues;
            s += ComboUtil.Apply(Square, 5) + "," + mi(3).v + "," + si(ComboUtil.Pair(mi)) + ";";

            // A4：ジェネリッククラスのデリゲート型のフィールド・ジェネリックメソッド・ジェネリッククラスを引数に持つデリゲート
            var box = new ComboBox<int>(5);
            box.calc = Square;
            OnBox ob = BoxValue;
            s += box.Run(6) + "," + ComboGeneric.CountThen(new string[] { "a", "b", "c" }, Square) + "," + ob(box) + ";";

            // A5：List / Dictionary を引数・戻り値に持つデリゲート
            SumList sl = SumL;
            MakeList ml = MakeL;
            SumDict sd = SumD;
            var dict = new Dictionary<string, int>();
            dict["a"] = 3; dict["b"] = 4;
            s += sl(new List<int> { 1, 2, 3 }) + "," + ml(4).Count + "," + sl(ml(5)) + "," + sd(dict) + ";";

            // A6：ラムダ・LINQ の中でデリゲートを呼ぶ（フィールド・ローカル変数）
            _calc = Square;
            Calc local = Over;
            var list = new List<int> { 1, 2, 3 };
            int acc = 0;
            list.ForEach(x => acc += _calc(x));
            s += acc + "," + list.Select(x => local(x)).Sum() + "," + list.Where(x => _calc(x) > 3).Count() + ";";

            // A7：継承（基底と派生の両方で同じデリゲート型。virtual は override が呼ばれる）
            s += derived.BaseMake() + "," + derived.DerivedMake() + ";";

            // A9：[RecursiveMethod] の中で呼ぶ・デリゲートで再帰する
            _rec = Fact;
            s += SumDown(3) + "," + _rec(5) + ",";
            // 呼ばれたメソッドの中から、同じデリゲート型で別の UdonSharpBehaviour を呼ぶ（入れ子の呼び出しのあとでも、外側の戻り値が正しい）
            Calc outer = CallsDerived;
            s += outer(1) + "," + outer(2) + ";";

            // B1：値型・配列・GameObject・UdonSharpBehaviour
            MoveVec mv = Up;
            SumArray sa = SumA;
            NameOfObject no = NameOfGo;
            ReadBehaviour rb = ReadUnit;
            s += mv(Vector3.one).y + "," + sa(new int[] { 1, 2, 3, 4 }) + "," + no(unit.gameObject) + "," + rb(unit) + ";";

            // B2：引数なし・引数 5 つ
            NoArgs na = Seven;
            FiveArgs f5 = Sum5;
            s += na() + "," + f5(1, 2, 3, 4, 5) + ";";

            // B3：デリゲートを引数に持つデリゲート
            ApplyCalc ac = ApplyPlus;
            s += ac(Square, 4) + "," + ac(DelegateComboUnit.Triple, 4) + ";";

            // B4：オーバーロード（デリゲート型で選ぶ）、B5：object を受けるメソッドを string のデリゲートに
            TextMap tm = Over;
            Calc oc = Over;
            LogText lt = LogObject;
            lt("x"); lt("y");
            s += tm("a") + "," + oc(2) + "," + _log + ";";

            // B6：Udon の外部メソッド
            FloatMap abs = Mathf.Abs;
            s += abs(-2.5f) + ";";

            // B7：イベントのメソッド（Interact）
            s += unit.InteractViaDelegate() + ";";

            // B8：ほかの UdonSharpBehaviour の static メソッド
            Calc triple = DelegateComboUnit.Triple;
            s += triple(5) + ";";

            // B10：条件演算子
            bool yes = s.Length > 0;
            Calc c1 = yes ? (Calc)Square : null;
            Calc c2 = !yes ? Square : (Calc)Seven2;
            s += c1(3) + "," + c2(3) + "," + (yes ? (Calc)Over : Square)(9);

            // D2
            s += "|" + (publicCalc == null) + "," + (publicCalcs == null || publicCalcs.Length == 0);
            return s;
        }

        private static int Seven2(int x) => 7 + x;

        private int CallsDerived(int x)
        {
            Calc inner = derived.GetVirt();
            return inner(x) + 1000;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#126 delegate combo\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#126 delegate combo"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #126 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
