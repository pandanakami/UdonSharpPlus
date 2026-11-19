using System.Collections.Generic;
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
            "k:2,4,2,-1,2,4,-1;l:H7L2,u7u2u9u4u9u1,2 2 2 2 2 2;" +
            "m:5 4 3 1 1,39 38 37 36 35 34 33 32 31 30 29 28 27 26 25 24 23 22 21 20 19 18 17 16 15 14 13 12 11 10 9 8 7 6 5 4 3 2 1 0,5 4 3 1 1,16 15 14 13 12 11 10 9 8 7 6 5 4 3 2 1 0,0 3 6 9 12 15 18 1 4 7 10 13 16 19 2 5 8 11 14 17,2 1;";

        // #171：UdonSharpBehaviour のメソッドの中の Sort は、作業用の配列（初めは 16 個分）を呼び出し箇所ごとに使い回す。
        // 同じ箇所で、小さいリスト・16 個を超えるリスト（作り直す）・また小さいリスト（前の中身が残った配列）を並べ替える
        private string SortDesc(List<int> l)
        {
            l.Sort((a, b) => b - a);
            return string.Join(" ", l);
        }

        // 同じキーは元の順のまま（キーは 100 の位）
        private string StableByKey(List<int> l)
        {
            l.Sort((a, b) => a / 100 - b / 100);
            string s = "";
            foreach (int v in l)
                s += (s.Length == 0 ? "" : " ") + v % 100;
            return s;
        }

        private string SortReuse()
        {
            var a = new List<int>();
            for (int i = 0; i < 40; i++) a.Add(i * 7 % 40);
            var c = new List<int>();
            for (int i = 0; i < 17; i++) c.Add(i * 5 % 17);
            var d = new List<int>();
            for (int i = 0; i < 20; i++) d.Add(i % 3 * 100 + i);
            return "m:" + SortDesc(new List<int> { 3, 1, 4, 1, 5 }) + "," + SortDesc(a) + "," + SortDesc(new List<int> { 3, 1, 4, 1, 5 }) + "," + SortDesc(c) + ","
                   + StableByKey(d) + "," + SortDesc(new List<int> { 2, 1 }) + ";";
        }

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = LambdaScenario.Run() + SortReuse();
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
