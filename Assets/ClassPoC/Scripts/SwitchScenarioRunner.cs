using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ClassPoC
{
    // #65: インタフェース経由の呼び出しを型番号の switch で振り分けたものを Udon 上で使い、
    // 手計算の期待値と比べて OK/NG を表示する。Start で 1 回実行し、Interact でもう一度実行できる。
    // 時間（time）は参考の表示で、OK/NG には入れない
    public class SwitchScenarioRunner : UdonSharpBehaviour
    {
        private const string Expected =
            "a:2,6,15,13,5,4,0,-9,11,12,42;b:101;c:15;d:a,c,banner;e:13,12;";

        private const int Loops = 5000;

        public SwitchGolem golem;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            golem.ResetUnit();

            // a: 汎用クラス 8 個、ジェネリクスのコピー 2 個、UdonSharpBehaviour 1 個を混ぜて呼ぶ
            ISwitchUnit[] units = new ISwitchUnit[]
            {
                new SwUnitA(), new SwUnitB(), new SwUnitC(), new SwUnitD(),
                new SwUnitE(), new SwUnitF(), new SwUnitG(), new SwUnitH(),
                new SwBox<int>(7, 1), new SwBox<string>("s", 2), golem,
            };
            string actual = "a:";
            for (int i = 0; i < units.Length; i++)
            {
                units[i].Boost(1);
                actual += (i > 0 ? "," : "") + units[i].Power();
            }
            actual += ";";

            // b: 汎用クラスの中から呼ぶ
            actual += "b:" + new SwArmy().Total(units) + ";";

            // c: static メソッドの中から呼ぶ
            actual += "c:" + PowerOf(units[2]) + ";";

            // d: 実装クラスが 3 個のインタフェース（is を並べる形）
            ISwitchLabel[] labels = new ISwitchLabel[] { new SwUnitA(), new SwUnitC(), new SwBanner() };
            actual += "d:" + labels[0].Label() + "," + labels[1].Label() + "," + labels[2].Label() + ";";

            // e: 別のプログラムに渡して、そちらの振り分けで呼ぶ（型番号が全プログラムで揃っていること）
            actual += "e:" + golem.Measure(units[3]) + "," + golem.Measure(units[9]) + ";";

            bool ok = actual == Expected;

            // 参考：is を並べる形なら最初に判定するクラス（完全名の順で SwBox__int）と、最後に判定するクラス（SwUnitH）を呼ぶ時間。
            // switch なら同じくらいになる。最初の 1 回は空打ちして、測る順の影響を減らす
            Measure(units[0]);
            string time = "first " + Measure(units[8]) + "ms, last " + Measure(units[7]) + "ms (" + Loops + " calls)";

            Debug.Log("[ClassPoC] #65 " + (ok ? "OK " : "NG ") + actual + " " + time);
            if (display != null)
                display.text = "#65 interface switch\n" + (ok ? "RESULT: OK" : "RESULT: NG") + "\n\n"
                               + actual.Replace(";", "\n") + (ok ? "" : "\nexpected:\n" + Expected.Replace(";", "\n"))
                               + "\ntime: " + time;
        }

        private static int PowerOf(ISwitchUnit u) { return u.Power(); }

        private int Measure(ISwitchUnit u)
        {
            float start = Time.realtimeSinceStartup;
            int sum = 0;
            for (int i = 0; i < Loops; i++)
                sum += u.Power();
            return Mathf.RoundToInt((Time.realtimeSinceStartup - start) * 1000f);
        }
    }
}
