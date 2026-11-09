using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #58: LinqScenario を Udon 上で実行し、普通の C#（エディタ）で実行した結果と比べて OK/NG を表示する。
    // Start で 1 回実行し、ボタン（Interact）でもう一度実行できる。
    public class LinqScenarioRunner : UdonSharpBehaviour
    {
        // エディタで LinqScenario.Run() を普通の C# として実行した結果（m は RunInstance の結果を手で書いたもの）
        private const string Expected =
            "a:10 16 12,242;b:3,586<5><8><6>;c:True,True,False,True,False,6,2,True,False;d:5,5,0,7,3,0;e:26,220,1,9,-1,5.5,2.75;" +
            "f:True3,15,1;g:annbob2,bob,4,2cid;h:3,3,12;" +
            "i:1 3 3 5 6 8,92,fig pear kiwi date apple,bobanncid;j:3,5,80 60 50;k:5 3 8 1 6,3c,8 1 6,8,2,0;l:50,10,3;" +
            "n:faygus,eve,150,fayeve,True,3blue,2;" +
            "m:431,1;m:431,1;";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        // UdonSharpBehaviour のメソッドの中（テンポラリは呼び出し箇所ごとのフィールドを Clear して使い回す。2 回呼んで確かめる）
        private int[] _values = { 4, 1, 4, 3, 1 };

        private string RunInstance()
        {
            List<int> sorted = _values.Distinct().OrderByDescending(x => x).ToList();
            int smallest = _values.OrderBy(x => x).First();
            string s = "";
            foreach (int x in sorted)
                s += x;
            return "m:" + s + "," + smallest + ";";
        }

        public void Run()
        {
            string actual = LinqScenario.Run() + RunInstance() + RunInstance();
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

            Debug.Log("[FeatureTests] #58 " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
