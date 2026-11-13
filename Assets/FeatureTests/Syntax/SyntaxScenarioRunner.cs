using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #79: SyntaxScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、Interact でもう一度実行できる。
    public class SyntaxScenarioRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // エディタで SyntaxScenario.Run() を普通の C# として実行した結果
        private const string Expected =
            "a:3,x3,p9,4,6,10;b:6,2,8,4;c:5,[z],25,4,6,True,False;d:1,1,1,0,12,4,9;" +
            "n:2/11/3 3/12/4 4/13/5 5/14/6 101/201/102,TrueFalseTrueTrueTrue-3True,7,14,5,21,28;" +
            "e1:0,F,-1,F,FT,-1,0,0,0,0,0,0,0,0;e2:0,FFT,0,0,0,F,0,0,0,0,0,0,null,0;e3:5,5,5,5,5,5,0,1,5,1,0,0,5,4,1,T,0;" +
            "e4:0,FFF,F0,0,0,0,0,a3,3,a,T0;f1:3,TT,1,1,2,1,T2,T,1,1,4,T,4,TTTTT,T,1,9;f2:TT,4,6,T,T,T,1;g:1,2,6,1,9,2,21,2,2;" +
            "h1:3,5,16 8 7,12,1,31,14,2,3,6,1;h2:2,6,2,17,3,2,7,z!,1;";

        public SyntaxTarget target;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = SyntaxScenario.Run(target) + BoundaryScenario.Run();
            bool ok = actual == Expected;

            string text = "#79 syntax variations / boundaries\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            resultName = "#79 syntax"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #79 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
