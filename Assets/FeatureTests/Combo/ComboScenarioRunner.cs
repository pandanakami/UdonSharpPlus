using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Combo
{
    // #63: ComboScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、Interact でもう一度実行できる。
    public class ComboScenarioRunner : UdonSharpBehaviour
    {
        // エディタで ComboScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:4,mage,52,2,7,Truemage,hired:x thief;b:2,-1,True,8 6 2 8 7 5,19;c:archer golem hired:x knight thief,6,8,7;" +
            "d:3,golem,hired:74,True,False,13,8;e:2,3 1 2,TrueFalse,yan,5 7,b c,7none;f:3,3,2,a,8,8;g:1 1 0,1,2,8;h:12,5,tb,33,21;";

        public ComboGolem golem;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            golem.ResetUnit();
            string actual = ComboScenario.Run(golem);
            bool ok = actual == Expected;

            string text = "#63 combination scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                if (ei.Length == 0 && ai.Length == 0)
                    continue;
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }

            Debug.Log("[FeatureTests] #63 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
