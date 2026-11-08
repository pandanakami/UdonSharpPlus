using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ClassPoC
{
    // #56: CollectionScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // あわせて、UdonSharpBehaviour のフィールドの初期化子（List / Dictionary）が使えることも確かめる。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class CollectionScenarioRunner : UdonSharpBehaviour
    {
        // エディタで CollectionScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:10 7 9 1,True,True,2,4;b:1 7 9 10;c:10 9 7 1;d:4,9 7 1 7,9 7 1 7;e0:2two;e:2,False,True,11;f:11;g:0;" +
            "h:14,2,14,2;i:sword4/0 herb2/2;j:3,ac,3,5,21,2axe;";

        public Text display;

        private List<string> _runs = new List<string> { "init" };
        private Dictionary<string, int> _counts = new Dictionary<string, int> { { "runs", 0 } };
        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            _counts["runs"]++;
            _runs.Add("run" + _counts["runs"]);
            string fieldText = "fields:" + _runs[0] + "," + _runs.Count + "," + _counts["runs"];
            bool fieldOk = _runs[0] == "init" && _runs.Count == _counts["runs"] + 1;

            string actual = CollectionScenario.Run();
            bool ok = actual == Expected && fieldOk;

            string text = "#56 List / Dictionary scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n"
                          + (fieldOk ? "OK  " : "NG  ") + fieldText + "\n\n";
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

            Debug.Log("[ClassPoC] #56 " + (ok ? "OK " : "NG ") + fieldText + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
