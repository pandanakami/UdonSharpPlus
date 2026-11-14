using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #124: クラスのメソッド（ほかの UdonSharpBehaviour・普通のクラス・インタフェース）をデリゲートに入れる・普通のクラスの中で
    // デリゲートを作る、を Udon 上で実行し、普通の C# の結果と比べて OK/NG を表示する
    public class ClassMethodRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "90,90,3,2,3,85,84;1,6,84,105,True;8,-4,15,16,19,15,11,12,14,20,True";

        public CmTarget target;
        public CmTargetDerived derived;
        public LocalComboKeeper keeper;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            target.hp = 100;

            // ほかの UdonSharpBehaviour のメソッド（基底の型の変数から入れても override・base は基底の実装・別の UdonSharpBehaviour で呼ぶ・-=）
            Calc hit = target.Hit;
            s += hit(10) + "," + target.hp + ",";
            CmTarget asBase = derived;
            Calc kind = asBase.Kind;
            s += kind(1) + "," + derived.BaseKind()(1) + "," + derived.OwnKind()(1) + ",";
            keeper.Store(hit);
            s += keeper.CallStored(5) + ",";
            Calc two = target.Hit;
            two += target.Hit;
            two -= target.Hit;
            s += two(1) + ";";

            // 普通のクラス・インタフェースのインスタンスのメソッド（インタフェースは UdonSharpBehaviour の実装にも差し替える・-=）
            var counter = new CmCounter(2);
            Calc peek = counter.Peek;
            s += peek(1) + ",";
            ICmShape shape = new CmRect(2, 3);
            CmMeasure area = shape.Area;
            shape = target;
            CmMeasure area2 = shape.Area;
            s += area() + "," + area2() + ",";
            var counter2 = new CmCounter(1);
            counter2.count = 100;
            Calc pp = counter.Peek;
            pp += counter2.Peek;
            pp -= counter.Peek;
            s += pp(5) + ",";
            pp -= counter2.Peek;
            s += (pp == null) + ";";

            // 普通のクラスの中で作る（private のメソッド・private static・this と外の変数を使うラムダ・匿名メソッド・ローカル関数・static・+=）
            Calc add = counter.MakeAdd();
            add(3);
            s += add(1) + "," + counter.MakeNeg()(4) + ",";
            Calc lam = counter.MakeLambda(10);
            s += lam(1) + "," + lam(1) + ",";
            counter.step = 5;
            s += lam(0) + "," + counter.MakeAnon()(3) + "," + counter.MakeLocal()(3) + "," + CmCounter.MakeStatic(3)(4) + "," + counter.Chain()(1) + ",";
            keeper.Store(lam);
            s += keeper.CallStored(0) + ",";
            Calc aa = counter.MakeAdd();
            aa -= counter.MakeAdd();
            s += (aa == null);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#124 class method\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#124 class method"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #124 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
