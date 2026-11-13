using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Generic
{
    // #59: GenericScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class GenericScenarioRunner : GenericScenarioBase
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // エディタで GenericScenario.Run() を普通の C# として実行した結果と、z（このクラスのジェネリックメソッド）
        private const string Expected =
            "a:3,5,4,0,True,False;b:xyTrue,bob2True;c:3z;d:9,plum,1.5,pear,9;e:b/2,3,1;f:2,1,0,True,3;g:3,3;h:5,1,0,5;" +
            "z:2q7w;y:3b,2,1,9x;";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        // UdonSharpBehaviour のジェネリックメソッド
        private void Swap<T>(T[] array, int i, int j)
        {
            T t = array[i];
            array[i] = array[j];
            array[j] = t;
        }

        private T Echo<T>(T value) => value;

        private string RunBehaviour()
        {
            int[] numbers = { 1, 2 };
            string[] texts = { "p", "q" };
            Swap(numbers, 0, 1);
            Swap(texts, 0, 1);
            string r = "z:" + numbers[0] + texts[0] + Echo(7) + Echo<string>("w") + ";";

            // y: 基底クラスのジェネリックメソッドを、継承したクラスから呼ぶ
            int[] more = { 1, 3, 1 };
            string[] words = { "a", "b" };
            r += "y:" + Pick(more, 1) + this.Pick(words, 1) + "," + CountOf(more, 1) + "," + CountOf(words, "a") + "," + PickBoth(new[] { 9 }, new[] { "x" }) + ";";
            return r;
        }

        public void Run()
        {
            string actual = GenericScenario.Run() + RunBehaviour();
            bool ok = actual == Expected;

            string text = "#59 Generics scenario\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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

            resultName = "#59 generics"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #59 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
