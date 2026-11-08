using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionPoC
{
    // #58: LinqScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class LinqScenarioRunner : UdonSharpBehaviour
    {
        // エディタで LinqScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:10 16 12,242;b:3,586<5><8><6>;c:True,True,False,True,False,6,2,True,False;d:5,5,0,7,3,0;e:26,220,1,9,-1,5.5,2.75;" +
            "f:True3,15,1;g:annbob2,bob,4,2cid;h:3,3,12;";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = LinqScenario.Run();
            bool ok = actual == Expected;

            string text = "#58 LINQ scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            Debug.Log("[CollectionPoC] #58 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
