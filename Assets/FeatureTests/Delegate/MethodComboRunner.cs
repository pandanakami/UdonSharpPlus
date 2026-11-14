using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #124 の追加テスト（ほかの機能との組み合わせ）：+= / -=・クロージャ・ローカル関数と匿名メソッド・インタフェース・継承・
    // ジェネリクス・List と LINQ・配列と Dictionary の要素・呼び返し・あとのフレームで呼ぶ・partial
    public partial class MethodComboRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "10,2,20,True;1017,2026;12,8,8;30,1,4;3,2;11,12,2,3,2;30,1;2;47;61,29;3105";

        public MtTarget target;
        public CmTargetDerived derived;
        public LocalComboKeeper keeper;
        public Text display;

        private Calc _bounce;
        private MtBridge _bridge = new MtBridge();
        private MtBox _laterBox;

        // 10：Start で作って別の UdonSharpBehaviour に渡し、あとのフレーム（Run）で呼ぶ
        private void Start()
        {
            _laterBox = new MtBox(50);
            Calc later = target.Hit;
            later += _laterBox.Get;
            keeper.StoreLater(later);
            SendCustomEventDelayedFrames(nameof(Run), 1);
        }

        public override void Interact() { Run(); }

        // 9：呼び返し（この UdonSharpBehaviour → 普通のクラス → 別の UdonSharpBehaviour → このデリゲート）
        [RecursiveMethod]
        private int Bounce(int x)
        {
            if (x <= 0)
                return 1;
            int keep = x;
            int r = _bridge.Through(_bounce, target, x - 1);
            return keep * 10 + r;
        }

        private string Scenario()
        {
            string s = "";
            target.hp = 10;

            // 1：普通のクラス・UdonSharpBehaviour の public・private のメソッドを 1 つにつなぐ・外す
            var b1 = new MtBox(1);
            Calc chain = b1.Add;
            chain += target.Pub;
            chain += target.MakePriv();
            s += chain(1) + "," + b1.v + ",";
            chain -= target.Pub;
            chain -= b1.Add;
            s += chain(2) + ",";
            chain -= target.MakePriv();
            s += (chain == null) + ";";

            // 2：普通のクラスの中のラムダが、this・引数・ローカル変数・別の普通のクラス・UdonSharpBehaviour を使う（作ったあとの変更が見える）
            var other = new MtBox(1000);
            Calc cap = b1.MakeCapture(3, other, target);
            s += cap(0) + ",";
            other.v = 2000;
            target.hp = 20;
            b1.v = 0;
            s += cap(0) + ";";

            // 3：ローカル関数・匿名メソッドからインスタンスのメソッドを呼び、別の UdonSharpBehaviour で呼ぶ
            var b2 = new MtBox(5);
            keeper.Store(b2.MakeViaLocal());
            s += keeper.CallStored(1) + ",";
            keeper.Store(b2.MakeViaAnon());
            s += keeper.CallStored(3) + "," + b2.v + ";";

            // 4：インタフェース（普通のクラスの実装 4 つ＋UdonSharpBehaviour の実装。作ったあとに変数を入れ替えても作ったときの対象）
            var shapes = new ICmShape[] { new MtShapeA(), new MtShapeB(), new MtShapeC(), new MtShapeD(), target };
            var measures = new CmMeasure[5];
            for (int i = 0; i < shapes.Length; i++)
                measures[i] = shapes[i].Area;
            int areaSum = 0;
            foreach (CmMeasure m in measures)
                areaSum += m();
            ICmShape one = shapes[0];
            CmMeasure first = one.Area;
            one = shapes[3];
            s += areaSum + "," + first() + "," + one.Area() + ";";

            // 5：基底の型の変数から入れても override・base
            CmTarget asBase = derived;
            Calc kind = asBase.Kind;
            s += kind(1) + "," + derived.BaseKind()(1) + ";";

            // 6：ジェネリッククラスの具体型ごと（混ざらない・別のものは -= で外れない）
            var gi = new MtGen<int>(5, 1);
            var gs = new MtGen<string>("a", 2);
            Calc ci = gi.Count;
            Calc cs = gs.Count;
            Calc both = ci + cs;
            both -= gi.Make();
            s += ci(10) + "," + cs(10) + "," + gi.Make()(1) + "," + gs.Make()(1) + "," + both(0) + ";";

            // 7：List・LINQ（インスタンスのメソッドのデリゲートを、ラムダから呼ぶ）
            var nums = new List<int> { 1, 2, 3 };
            Calc g = b2.Get;
            Calc tp = target.Pub;
            s += nums.Select(x => g(x)).Sum() + "," + nums.Where(x => tp(x) > 22).Count() + ";";

            // 8：配列の要素から作ったあとで、要素を入れ替えても作ったときの対象
            var arr = new MtBox[] { new MtBox(1), new MtBox(2) };
            Calc fromArr = arr[1].Get;
            arr[1] = new MtBox(99);
            s += fromArr(0) + ";";

            // 9：呼び返し
            _bounce = Bounce;
            s += Bounce(2) + ";";

            // 10：あとのフレームで呼ぶ（UdonSharpBehaviour の Hit で hp が減り、戻り値は最後の普通のクラスの Get）
            target.hp = 30;
            _laterBox.v = 60;
            s += keeper.CallLater(1) + "," + target.hp + ";";

            // 11：partial の別のファイル・別のファイルの同じ名前のメソッド
            s += PartUse();
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#124 method combo\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#124 method combo"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #124 combo " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
