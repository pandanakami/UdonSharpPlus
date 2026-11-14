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
        private const string Expected = "9,8,105,205;36,14,49;True,False;a1b2;20;212;16,15;n3;True,True;12,5,8,c3!,9,15,8,120,8,12;1,11,21,107,107,6,8,3,10,10,15,10;105,205,51,0,1,2,i10,i21,6,7,6;10,105,a1!a,6,True,16,4;6,9,9,9,4;11,11,14,1009,105;ABC,A,A,ABC,A;1,32;3,3,B,B,null;null,A,A,null,A,null";

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
            s += acc + ";";
            s += ClosureScenario() + ";";
            s += CombineScenario() + ";";
            s += CombineMoreScenario();
            return s;
        }

        // #122 の追加テスト：呼ばれたものを _log に名前で残し、つないだ中身を確かめる
        private int LA(int x) { _log += "A"; return x; }
        private int LB(int x) { _log += "B"; return x; }
        private int LC(int x) { _log += "C"; return x; }
        private Calc MakeLogCounter() { int n = 0; return x => { n++; _log += n; return n; }; }
        private Calc _propBack;
        private int _gets, _sets;
        private Calc LogProp { get { _gets++; return _propBack; } set { _sets++; _propBack = value; } }

        private string Trace(Calc c)
        {
            if (c == null)
                return "null";
            _log = "";
            c(0);
            return _log;
        }

        private string CombineMoreScenario()
        {
            string s = "";
            // 1. 同じ並びが複数あるときは、最後のものだけを外す
            Calc ab = (Calc)LA + LB;
            Calc t = ab + LC + ab;
            t -= ab;
            s += Trace(t) + ",";
            // 2. 重なった並びは、うしろから外す
            Calc aaa = (Calc)LA + LA + LA;
            aaa -= (Calc)LA + LA;
            s += Trace(aaa) + ",";
            // 3. つなぐ・外すで、元の値は変わらない
            Calc original = LA;
            Calc combined = original + LB;
            Calc removed = combined - LB;
            combined += LC;
            s += Trace(original) + "," + Trace(combined) + "," + Trace(removed) + ";";
            // 4. 同じラムダでも、外の変数の環境が違えば外れない
            Calc c1 = MakeLogCounter(), c2 = MakeLogCounter();
            c1(0); c1(0);
            Calc both = c1 + c2;
            both -= c1;
            s += Trace(both) + ",";
            Calc both2 = c1 + c2;
            both2 -= MakeLogCounter();
            s += Trace(both2) + ";";
            // 5. 副作用のあるプロパティ（get・set は 1 回ずつ）・配列の要素への += / -=
            _gets = 0; _sets = 0; _propBack = null;
            LogProp += LA;
            LogProp += LB;
            LogProp -= LA;
            s += _gets + "," + _sets + "," + Trace(LogProp) + ",";
            var arr = new Calc[2];
            arr[1] += LA;
            arr[1] += LB;
            arr[1] -= LA;
            s += Trace(arr[1]) + ",";
            arr[1] -= LB;
            s += Trace(arr[1]) + ";";
            // null との + / -
            Calc z = null;
            Calc a = LA;
            s += Trace(z + z) + "," + Trace(z + a) + "," + Trace(a + z) + "," + Trace(z - a) + "," + Trace(a - z) + "," + Trace(a - a);
            return s;
        }

        // #122 += / -=、+ / -
        private string CombineScenario()
        {
            string s = "";
            _base = 100;
            // 呼ぶ順番・戻り値は最後のもの・戻り値なし
            Calc m = Square;
            m += DelegateMath.Twice;
            s += m(5) + ",";
            m += AddBase;
            s += m(5) + ",";
            _log = "";
            _notify = OnNotify;
            _notify += (it, t) => _log += "!" + t;
            _notify(new DelegateItem(1), "a");
            s += _log + ",";
            // -= は最後に現れるものを外す・全部外れたら null・ないものは外さない・null との組み合わせ
            Calc m2 = Square;
            m2 += DelegateMath.Twice;
            m2 += Square;
            m2 -= Square;
            s += m2(3) + ",";
            Calc one = Square;
            one -= Square;
            s += (one == null) + ",";
            Calc one2 = Square;
            one2 -= DelegateMath.Twice;
            one2 -= null;
            s += one2(4) + ",";
            Calc n = null;
            n += Square;
            s += n(2) + ";";
            // + / -、続けて並んだものを外す
            Calc ab = (Calc)Square + DelegateMath.Twice;
            s += ab(3) + "," + (ab - DelegateMath.Twice)(3) + ",";
            Calc seq = (Calc)Square + DelegateMath.Twice + Square;
            seq -= (Calc)Square + DelegateMath.Twice;
            s += seq(3) + ",";
            // ラムダ：同じ値は外れる・別の場所に書いた同じ式は外れない（C# と同じ）
            Calc lam = x => x + 1;
            Calc both = Square;
            both += lam;
            both -= lam;
            s += both(3) + ",";
            both += x => x + 1;
            both -= x => x + 1;
            s += both(3) + ";";
            // クロージャ・外の変数への += ・普通のクラスのフィールドへの += ・別の UdonSharpBehaviour で呼ぶ
            int hits = 0;
            Calc hc = x => ++hits;
            hc += x => hits += 10;
            s += hc(0) + "," + hits + ",";
            Calc cap = Square;
            Calc useCap = x => cap(x);
            cap += DelegateMath.Twice;
            s += useCap(7) + ",";
            var item = new DelegateItem(1000);
            item.calc = DelegateMath.Twice;
            item.calc += Square;
            s += item.Apply(3) + ",";
            holder.Store(m);
            s += holder.CallStored(5);
            return s;
        }

        // #121 クロージャ（外のローカル変数・引数を使うラムダ）
        private Calc MakeAdder(int k) => x => x + k;
        private Calc MakeCounter() { int n = 0; return x => { n += x; return n; }; }

        private string ClosureScenario()
        {
            string s = "";
            // ループの中で作る（1 回ごとに別の変数）・ラムダと元のメソッドが同じ変数を書き換える
            var arr = new Calc[3];
            for (int i = 0; i < 3; i++)
            {
                int j = i;
                arr[i] = x => x + j * 10;
            }
            s += arr[0](1) + "," + arr[1](1) + "," + arr[2](1) + ",";
            int total = 0;
            Calc addTo = x => { total += x; return total; };
            addTo(3); addTo(4); total += 100;
            s += addTo(0) + "," + total + ",";
            // メソッドを抜けたあとに呼ぶ（引数を使う・ローカル変数を持ち続ける）
            Calc a5 = MakeAdder(5), a7 = MakeAdder(7);
            s += a5(1) + "," + a7(1) + ",";
            Calc c1 = MakeCounter(); c1(1); Calc c2 = MakeCounter();
            s += c1(2) + "," + c2(10) + ",";
            // foreach の変数・ラムダの中のラムダ（外のラムダの引数とローカル変数を使う）
            foreach (int w in new int[] { 2, 3 })
                arr[w - 2] = x => x * w;
            s += arr[0](5) + "," + arr[1](5) + ",";
            Calc outerC = x => { int m = x * 2; Calc inner = y => y + m + x; return inner(1); };
            s += outerC(3) + ";";
            // ラムダの引数と外の変数を、中のラムダで使う・別の UdonSharpBehaviour で呼ぶ
            int bias = 100;
            CalcMaker mk = k => y => y + k + bias;
            s += mk(2)(3) + ",";
            bias = 200;
            s += mk(2)(3) + ",";
            int off = 40;
            holder.Store(x => x + off);
            off = 50;
            s += holder.CallStored(1) + ",";
            // 値を使う ++・普通のクラス・構造体・List のラムダの中でも書き換える・デリゲートの変数
            int hits = 0;
            Calc hit = x => hits++;
            s += hit(0) + "," + hit(0) + "," + hits + ",";
            DelegateItem it = new DelegateItem(4);
            Describe dsc = x => "i" + (it.v + x);
            it = new DelegateItem(9);
            s += dsc(1) + ",";
            it.v = 20;
            s += dsc(1) + ",";
            Vector3 pos = new Vector3(1, 2, 3);
            Calc py = x => x + (int)pos.y;
            pos = new Vector3(0, 5, 0);
            s += py(1) + ",";
            int sum = 0;
            Calc addS = x => sum + x;
            var nums = new System.Collections.Generic.List<int> { 1, 2, 3 };
            nums.ForEach(v => sum += v);
            s += addS(1) + ",";
            Calc inner2 = x => x + 1;
            Calc wrap = x => inner2(x) * 2;
            inner2 = x => x + 2;
            s += wrap(1);
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
