using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #79: SyntaxScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、Interact でもう一度実行できる。
    public class SyntaxScenarioRunner : UdonSharpBehaviour
    {
        // エディタで SyntaxScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:3,x3,p9,4,6,10;b:6,2,8,4;c:5,[z],25,4,6,True,False;d:1,1,1,0,12,4,9;" +
            "n:2/11/3 3/12/4 4/13/5 5/14/6 101/201/102,TrueFalseTrueTrueTrue-3True,7,14,5,21,28;";

        public SyntaxTarget target;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = SyntaxScenario.Run(target);
            bool ok = actual == Expected;

            string text = "#79 syntax variations\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            Debug.Log("[FeatureTests] #79 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
