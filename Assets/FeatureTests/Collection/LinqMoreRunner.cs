using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #137 の子チケット：添字付きのラムダ（#142）・SelectMany（#143）・足した LINQ のメソッド（#144）・
    // Enumerable.Range / Repeat / Empty（#145）・LINQ の結果をそのまま渡す（#146）
    public class LmBox
    {
        public string name;
        public List<int> items;
        public string[] tags;

        public LmBox(string name, List<int> items, string[] tags)
        {
            this.name = name;
            this.items = items;
            this.tags = tags;
        }
    }

    public class LinqMoreRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "I1=70,3,3;I2=b0a1c2,1;I3=6,5;M1=6,3,pqr;M2=x1x2z3;M3=2,3,1,2,3;M4=26,3,8;M5=aa1bb2;R1=213,6,18;R2=9,12,3,140;R3=7,0,0bacz;W1=6,15,3;W2=5,2,1,2;E1=3,0,True,6;E2=3,0,6;E3=720,31,bac,21,-1;G1=15,4,ababab,0,12,4,6,3;J1=b!,a!,c!|5-6|b+a+c|1,2,3,4,5,6|bbaacc|0.5,1,1.5,2,2.5,3|1,2,3";

        public Text display;

        private int _c;

        private List<int> ItemsOf(LmBox b) => b.items;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };
            int[] arr = { 3, 1, 2 };
            var words = new List<string> { "b", "a", "c" };
            var boxes = new List<LmBox>
            {
                new LmBox("x", new List<int> { 1, 2 }, new[] { "p", "q" }),
                new LmBox("y", new List<int>(), new string[0]),
                new LmBox("z", new List<int> { 3 }, new[] { "r" }),
            };
            var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };

            // I：添字付きのラムダ（添字は、その段に来た要素の順番）
            s += "I1=" + nums.Select((x, i) => x * i).Sum() + "," + nums.Where((x, i) => i % 2 == 0).Count() + "," + nums.Where(x => x > 2).Select((x, i) => i).Last() + ";";
            string i2 = "";
            foreach (string w in words.Select((w, i) => w + i))
                i2 += w;
            s += "I2=" + i2 + "," + arr.Where((x, i) => x > i).ToArray().Length + ";";
            s += "I3=" + nums.TakeWhile((x, i) => i < 3).Sum() + "," + nums.SkipWhile((x, i) => i < 4).First() + ";";

            // M：SelectMany
            string m1 = "";
            foreach (string t in boxes.SelectMany(b => b.tags))
                m1 += t;
            s += "M1=" + boxes.SelectMany(b => b.items).Sum() + "," + boxes.SelectMany(b => b.tags).Count() + "," + m1 + ";";
            string m2 = "";
            foreach (string t in boxes.SelectMany(b => b.items, (b, i) => b.name + i))
                m2 += t;
            s += "M2=" + m2 + ";";
            _c = 0;
            int m3 = boxes.SelectMany(b => { _c++; return b.items; }).Take(2).Sum();
            s += "M3=" + boxes.SelectMany(b => b.items.Where(i => i > 1)).ToList().Count + "," + m3 + "," + _c + "," + boxes.Take(2).SelectMany(b => b.items).Count() + ","
                 + boxes.SelectMany(b => b.items).OrderByDescending(i => i).First() + ";";
            s += "M4=" + boxes.SelectMany((b, i) => b.items.Select(x => x + i * 10)).Sum() + "," + boxes.SelectMany(ItemsOf).Count() + ","
                 + boxes.SelectMany(b => b.items).Where(i => i != 2).Select(i => i * 2).Sum() + ";";
            string m5 = "";
            foreach (string t in dict.SelectMany(kv => new[] { kv.Key, kv.Key + kv.Value }))
                m5 += t;
            s += "M5=" + m5 + ";";

            // R：Reverse・Concat・Append・Prepend
            string r1 = "";
            foreach (int x in arr.Reverse())
                r1 += x;
            s += "R1=" + r1 + "," + nums.Where(x => x > 3).Reverse().First() + "," + nums.Select(x => x * 2).Reverse().Skip(1).Take(2).Sum() + ";";
            s += "R2=" + nums.Concat(arr).Count() + "," + arr.Concat(nums.Where(x => x > 5)).Sum() + "," + words.Concat(words).Distinct().Count() + ","
                 + nums.Where(x => x < 3).Concat(nums.Where(x => x > 4)).Select(x => x * 10).Sum() + ";";
            string r3 = "";
            foreach (string w in words.Append("z").Prepend("0"))
                r3 += w;
            s += "R3=" + nums.Append(7).Last() + "," + nums.Prepend(0).First() + "," + r3 + ";";

            // W：TakeWhile・SkipWhile（条件を呼ぶ回数も C# と同じ）
            s += "W1=" + nums.TakeWhile(x => x < 4).Sum() + "," + nums.SkipWhile(x => x < 4).Sum() + "," + nums.SkipWhile(x => x < 3).TakeWhile(x => x < 6).Count() + ";";
            _c = 0;
            int w2a = nums.SkipWhile(x => { _c++; return x < 2; }).Count();
            int c2a = _c;
            _c = 0;
            int w2b = nums.TakeWhile(x => { _c++; return x < 2; }).Count();
            s += "W2=" + w2a + "," + c2a + "," + w2b + "," + _c + ";";

            // E：ElementAt・Single・Aggregate
            s += "E1=" + nums.ElementAt(2) + "," + nums.ElementAtOrDefault(10) + "," + (words.ElementAtOrDefault(9) == null) + "," + nums.Where(x => x > 4).ElementAt(1) + ";";
            s += "E2=" + nums.Single(x => x == 3) + "," + nums.SingleOrDefault(x => x > 9) + "," + nums.Where(x => x == 6).Single() + ";";
            s += "E3=" + nums.Aggregate((a, b) => a * b) + "," + nums.Aggregate(10, (a, b) => a + b) + "," + words.Aggregate("", (a, w) => a + w) + ","
                 + nums.Aggregate(0L, (a, b) => a + b) + "," + nums.Where(x => x > 4).Aggregate((a, b) => a - b) + ";";

            // G：Enumerable.Range・Repeat・Empty
            string g1 = "";
            foreach (string t in Enumerable.Repeat("ab", 3))
                g1 += t;
            var range = Enumerable.Range(3, 3);
            int g2 = 0;
            foreach (int x in range)
                g2 += x;
            s += "G1=" + Enumerable.Range(1, 5).Sum() + "," + Enumerable.Range(0, 4).Select(i => i * i).ToList().Count + "," + g1 + "," + Enumerable.Empty<int>().Count() + ","
                 + g2 + "," + Enumerable.Range(0, 10).Where(i => i % 3 == 0).ToArray().Length + "," + Enumerable.Repeat(2, 3).Sum() + "," + range.Count() + ";";

            // J：LINQ の結果・List を、そのまま string.Join / string.Concat に渡す
            s += "J1=" + string.Join(",", words.Select(w => w + "!")) + "|" + string.Join("-", nums.Where(x => x > 4)) + "|" + string.Join("+", words) + "|" + string.Join(",", nums) + "|"
                 + string.Concat(words.Select(w => w + w)) + "|" + string.Join(",", nums.Select(x => x * 0.5f)) + "|" + string.Join(",", Enumerable.Range(1, 3));
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#137 LINQ more\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#137 LINQ more"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #137 linq more " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
