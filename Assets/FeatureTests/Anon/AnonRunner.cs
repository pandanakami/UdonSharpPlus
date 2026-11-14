using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Anon
{
    // #70：匿名型（new { ... }）。作る・メンバーを読む・メンバー名の省略・入れ子・ToString・Equals・文字列の連結・LINQ の射影・配列・
    // 普通のクラスの中・ラムダ・ローカル関数・クロージャ・partial
    public partial class AnonRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "a3,4;bob75,True,False,False;1x2.5,True,True;{ Name = a, Score = 3 }|[{ Name = a, Score = 3 }]|<{ Name = a, Score = 3 }>|pre:{ Name = a, Score = 3 }|{ A = 1, Inner = { B = x, C = 2.5 } }|{ V = (1.00, 2.00, 3.00), Nul = , Ch = c, Ok = True, L = 5 };True,False,False,False;2,95,30;ab3,b,2;{ Name = bob, Hp = 7 },17;101,{ x = 2, Tag = t },106;1,2;1,True;1b,2,{ value = 4 }{ value = v };{ Name = a, Score = 3 },True,3";

        public Text display;

        private int _bonus = 5;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";

            // 1：作る・メンバーを読む
            var p = new { Name = "a", Score = 3 };
            s += p.Name + p.Score + "," + (p.Score + 1) + ";";

            // 2：メンバー名の省略（変数・フィールド・メンバー）・同じ並びなら同じ型（値で比べる。== は参照の比較）
            var item = new AnItem("bob", 7);
            var q = new { item.Name, item.Hp, _bonus };
            var r = new { Name = "bob", Hp = 7, _bonus = 5 };
            s += q.Name + q.Hp + q._bonus + "," + q.Equals(r) + "," + (q == r) + "," + q.Equals(new { Name = "bob", Hp = 8, _bonus = 5 }) + ";";

            // 3：入れ子
            var n = new { A = 1, Inner = new { B = "x", C = 2.5f } };
            s += n.A + n.Inner.B + n.Inner.C + "," + n.Inner.Equals(new { B = "x", C = 2.5f }) + "," + n.Equals(new { A = 1, Inner = new { B = "x", C = 2.5f } }) + ";";

            // 4：ToString・文字列の連結・補間・+=（値型・null の string・char・bool・long・入れ子）
            string none = null;
            var t = new { V = new Vector3(1, 2, 3), Nul = none, Ch = 'c', Ok = true, L = 5L };
            string ss = "pre:";
            ss += p;
            s += p.ToString() + "|" + "[" + p + "]" + "|" + $"<{p}>" + "|" + ss + "|" + n + "|" + t + ";";

            // 5：Equals（メンバーごとの値・普通のクラスのメンバーは参照・null・匿名型でないもの）
            var e1 = new { A = 1, S = "k", P = item };
            var e2 = new { A = 1, S = "k", P = item };
            var e3 = new { A = 1, S = "k", P = new AnItem("bob", 7) };
            s += e1.Equals(e2) + "," + e1.Equals(e3) + "," + e1.Equals(null) + "," + e1.Equals((object)5) + ";";

            // 6：LINQ の射影（匿名型のリスト・ラムダの中で読む）
            var nums = new List<int> { 1, 2, 3, 4 };
            var proj = nums.Select(x => new { N = x, Sq = x * x }).Where(a => a.Sq > 4).ToList();
            int tot = 0;
            foreach (var a in proj)
                tot += a.N * 10 + a.Sq;
            s += proj.Count + "," + tot + "," + nums.Select(x => new { N = x, D = x * 2 }).Sum(a => a.N + a.D) + ";";

            // 7：匿名型の配列
            var arr = new[] { new { K = "a", V = 1 }, new { K = "b", V = 2 } };
            string ks = "";
            int vs = 0;
            foreach (var x in arr)
            {
                ks += x.K;
                vs += x.V;
            }
            s += ks + vs + "," + arr[1].K + "," + arr.Length + ";";

            // 8：普通のクラスの中で作る・使う
            s += item.Describe() + "," + new AnItem("z", 4).Total(new List<AnItem> { item, new AnItem("y", 5) }) + ";";

            // 9：ラムダ・クロージャ・ローカル関数で使う
            var cap = new { Base = 100, Tag = "t" };
            Func<int, int> addCap = x => x + cap.Base;
            Func<int, string> tagged = x => new { x, cap.Tag }.ToString();
            int Loc(int x) => new { Y = x * 2 }.Y + cap.Base;
            s += addCap(1) + "," + tagged(2) + "," + Loc(3) + ";";

            // 10：作った時点の値（あとで元の変数を変えても変わらない）
            int mutable = 1;
            var snap = new { mutable };
            mutable = 2;
            s += snap.mutable + "," + mutable + ";";

            // 11：条件演算子・null との比較
            var tern = mutable > 1 ? new { W = 1 } : new { W = 2 };
            s += tern.W + "," + (tern != null) + ";";

            // 12：ジェネリックメソッドの型引数・ジェネリッククラスの中の匿名型（型引数ごとに別の型）
            var viaId = AnGen.Id(new { A = 1, B = "b" });
            var one = AnGen.One(new { A = 2 });
            var bi = new AnBox<int>();
            bi.value = 4;
            var bs = new AnBox<string>();
            bs.value = "v";
            s += viaId.A + viaId.B + "," + one[0].A + "," + bi.Show() + bs.Show() + ";";

            // 13：partial の別のファイルの、同じ並びの匿名型
            s += PartAnon();
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#70 anonymous types\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#70 anonymous types"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #70 anon " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
