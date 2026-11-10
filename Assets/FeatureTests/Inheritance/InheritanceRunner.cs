using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Inheritance
{
    // #92: InheritanceScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する
    public class InheritanceRunner : UdonSharpBehaviour
    {
        // エディタで InheritanceScenario.Run() を普通の C# として実行した結果
        private const string Expected = "20,base,22 10,base,11 ;False,True;22,22,16;43;";

        public UdonSharpBehaviour[] targets;   // Knight, Archer
        public InheritUnitBase baseTyped;      // Knight
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            string actual = InheritanceScenario.Run(targets, baseTyped, this);
            bool ok = actual == Expected;
            string text = "#92 behaviour inheritance + interface\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
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
            Debug.Log("[FeatureTests] #92 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
