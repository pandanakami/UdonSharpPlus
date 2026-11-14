using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #134：作り方（static・this のメソッド・ほかのインスタンスのメソッド・ラムダ・クロージャ・匿名メソッド・ローカル関数）×
    // 作る場所（UdonSharpBehaviour・普通のクラスのインスタンス／static・基底と派生・partial）×
    // 使い方（呼ぶ・+= / -=・LINQ・配列・別の UdonSharpBehaviour に渡す・メソッド越し。UdonSharpBehaviour と普通のクラスの両方で）の総当たり。
    // 作る方は 1 つずつ Use に渡し、Use が使い方を全部試す。各行は「作る場所と作り方=使い方ごとの結果」
    public partial class GmRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "A1=14,15,11,11,39,1,3,17,18,19,20|14,15,11,11,39,1,3,17,18;A2=15,20,11,11,30,1,2,30,35,40,45|15,20,11,11,30,1,2,30,35;A3=13,17,11,11,27,1,2,25,29,33,37|13,17,11,11,27,1,2,25,29;A4=18,24,11,11,36,1,3,36,42,48,54|18,24,11,11,36,1,3,36,42;A5=24,25,11,11,69,1,3,27,28,29,30|24,25,11,11,69,1,3,27,28;A6=15,20,11,11,30,1,2,30,35,40,45|15,20,11,11,30,1,2,30,35;A7=15,16,11,11,42,1,3,18,19,20,21|15,16,11,11,42,1,3,18,19;A8=-1,0,11,11,-6,1,0,2,3,4,5|-1,0,11,11,-6,1,0,2,3;A9=10,13,11,11,21,1,2,19,22,25,28|10,13,11,11,21,1,2,19,22;A10=14,16,11,11,51,1,3,29,31,33,35|30,32,11,11,99,1,3,45,47;B1=34,35,11,11,99,1,3,37,38,39,40|34,35,11,11,99,1,3,37,38;B2=10,11,11,11,27,1,3,13,14,15,16|10,11,11,11,27,1,3,13,14;B3=13,17,11,11,27,1,2,25,29,33,37|13,17,11,11,27,1,2,25,29;B4=18,24,11,11,36,1,3,36,42,48,54|18,24,11,11,36,1,3,36,42;B5=44,45,11,11,129,1,3,47,48,49,50|44,45,11,11,129,1,3,47,48;B6=15,20,11,11,30,1,2,30,35,40,45|15,20,11,11,30,1,2,30,35;B7=19,20,11,11,54,1,3,22,23,24,25|19,20,11,11,54,1,3,22,23;B8=-1,0,11,11,-6,1,0,2,3,4,5|-1,0,11,11,-6,1,0,2,3;B9=10,13,11,11,21,1,2,19,22,25,28|10,13,11,11,21,1,2,19,22;B10=22,24,11,11,75,1,3,37,39,41,43|38,40,11,11,123,1,3,53,55;C1=54,55,11,11,159,1,3,57,58,59,60|54,55,11,11,159,1,3,57,58;C2=64,65,11,11,189,1,3,67,68,69,70|64,65,11,11,189,1,3,67,68;C3=15,20,11,11,30,1,2,30,35,40,45|15,20,11,11,30,1,2,30,35;C4=9,10,11,11,24,1,3,12,13,14,15|9,10,11,11,24,1,3,12,13;C5=17,22,11,11,36,1,3,32,37,42,47|17,22,11,11,36,1,3,32,37;C6=8,11,11,11,45,3,3,35,38,41,44|40,43,11,11,141,3,3,67,70;C7=13,17,11,11,27,1,2,25,29,33,37|13,17,11,11,27,1,2,25,29;C8=18,24,11,11,36,1,3,36,42,48,54|18,24,11,11,36,1,3,36,42;D1=303,403,11,11,609,1,3,603,703,803,903|303,403,11,11,609,1,3,603,703;D2=0,1,11,11,-3,1,0,3,4,5,6|0,1,11,11,-3,1,0,3,4;D3=3,5,11,11,3,1,0,9,11,13,15|3,5,11,11,3,1,0,9,11;D4=12,16,11,11,24,1,2,24,28,32,36|12,16,11,11,24,1,2,24,28;D5=7,8,11,11,18,1,2,10,11,12,13|7,8,11,11,18,1,2,10,11;D6=2,4,11,11,0,1,0,8,10,12,14|2,4,11,11,0,1,0,8,10;D7=313,413,11,11,639,1,3,613,713,813,913|313,413,11,11,639,1,3,613,713";

        public GmPeer peer;
        public GmBase inst;   // 実体は GmDerived
        public GmDerived derived;
        public Text display;

        private int _f = 5;
        private GmItem _item = new GmItem(4);
        private GmPlain _plain = new GmPlain();
        private GmPass _pass = new GmPass();

        private static int SMethod(int x) => x + 11;
        private int IMethod(int x) => x * _f;

        private Func<int, int> A1() => SMethod;
        private Func<int, int> A2() => IMethod;
        private Func<int, int> A3() => _item.Mul;
        private Func<int, int> A4() => peer.Mul;
        private Func<int, int> A5() => x => x + 21;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        // 使い方（UdonSharpBehaviour の中）
        private string Use(Func<int, int> f)
        {
            string s = f(3) + "," + f.Invoke(4);
            Func<int, int> g = f;
            g += x => x * 2 + 1;
            s += "," + g(5);
            g -= f;
            s += "," + g(5);
            var list = new List<int> { 3, 1, 2 };
            s += "," + list.Select(f).Sum() + "," + list.OrderBy(f).First() + "," + list.Where(x => f(x) > 5).Count();
            var arr = new Func<int, int>[2];
            arr[1] = f;
            s += "," + arr[1](6);
            peer.Store(f);
            s += "," + peer.CallStored(7);
            s += "," + _pass.Call(f, 8) + "," + _pass.Id(f)(9);
            return s + "|" + _pass.Use(f, peer);
        }

        private string Scenario()
        {
            string s = "";
            // A：この UdonSharpBehaviour の中で作る
            s += "A1=" + Use(A1()) + ";";
            s += "A2=" + Use(A2()) + ";";
            s += "A3=" + Use(A3()) + ";";
            s += "A4=" + Use(A4()) + ";";
            s += "A5=" + Use(A5()) + ";";
            s += PartA();
            // B：普通のクラスのインスタンスのメソッドの中で作る
            s += "B1=" + Use(_plain.B1()) + ";";
            s += "B2=" + Use(_plain.B2()) + ";";
            s += "B3=" + Use(_plain.B3(_item)) + ";";
            s += "B4=" + Use(_plain.B4(peer)) + ";";
            s += "B5=" + Use(_plain.B5()) + ";";
            s += "B6=" + Use(_plain.B6()) + ";";
            s += "B7=" + Use(_plain.B7()) + ";";
            s += "B8=" + Use(_plain.B8(4)) + ";";
            s += "B9=" + Use(_plain.B9()) + ";";
            s += "B10=" + Use(_plain.B10()) + ";";
            // C：普通のクラスの static メソッドの中で作る
            s += "C1=" + Use(GmStatics.C1()) + ";";
            s += "C2=" + Use(GmStatics.C2()) + ";";
            s += "C3=" + Use(GmStatics.C3(4)) + ";";
            s += "C4=" + Use(GmStatics.C4(6)) + ";";
            s += "C5=" + Use(GmStatics.C5()) + ";";
            s += "C6=" + Use(GmStatics.C6(3)) + ";";
            s += "C7=" + Use(GmStatics.C7(_item)) + ";";
            s += "C8=" + Use(GmStatics.C8(peer)) + ";";
            // D：基底・派生の UdonSharpBehaviour の中で作る（実体は派生）
            s += "D1=" + Use(inst.D1()) + ";";
            s += "D2=" + Use(inst.D2()) + ";";
            s += "D3=" + Use(inst.D3()) + ";";
            s += "D4=" + Use(inst.D4()) + ";";
            s += "D5=" + Use(derived.D5()) + ";";
            s += "D6=" + Use(derived.D6()) + ";";
            s += "D7=" + Use(derived.D7());
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#134 matrix\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            string rows = "";
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                if (ei == ai)
                    rows += "OK ";
                else
                {
                    rows += "NG ";
                    text += $"NG  {ai}\n    expected {ei}\n";
                }
            }
            text += rows + "\n";
            resultName = "#134 matrix"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #134 matrix " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
