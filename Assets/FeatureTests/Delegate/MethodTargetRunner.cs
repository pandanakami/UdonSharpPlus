using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #124 の追加テスト（機能単体）：別のインスタンス・作ったときの対象を持ち続ける・受け側を 1 回だけ評価する・
    // public / private / protected・普通のクラスの中のラムダ・同じものかどうか・型とオーバーロード
    public class MethodTargetRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "1,100,5,5,8;1,1,12,20,8,0,1;11,20,7,19,18;35,36,136,47;True,False,False,True,False;7,14,16,10,18,sa8,1001";

        public MtTarget target;
        public MtTargetDerived derived;
        public Text display;

        private int _gets;
        private int _idx;
        private int _propGets;
        private MtBox _propBox = new MtBox(0);

        private MtBox GetBox() { _gets++; return new MtBox(_gets * 100); }
        private int NextIndex() => _idx++;
        private MtBox BoxProp { get { _propGets++; return _propBox; } }

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            target.hp = 10;
            derived.hp = 20;

            // 1・2：別のインスタンス・作ったあとのフィールドの変更は見える・受け側の変数を入れ替えても作ったときの対象を呼ぶ
            var b1 = new MtBox(1);
            var b2 = new MtBox(100);
            Calc g1 = b1.Get, g2 = b2.Get;
            s += g1(0) + "," + g2(0) + ",";
            b1.v = 5;
            s += g1(0) + ",";
            MtBox r = b1;
            Calc gr = r.Get;
            r = b2;
            s += gr(0) + ",";
            Calc add1 = b1.Add;
            add1(3);
            s += b1.v + ";";

            // 3：受け側は作るときに 1 回だけ評価する（メソッドの戻り値・添字が式の配列の要素・Dictionary・プロパティ）
            _gets = 0;
            Calc ga = GetBox().Add;
            ga(1);
            ga(1);
            s += _gets + ",";
            var boxes = new MtBox[] { new MtBox(10), new MtBox(20) };
            _idx = 0;
            Calc ia = boxes[NextIndex()].Add;
            ia(1);
            ia(1);
            s += _idx + "," + boxes[0].v + "," + boxes[1].v + ",";
            var map = new Dictionary<string, MtBox>();
            map["k"] = new MtBox(7);
            Calc ma = map["k"].Add;
            map["k"] = new MtBox(0);
            s += ma(1) + "," + map["k"].v + ",";
            _propGets = 0;
            Calc pa = BoxProp.Add;
            pa(1);
            s += _propGets + ";";

            // 4：UdonSharpBehaviour の public・private・protected（派生から）・base
            Calc pub = target.Pub;
            Calc priv = target.MakePriv();
            Calc prot = target.MakeProt();
            s += pub(1) + "," + priv(2) + "," + prot(3) + "," + derived.ProtFromDerived()(1) + "," + derived.BaseProt()(2) + ";";

            // 5：普通のクラスの中のラムダ（this のフィールド・引数・ローカル変数。インスタンスごとに別。作ったあとの変更が見える）
            var m1 = new MtBox(1);
            var m2 = new MtBox(2);
            Calc mix1 = m1.MakeMix(3);
            Calc mix2 = m2.MakeMix(4);
            s += mix1(0) + "," + mix1(0) + ",";
            m1.v = 100;
            s += mix1(0) + "," + mix2(0) + ";";

            // 6：同じ対象・同じメソッドなら -= で外れる。別のメソッド・別のインスタンスなら外れない
            Calc idA = b1.Get;
            idA -= b1.Get;
            Calc idB = b1.Get;
            idB -= b1.Add;
            Calc idC = b1.Get;
            idC -= b2.Get;
            Calc idD = target.Pub;
            idD -= target.Pub;
            Calc idE = target.Pub;
            idE -= derived.Pub;
            s += (idA == null) + "," + (idB == null) + "," + (idC == null) + "," + (idD == null) + "," + (idE == null) + ";";

            // 7：戻り値なし・引数 3 つ・普通のクラス／インタフェース／配列・Vector3／GameObject・デリゲートを受け渡す・オーバーロード
            MtVoid note = b1.Note;
            note(7);
            MtMany many = b1.Sum;
            MtMakeBox mk = b1.Merge;
            MtVec mv = target.Move;
            MtCalcOut wrap = b1.Wrap;
            MtText ot = b1.Over;
            Calc oi = b1.Over;
            s += b1.log + "," + many(1, 2, 3) + "," + mk(new MtBox(1), new CmRect(2, 2), new int[3]).v + "," + mv(Vector3.zero, gameObject).y + ","
                 + wrap(x => x * 2)(5) + "," + ot("a") + "," + oi(1);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#124 method target\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#124 method target"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #124 target " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
