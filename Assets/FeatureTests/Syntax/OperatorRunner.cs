using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #105：ユーザー定義の演算子・変換を持つ普通のクラス
    public class OpVec
    {
        public int x, y;
        public OpVec(int x, int y) { this.x = x; this.y = y; }

        public static OpVec operator +(OpVec a, OpVec b) => new OpVec(a.x + b.x, a.y + b.y);
        public static OpVec operator -(OpVec a, OpVec b) => new OpVec(a.x - b.x, a.y - b.y);
        public static OpVec operator -(OpVec a) => new OpVec(-a.x, -a.y);
        public static OpVec operator +(OpVec a) => a;
        public static OpVec operator *(OpVec a, int k) => new OpVec(a.x * k, a.y * k);
        public static OpVec operator *(int k, OpVec a) => new OpVec(a.x * k * 10, a.y * k * 10);   // 左右で違う結果にして、どちらが呼ばれたか分かるように
        public static OpVec operator /(OpVec a, int k) => new OpVec(a.x / k, a.y / k);
        public static OpVec operator %(OpVec a, int k) => new OpVec(a.x % k, a.y % k);
        public static OpVec operator ++(OpVec a) => new OpVec(a.x + 1, a.y + 1);
        public static OpVec operator --(OpVec a) => new OpVec(a.x - 1, a.y - 1);
        public static bool operator ==(OpVec a, OpVec b)
        {
            if ((object)a == null || (object)b == null)
                return (object)a == (object)b;
            return a.x == b.x && a.y == b.y;
        }
        public static bool operator !=(OpVec a, OpVec b) => !(a == b);
        public static bool operator <(OpVec a, OpVec b) => a.x + a.y < b.x + b.y;
        public static bool operator >(OpVec a, OpVec b) => a.x + a.y > b.x + b.y;
        public static bool operator <=(OpVec a, OpVec b) => !(a > b);
        public static bool operator >=(OpVec a, OpVec b) => !(a < b);
        public static OpVec operator !(OpVec a) => new OpVec(a.y, a.x);
        public static OpVec operator ~(OpVec a) => new OpVec(~a.x, ~a.y);
        public static OpVec operator &(OpVec a, OpVec b) => new OpVec(a.x & b.x, a.y & b.y);
        public static OpVec operator |(OpVec a, OpVec b) => new OpVec(a.x | b.x, a.y | b.y);
        public static OpVec operator ^(OpVec a, OpVec b) => new OpVec(a.x ^ b.x, a.y ^ b.y);
        public static OpVec operator <<(OpVec a, int n) => new OpVec(a.x << n, a.y << n);
        public static OpVec operator >>(OpVec a, int n) => new OpVec(a.x >> n, a.y >> n);

        public static implicit operator OpVec(int v) => new OpVec(v, v);
        public static explicit operator int(OpVec v) => v.x * 100 + v.y;
        public static explicit operator string(OpVec v) => "<" + v.x + "," + v.y + ">";

        public static string S(OpVec v) => (object)v == null ? "null" : v.x + ":" + v.y;

        // 演算子を、クラスの中のメソッド・プロパティから使う
        public OpVec Twice() => this + this;
        public int Len => (int)(this * 1);
    }

    // 演算子を持つジェネリッククラス
    public class OpBox<T>
    {
        public T v;
        public OpBox(T v) { this.v = v; }
        public static OpBox<T> operator +(OpBox<T> a, OpBox<T> b) => new OpBox<T>(b.v);
        public static implicit operator T(OpBox<T> b) => b.v;
        public static implicit operator OpBox<T>(T v) => new OpBox<T>(v);
    }

    public static class OpVecUtil
    {
        public static OpVec Sum(OpVec[] all)
        {
            OpVec s = 0;
            foreach (OpVec v in all)
                s += v;
            return s;
        }
        public static OpVec Scale(this OpVec v, int k) => v * k;
        public static OpVec Plus(this OpVec v, OpVec w) => v + w;
    }

    public interface IOpShape
    {
        OpVec Corner(OpVec origin);
    }

    public class OpSquare : IOpShape
    {
        public OpVec size = new OpVec(2, 2);
        public OpVec Corner(OpVec origin) => origin + size;
    }

    public class OpHolder
    {
        public OpVec pos = new OpVec(0, 0);
        public OpVec[] trail = { new OpVec(1, 1), new OpVec(2, 2) };
        public OpVec Pos { get; set; } = new OpVec(5, 5);
        public void Move(OpVec d) { pos += d; }
    }

    // 呼ばれた演算子を記録する（オーバーロードの選ばれ方・呼ばれる回数・順番を見る。#105 の追加テスト）
    public class OpLog
    {
        public string s = "";
    }

    public class OpNum
    {
        public int v;
        public OpLog log;
        public OpNum(int v, OpLog log) { this.v = v; this.log = log; }

        public static OpNum operator +(OpNum a, OpNum b) { a.log.s += "N"; return new OpNum(a.v + b.v, a.log); }
        public static OpNum operator +(OpNum a, int b) { a.log.s += "I"; return new OpNum(a.v + b, a.log); }
        public static OpNum operator +(OpNum a, long b) { a.log.s += "L"; return new OpNum(a.v + 2000, a.log); }
        public static OpNum operator +(OpNum a, float b) { a.log.s += "F"; return new OpNum(a.v + 1000, a.log); }
        public static OpNum operator -(OpNum a, OpNum b) => a + new OpNum(-b.v, a.log);   // 別の演算子を呼ぶ
        public static OpNum operator *(OpNum a, int k)
        {
            if (k == 0)
                return new OpNum(0, a.log);
            if (k < 0)
                return -(a * -k);
            OpNum r = a;
            for (int i = 1; i < k; i++)
                r = r + a;
            return r;
        }
        public static OpNum operator -(OpNum a) => new OpNum(-a.v, a.log);
        public static bool operator ==(OpNum a, OpNum b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return ReferenceEquals(a, b);
            a.log.s += "e" + a.v;
            return a.v == b.v;
        }
        public static bool operator !=(OpNum a, OpNum b) => !(a == b);
        public static implicit operator int(OpNum n) => n.v;
    }

    public class OpCounter
    {
        public int gets, sets;
        private OpVec _p = new OpVec(1, 1);
        public OpVec P { get { gets++; return _p; } set { sets++; _p = value; } }
    }

    // #105 のパネル
    public class OperatorRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "O1=4:6,-2:-2,-1:-2,1:2,2:4,20:40,7:10,8:12,5:8,1:20:1;O2=1:0,3:6,2:6,4:8,1:2,2:1,-2:-3;O3=TrueFalseFalseFalseTrueTrueTrue,TrueFalseTrueTrue,FalseTrue-1;O4=7:7,102,102304,<1,2>,55,6:6,2:3,2:3,407;O5=6:95:86:9,2:45:63:34:44:61:2,11:128:94:4;O6=4:4LMRiA6:7;O7=95,t;O8=34,-3,103,51,32;O9=13:14,3:6,3:4,2:4102,4:6,3:8,2,1005,0:-5;O10=TrueFalseFalse,4:62:4;O11=33:3eq+ffff;O12=3110ILNFNININNNNNNNNNNN;O13=2147483647:-2147483648,-2147483648:-2147483647,1:-8-2147483648:01:-80:-10:-4,-3:3-1:1,0-1:0;O14=334:5,Falseabcde1,Trueabcde1e2,Trueabcde1e2,TrueTrueFalseTrueTrue;O15=6:6406,4:41:13:37:8,9.5,4:4,9III,4:53:3;O16=12:12";

        public Text display;
        public OperatorPeer peer;

        private OpVec _field = new OpVec(1, 2);
        private OpVec[] _arr = { new OpVec(1, 1), new OpVec(2, 2), new OpVec(3, 3) };
        private string _log = "";
        private int _c;

        private OpVec Log(string tag, int v) { _log += tag; return new OpVec(v, v); }
        private int Idx() { _log += "i"; return _c++; }
        private int Take(OpVec v) => v.x * 10 + v.y;
        private OpVec Give(int n) => n;    // 戻り値の暗黙の変換
        private OpNum Mk(OpLog log, string tag, int v) { log.s += tag; return new OpNum(v, log); }
        private OpVec _evt = new OpVec(0, 0);
        private bool _flag = true;

        public void OpEvent() { _evt = _evt + 5; _evt++; }

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var a = new OpVec(1, 2);
            var b = new OpVec(3, 4);

            // O1：2 項・単項の演算子（左右で違う * 、優先順位、括弧、連鎖）
            s += "O1=" + OpVec.S(a + b) + "," + OpVec.S(a - b) + "," + OpVec.S(-a) + "," + OpVec.S(+a) + "," + OpVec.S(a * 2) + "," + OpVec.S(2 * a) + ","
                 + OpVec.S(a + b * 2) + "," + OpVec.S((a + b) * 2) + "," + OpVec.S(a + b + a) + "," + OpVec.S(b / 2) + OpVec.S(b % 3) + ";";

            // O2：ビット演算・シフト・! ・~
            s += "O2=" + OpVec.S(a & b) + "," + OpVec.S(a | b) + "," + OpVec.S(a ^ b) + "," + OpVec.S(a << 2) + "," + OpVec.S(b >> 1) + "," + OpVec.S(!a) + "," + OpVec.S(~a) + ";";

            // O3：比べる（== は値で比べる自分の演算子、null との比較、List.Contains は Equals なので参照）
            OpVec n = null;
            var a2 = new OpVec(1, 2);
            var list = new List<OpVec> { a, b };
            s += "O3=" + (a == a2) + (a != a2) + (a == b) + (a == null) + (n == null) + (null == n) + (n != a) + "," + (a < b) + (a > b) + (a <= a2) + (b >= a)
                 + "," + list.Contains(a2) + list.Contains(a) + list.IndexOf(a2) + ";";

            // O4：変換（暗黙・明示、引数・戻り値・演算子の引数・初期化、標準の変換と組み合わせる）
            OpVec c4 = 7;
            long l4 = (int)a;
            double d4 = (int)b;
            s += "O4=" + OpVec.S(c4) + "," + (int)a + "," + l4 + d4 + "," + (string)a + "," + Take(5) + "," + OpVec.S(Give(6)) + "," + OpVec.S(a + 1) + "," + OpVec.S(1 + a) + "," + ((int)(a + b) + 1) + ";";

            // O5：複合代入・インクリメント（ローカル・フィールド・配列・List・普通のクラスのフィールド・プロパティ）、値として使う
            var x = new OpVec(1, 1);
            x += a;
            x -= 1;
            x *= 3;
            ++x;
            x++;
            --x;
            var y = ++x;
            var z = (x += 1);
            _field += a;
            _arr[0] += b;
            _arr[1]++;
            ++_arr[2];
            list[0] += b;
            var h = new OpHolder();
            h.pos += a;
            h.Pos += b;
            h.trail[1] *= 2;
            h.Move(10);
            int i5 = 0;
            _arr[i5] += 1;
            s += "O5=" + OpVec.S(x) + OpVec.S(y) + OpVec.S(z) + "," + OpVec.S(_field) + OpVec.S(_arr[0]) + OpVec.S(_arr[1]) + OpVec.S(_arr[2]) + OpVec.S(list[0]) + OpVec.S(a)
                 + "," + OpVec.S(h.pos) + OpVec.S(h.Pos) + OpVec.S(h.trail[1]) + ";";

            // O6：評価の順（左から 1 回ずつ）。添字は 1 回だけ評価する受け側の形にしてから
            _log = "";
            _c = 0;
            var o6 = Log("L", 1) + Log("M", 2) * 3 - Log("R", 3);
            int k6 = Idx();
            _arr[k6] += Log("A", 1);
            s += "O6=" + OpVec.S(o6) + _log + OpVec.S(_arr[0]) + ";";

            // O7：ジェネリッククラス（型引数ごとのコピーの中の演算子・変換）
            OpBox<int> g1 = 5;
            var g2 = g1 + new OpBox<int>(9);
            int gi = g2;
            OpBox<string> gs = "s";
            string gt = gs + "t";
            s += "O7=" + gi + (int)g1 + "," + gt + ";";

            // O8：UdonSharpBehaviour の演算子（別の UdonSharpBehaviour の static メソッド）
            s += "O8=" + (peer + 4) + "," + (-peer) + "," + (int)peer + "," + OperatorPeer.Half(peer) + "," + peer.Plus(2) + ";";

            // O9：ほかの機能と組み合わせる（static クラス・拡張メソッド・インタフェース・クラスの中・ラムダ・LINQ・デリゲート）
            IOpShape shape = new OpSquare();
            Func<OpVec, OpVec, OpVec> add = (p, q) => p + q;
            Func<OpVec, bool> big = v => v > new OpVec(2, 2);
            var vs = new List<OpVec> { new OpVec(1, 0), new OpVec(2, 3), new OpVec(0, 5) };
            s += "O9=" + OpVec.S(OpVecUtil.Sum(_arr)) + "," + OpVec.S(a.Scale(3)) + "," + OpVec.S(shape.Corner(a)) + "," + OpVec.S(a.Twice()) + a.Len + ","
                 + OpVec.S(add(a, b)) + "," + OpVec.S(vs.Aggregate((p, q) => p + q)) + "," + vs.Count(big) + "," + string.Join("", vs.Where(v => v != new OpVec(2, 3)).Select(v => (int)v)) + ","
                 + OpVec.S(vs.Select(v => -v).Last()) + ";";

            // O10：タプル・匿名型の要素（タプルの == は要素の == を使う。Equals は要素の Equals なので参照）
            var t1 = (a, 1);
            var t2 = (a2, 1);
            var an = new { V = a + b, W = 1 };
            s += "O10=" + (t1 == t2) + (t1 != t2) + t1.Equals(t2) + "," + OpVec.S(an.V) + OpVec.S((t1.Item1 + t2.Item1)) + ";";

            // O11：条件（if・三項・while・for）
            var w = new OpVec(0, 0);
            int loops = 0;
            while (w < new OpVec(3, 3))
            {
                w++;
                loops++;
            }
            string cond = a == a2 ? "eq" : "ne";
            if (!(a != a2) && a < b)
                cond += "+";
            for (OpVec f = 0; f < 4; f += 1)
                cond += "f";
            s += "O11=" + loops + OpVec.S(w) + cond + ";";

            // O12：オーバーロードの選ばれ方（int・long・float・char・short と、標準の変換）と、演算子の中から別の演算子を呼ぶもの
            var lg = new OpLog();
            var n1 = new OpNum(1, lg);
            int r12 = (n1 + 2) + (n1 + 2L) + (n1 + 2f) + (n1 + 'a') + (n1 + (short)3) + (n1 + n1) + (n1 - n1) + (n1 * 3) + (n1 * -2) + (n1 * 0);
            s += "O12=" + r12 + lg.s + ";";

            // O13：境界の値（あふれ・最小値の符号反転・シフト 0／31／32／33・負の / と %）
            var mm = new OpVec(int.MinValue, int.MaxValue);
            var sh = new OpVec(1, -8);
            s += "O13=" + OpVec.S(mm + new OpVec(-1, 1)) + "," + OpVec.S(-mm) + "," + OpVec.S(sh << 0) + OpVec.S(sh << 31) + OpVec.S(sh << 32) + OpVec.S(sh >> 31) + OpVec.S(sh >> 33) + ","
                 + OpVec.S(new OpVec(-7, 7) / 2) + OpVec.S(new OpVec(-7, 7) % 3) + "," + (int)new OpVec(0, 0) + OpVec.S(~new OpVec(0, -1)) + ";";

            // O14：評価の回数・順番（プロパティの受け側は get・set 1 回ずつ、タプルの == は全部評価してから左から比べ、違えばそこで止める）
            var cnt = new OpCounter();
            cnt.P += new OpVec(1, 2);
            cnt.P++;
            ++cnt.P;
            var lt = new OpLog();
            bool t14a = (Mk(lt, "a", 1), Mk(lt, "b", 2)) == (Mk(lt, "c", 9), Mk(lt, "d", 2));
            string s14a = lt.s;
            lt.s = "";
            bool t14b = (Mk(lt, "a", 1), Mk(lt, "b", 2)) == (Mk(lt, "c", 1), Mk(lt, "d", 2));
            string s14b = lt.s;
            lt.s = "";
            bool t14c = (Mk(lt, "a", 1), Mk(lt, "b", 2)) != (Mk(lt, "c", 1), Mk(lt, "d", 3));
            var nl = new OpNum(3, lt);
            OpNum nn = null;
            s += "O14=" + cnt.gets + cnt.sets + OpVec.S(cnt.P) + "," + t14a + s14a + "," + t14b + s14b + "," + t14c + lt.s + ","
                 + (nn == null) + (null == nn) + (nl == null) + (nn != nl) + (nn == nn) + ";";

            // O15：ほかの機能と（foreach の要素の変換・三項・初期化子・Dictionary・ジェネリックの 2 つの型・ラムダの中の複合代入・LINQ・拡張メソッド）
            var e15 = new OpVec(0, 0);
            foreach (OpVec v in new int[] { 1, 2, 3 })
                e15 += v;
            int f15 = 0;
            foreach (int k in new List<OpVec> { new OpVec(1, 2), new OpVec(3, 4) })
                f15 += k;
            OpVec q15 = _flag ? 4 : a;
            OpVec[] i15 = { 1, a };
            var l15 = new List<OpVec> { 2 };
            l15.Add(3);
            var d15 = new Dictionary<string, OpVec> { { "k", 5 } };
            d15["k"] += a;
            d15["k"]++;
            OpBox<int> gi15 = 7;
            OpBox<float> gf15 = 1.5f;
            float gsum = (int)gi15 + (float)(gf15 + new OpBox<float>(2.5f));
            var cap = new OpVec(0, 0);
            Action step = () => { cap += 1; cap++; };
            step();
            step();
            var lq = new OpLog();
            var nums = new List<OpNum> { new OpNum(1, lq), new OpNum(2, lq), new OpNum(3, lq) };
            int lsum = nums.Select(m => m + 1).Sum(m => (int)m);
            s += "O15=" + OpVec.S(e15) + f15 + "," + OpVec.S(q15) + OpVec.S(i15[0]) + OpVec.S(l15[1]) + OpVec.S(d15["k"]) + "," + gsum + "," + OpVec.S(cap) + "," + lsum + lq.s + ","
                 + OpVec.S(a.Plus(3)) + OpVec.S(shape.Corner(1)) + ";";

            // O16：カスタムイベントから（SendCustomEvent）
            SendCustomEvent(nameof(OpEvent));
            SendCustomEvent(nameof(OpEvent));
            s += "O16=" + OpVec.S(_evt);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#105 operators\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#105 operators"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #105 operators " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
