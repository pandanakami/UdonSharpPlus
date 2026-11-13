using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #57: LambdaScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class LambdaScenarioRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // エディタで LambdaScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:5,1,2,4,0,-1;b:True,False,True,False;c:0,5 3 1;d:33,3;e:8;f:8;g:8 6 5 3 1,acdbe;h:acd,a,2,ace;i:2,True;j:2,3,0;" +
            "k:2,4,2,-1,2,4,-1;l:H7L2,u7u2u9u4u9u1,2 2 2 2 2 2;";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = LambdaScenario.Run();
            bool ok = actual == Expected;

            string text = "#57 lambda scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            resultName = "#57 lambda"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #57 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
