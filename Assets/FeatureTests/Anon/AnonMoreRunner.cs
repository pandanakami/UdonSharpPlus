using System;
using System.Collections.Generic;
using System.Linq;
using FeatureTests.Delegate;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Anon
{
    // #70 の追加テスト：機能単体（U）と、ほかの機能との組み合わせ（C）
    public partial class AnonMoreRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "U1=123,3;U2=TrueFalseFalseFalseFalseFalse;U4=[]<>True;U5=197;U6=TrueFalseFalseFalseTrue;U7={ A = 1, Inner =  };U8=L5bob!3n,{ local = L, _bonus = 5, Label = bob!, class = 3, 名前 = n };U9={ by = 200, ui = 4000000000, ul = 18000000000000000000, fl = 1.5, db = 2.25, nan = NaN, inf = Infinity, ns = , sh = -3, sb = -4, ch = z, us = 65535 };U10=|           { A = 1 }||{ A = 1 }           |;U11=x{ A = 2 }{ A = 1 };U12=True,1;C1=101,201,301,301;C2=4,14,True,4;C3=1,1,t1,q;C4={ a = 1, b = x }{ a = y, b = 2 }{ a = 3, b = z };C5=holder4,31,6,68,bob,True,holder4;C6=n6n5n4n3n2,5;C7=100,1;C8=2,2,6,3,3;C10={ Score = 3, Name = a },{ Name = a, Score = 3 },False,True,{ Name = a, Score = 3 }True{ Score = 3, Name = a };C11={ Src = on, N = 1 }";

        public GmPeer gm;
        public AnonPeer peer2;
        public Text display;

        private int _c;
        private int _bonus = 5;
        private bool _off;

        private int Next() => ++_c;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var item = new AnItem("bob", 7);
            var sq = new AnSquare();
            var holder = new AnHolder();
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };

            // U1：initializer は左から 1 回ずつ評価する
            _c = 0;
            var o1 = new { A = Next(), B = Next(), C = Next() };
            s += "U1=" + o1.A + o1.B + o1.C + "," + _c + ";";

            // U2・U3：並び・名前・型が違えば別の型（値が同じでも Equals は false）
            var t1 = new { A = 1, B = 2 };
            var t2 = new { B = 2, A = 1 };
            var t3 = new { A = 1, B = "2" };
            var t4 = new { A = 1L, B = 2 };
            var t5 = new { a = 1, B = 2 };
            var t6 = new { A = 1, B = 2 };
            s += "U2=" + t1.Equals(t6) + t1.Equals(t2) + t1.Equals(t3) + t1.Equals(t4) + t1.Equals(t5) + t2.Equals(t1) + ";";

            // U4：null の匿名型は、連結・補間では空の文字列
            var nul = _off ? new { A = 1 } : null;
            s += "U4=[" + nul + "]" + $"<{nul}>" + (nul == null) + ";";

            // U5：値型は作ったときの値、参照型のメンバーは作ったあとの変更が見える
            int mv = 1;
            var ar = new AnItem("n", 1);
            int[] arr2 = { 1, 2 };
            var snap = new { mv, Item = ar, Arr = arr2 };
            mv = 2;
            ar.Hp = 9;
            arr2[0] = 7;
            s += "U5=" + snap.mv + snap.Item.Hp + snap.Arr[0] + ";";

            // U6：参照型のメンバーの Equals（同じ参照・同じ内容の別の参照・null）
            var r1 = new { Arr = arr2, Item = ar, Shape = (IAnShape)sq };
            var r2 = new { Arr = arr2, Item = ar, Shape = (IAnShape)sq };
            var r3 = new { Arr = new[] { 7, 2 }, Item = ar, Shape = (IAnShape)sq };
            var r4 = new { Arr = arr2, Item = new AnItem("n", 9), Shape = (IAnShape)sq };
            var r5 = new { Arr = arr2, Item = ar, Shape = (IAnShape)null };
            var r6 = new { Arr = arr2, Item = ar, Shape = (IAnShape)null };
            s += "U6=" + r1.Equals(r2) + r1.Equals(r3) + r1.Equals(r4) + r1.Equals(r5) + r5.Equals(r6) + ";";

            // U7：null の入れ子の匿名型の ToString
            var outer = new { A = 1, Inner = _off ? new { B = 2 } : null };
            s += "U7=" + outer + ";";

            // U8：省略したメンバー名（変数・this のフィールド・ほかのオブジェクトのプロパティ・キーワード・日本語）
            string local = "L";
            var nm = new { local, this._bonus, item.Label, @class = 3, 名前 = "n" };
            s += "U8=" + nm.local + nm._bonus + nm.Label + nm.@class + nm.名前 + "," + nm + ";";

            // U9：文字列にする型
            byte by = 200;
            uint ui = 4000000000;
            ulong ul = 18000000000000000000;
            float fl = 1.5f;
            double db = 2.25;
            float nan = float.NaN;
            double inf = double.PositiveInfinity;
            string ns = null;
            short sh = -3;
            sbyte sb = -4;
            char ch = 'z';
            ushort us = 65535;
            var st = new { by, ui, ul, fl, db, nan, inf, ns, sh, sb, ch, us };
            s += "U9=" + st + ";";

            // U10：補間文字列の幅の指定
            var al = new { A = 1 };
            s += "U10=" + $"|{al,20}|" + $"|{al,-20}|" + ";";

            // U11：条件式の全体を文字列にする
            var al2 = new { A = 2 };
            s += "U11=" + "x" + (_off ? al : al2) + $"{(_off ? al : null)}" + $"{(!_off ? al : null)}" + ";";

            // U12：Equals の引数は 1 回だけ評価する
            _c = 0;
            var e12 = new { A = 1, B = 2 };
            bool eq12 = e12.Equals(new { A = Next(), B = 2 });
            s += "U12=" + eq12 + "," + _c + ";";

            // C1：ラムダ・匿名メソッド・ローカル関数が匿名型を使い、別の UdonSharpBehaviour に渡す・つなぐ
            var cap = new { Base = 100, Tag = "t" };
            Func<int, int> lam = x => x + cap.Base;
            Func<int, int> anon = delegate (int x) { return x + cap.Base * 2; };
            int Loc(int x) => x + cap.Base * 3;
            Func<int, int> loc = Loc;
            gm.Store(lam);
            int c1a = gm.CallStored(1);
            gm.Store(anon);
            int c1b = gm.CallStored(1);
            gm.Store(loc);
            int c1c = gm.CallStored(1);
            Func<int, int> multi = lam;
            multi += anon;
            multi += loc;
            s += "C1=" + c1a + "," + c1b + "," + c1c + "," + multi(1) + ";";

            // C2：匿名型を使う Predicate・Func を、List・LINQ に渡す
            var lim = new { Min = 2, Max = 5 };
            Predicate<int> inRange = x => x >= lim.Min && x <= lim.Max;
            Func<int, bool> fRange = x => x >= lim.Min && x <= lim.Max;
            s += "C2=" + nums.FindAll(inRange).Count + "," + nums.Where(fRange).Sum() + "," + nums.Exists(inRange) + "," + nums.Count(fRange) + ";";

            // C3：ジェネリックメソッドを何段も通す・ジェネリックメソッドの中で別の匿名型を作る
            var w = AnGen.Wrap(AnGen.Id(new { A = 1 }));
            var w2 = AnGen.Wrap(w);
            s += "C3=" + w[0].A + "," + w2[0][0].A + "," + AnGen.Describe(new { Z = 1 }) + "," + AnGen.One(AnGen.Id(new { A = 5, B = "q" }))[0].B + ";";

            // C4：クラスの型引数とメソッドの型引数を同時にメンバーにする
            var pr = new AnPair<int>();
            s += "C4=" + pr.Mix<string>(1, "x") + new AnPair<string>().Mix<int>("y", 2) + pr.Mix<string>(3, "z") + ";";

            // C5：this・普通のクラス・インタフェース・UdonSharpBehaviour・ジェネリッククラスをメンバーにする
            var gb = new AnGenBox<int>();
            gb.v = 8;
            gb.n = 6;
            var mix = new { Self = holder, Shape = (IAnShape)new AnCircle(), Peer = gm, G = gb, Item = item };
            s += "C5=" + mix.Self.Describe() + "," + mix.Shape.Area() + "," + mix.Peer.m + "," + mix.G.n + mix.G.v + "," + mix.Item.Name + ","
                 + mix.Equals(new { Self = holder, Shape = mix.Shape, Peer = gm, G = gb, Item = item }) + "," + holder.SelfPair() + ";";

            // C6：LINQ で、匿名型から別の匿名型に何段も射影する
            var multiStage = nums.Select(x => new { x, sq = x * x }).Where(a => a.sq > 3).Select(a => new { a.x, label = "n" + a.x }).OrderByDescending(b => b.x).ToList();
            string ms = "";
            foreach (var m in multiStage)
                ms += m.label;
            s += "C6=" + ms + "," + multiStage.Count + ";";

            // C7：条件演算子の両側が同じ型で、選んだ側の initializer だけ評価する
            _c = 0;
            var cond = _off ? new { V = Next() } : new { V = Next() * 100 };
            s += "C7=" + cond.V + "," + _c + ";";

            // C8：配列・List のメンバー・匿名型のリストをメンバーにする
            var proj = nums.Select(x => new { N = x }).ToList();
            var nest = new { Items = new[] { 3, 1, 2 }, Names = new List<string> { "a", "b" }, Rows = proj };
            s += "C8=" + nest.Items.Where(i => i > 1).Count() + "," + nest.Names.Count + "," + nest.Rows.Count + "," + nest.Items.Length + "," + nest.Rows[2].N + ";";

            // C10：partial の別のファイル・別の UdonSharpBehaviour
            s += "C10=" + PartMore() + "," + peer2.Describe() + ";";

            // C11：プリプロセッサ（有効な側だけから作る）
#region 匿名型
#pragma warning disable 0219
#if true
            var pp = new { Src = "on", N = 1 };
#else
            var pp = new { Src = "off", Other = 2 };
#endif
#pragma warning restore 0219
#endregion
            s += "C11=" + pp;
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#70 anonymous types (more)\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#70 anonymous types (more)"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #70 anon more " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
