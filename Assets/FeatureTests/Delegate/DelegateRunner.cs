using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #119: ソースで宣言したデリゲート（宣言・変数・引数・戻り値と、static メソッド・自分のメソッドを入れる）を Udon 上で実行し、
    // 普通の C# の結果と比べて OK/NG を表示する
    public class DelegateRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果
        private const string Expected = "9,8,105,205;36,14,49;True,False;a1b2;20;212;16,15;n3;True,True;12,5,8,c3!,9,15,8,120,8,12";

        // UdonSharpBehaviour の中で宣言したデリゲート（名前空間の直下に移して書き換える）
        public delegate bool Check(int x);

        public DelegateHolder holder;
        public Text display;

        private Calc _calc;
        private Calc[] _calcs = new Calc[2];
        private Check _check;
        private Notify _notify;
        private Describe _describe;
        private int _base = 100;
        private string _log = "";

        private static int Square(int x) => x * x;
        private int AddBase(int x) => x + _base;
        private bool IsEven(int x) => x % 2 == 0;
        private void OnNotify(DelegateItem item, string text) { _log += text + item.v; }
        private string Label(int x) => "n" + x;
        private int Apply(Calc c, int x) => c(x);
        private Calc Pick(bool square) => square ? (Calc)Square : DelegateMath.Twice;
        private Calc MakeTripler() => x => x * 3;
        private Calc _rec;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            _base = 100;
            string s = "";
            // static メソッド・別のクラスの static メソッド・自分のメソッド（呼んだときのフィールドを読む）
            _calc = Square; s += _calc(3) + ",";
            _calc = DelegateMath.Twice; s += _calc.Invoke(4) + ",";
            _calc = AddBase; s += _calc(5) + ",";
            _base = 200; s += _calc(5) + ";";
            // 引数に渡す・戻り値で返す
            s += Apply(Square, 6) + "," + Pick(false)(7) + "," + Pick(true)(7) + ";";
            // UdonSharpBehaviour の中で宣言したデリゲート、bool を返す
            _check = IsEven; s += _check(10) + "," + _check(7) + ";";
            // 戻り値なし、普通のクラスを引数に持つ
            _notify = OnNotify; _log = ""; _notify(new DelegateItem(1), "a"); _notify(new DelegateItem(2), "b"); s += _log + ";";
            // 普通のクラスのフィールドに入れて、クラスのメソッドの中で呼ぶ
            var item = new DelegateItem(10); item.calc = DelegateMath.Twice; s += item.Apply(5) + ";";
            // 配列・new Calc(...)
            _calcs[0] = Square; _calcs[1] = new Calc(AddBase); s += (_calcs[0](3) + _calcs[1](3)) + ";";
            // 別の UdonSharpBehaviour：渡したものを向こうで呼ぶ・向こうで作ったものをこちらで呼ぶ
            holder.Store(Square); s += holder.CallStored(4) + ",";
            Calc adder = holder.MakeAdder(); s += adder(5) + ";";
            // string を返す
            _describe = Label; s += _describe(3) + ";";
            // null との比較
            Calc none = null; s += (none == null) + "," + (_calc != null) + ";";
            // #120 ラムダ（外の変数を使わないもの）：式・ブロック・フィールドを使う・void・捨てる引数・引数に渡す・戻り値・
            // 別の UdonSharpBehaviour に渡す・再帰（フィールドを通して）・ラムダの中のラムダ・List のラムダの中で作る
            _calc = x => x * 3; s += _calc(4) + ",";
            _calc = x => { if (x > 0) return x; return -x; }; s += _calc(-5) + ",";
            _base = 7; _calc = x => x + _base; s += _calc(1) + ",";
            _log = ""; _notify = (it, t) => { _log += t + it.v; }; _notify(new DelegateItem(3), "c");
            _notify = (_, _) => _log += "!"; _notify(null, null); s += _log + ",";
            s += Apply(x => x - 1, 10) + "," + MakeTripler()(5) + ",";
            holder.Store(x => x + _base); s += holder.CallStored(1) + ",";
            _rec = n => n <= 1 ? 1 : n * _rec(n - 1); s += _rec(5) + ",";
            Calc outerL = x => { Calc inner = y => y + 1; return inner(x) * 2; }; s += outerL(3) + ",";
            var list = new System.Collections.Generic.List<int> { 1, 2, 3 };
            int acc = 0;
            list.ForEach(v => { Calc twice = y => y * 2; acc += twice(v); });
            s += acc;
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#119 delegate\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#119 delegate"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #119 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
