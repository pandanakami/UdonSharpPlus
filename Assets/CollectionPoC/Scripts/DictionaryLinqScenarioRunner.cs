using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionPoC
{
    // #61: DictionaryLinqScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class DictionaryLinqScenarioRunner : UdonSharpBehaviour
    {
        // エディタで DictionaryLinqScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:ann cid dan,3,False,True,cid,220;b:3,50,False,260,90,ann cid dan;c:cid90,2ann90,cid,eve=55;d:3,2,ann=70 cid=90;" +
            "e:2,ann=70 cid=90 dan=60;f:bread;";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = DictionaryLinqScenario.Run();
            bool ok = actual == Expected;

            string text = "#61 Dictionary LINQ scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            Debug.Log("[CollectionPoC] #61 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
