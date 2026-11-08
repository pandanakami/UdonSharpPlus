using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ClassPoC
{
    // #49: ClassScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class ClassScenarioRunner : UdonSharpBehaviour
    {
        // エディタで ClassScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:10,3,bullet,(0.00, 0.00, 0.00);b:(0.00, 0.00, 3.00),10;c:20,(1.00, 0.00, 3.00),first,40,False;d:18,99;" +
            "e:6,10,True,True,True;f:40,(2.00, 0.00, 0.00),1;g:True,True,False,True;h:9,8,Move;i:6;";

        public Text display;

        private void Start()
        {
            Run();
        }

        public override void Interact()
        {
            Run();
        }

        public void Run()
        {
            string actual = ClassScenario.Run();
            bool ok = actual == Expected;

            string text = "#49 general class scenario\n"
                          + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";

            // 項目（a:～;）ごとに比べる
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

            Debug.Log("[ClassPoC] " + (ok ? "OK " : "NG ") + actual);
            if (display != null)
                display.text = text;
        }
    }
}
