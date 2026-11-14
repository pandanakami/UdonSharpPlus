using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Delegate
{
    // #134：作り方 × 作る場所 × 使い方 の総当たり。ここは、作る場所のうち「普通のクラス」と、使う側（普通のクラス）

    public class GmItem
    {
        public int k;
        public GmItem(int k) { this.k = k; }
        public int Mul(int x) => x * k + 1;
    }

    // 使い方（普通のクラスの中）：呼ぶ・Invoke・+= と -=・LINQ・配列・別の UdonSharpBehaviour に渡す
    public class GmPass
    {
        public int Call(Func<int, int> f, int x) => f(x);
        public Func<int, int> Id(Func<int, int> f) => f;

        public string Use(Func<int, int> f, GmPeer peer)
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
            return s;
        }
    }

    // 作る場所：普通のクラスのインスタンスのメソッドの中
    public class GmPlain
    {
        public int bias = 7;

        public static int SM(int x) => x + 31;
        private int Inst(int x) => x + bias;

        public Func<int, int> B1() => SM;
        public Func<int, int> B2() => Inst;
        public Func<int, int> B3(GmItem it) => it.Mul;
        public Func<int, int> B4(GmPeer p) => p.Mul;
        public Func<int, int> B5() => x => x + 41;

        public Func<int, int> B6()
        {
            int local = 3;
            Func<int, int> f = x => x * local;
            local = 5;
            return f;
        }

        public Func<int, int> B7()
        {
            bias = 7;
            Func<int, int> f = x => x + bias * 2;
            bias = 8;
            return f;
        }

        public Func<int, int> B8(int p) => delegate (int x) { return x - p; };

        public Func<int, int> B9()
        {
            int Loc(int x) => x * 3 + 1;
            return Loc;
        }

        public Func<int, int> B10()
        {
            int acc = 2;
            int Loc(int x) { acc++; return x + acc + bias; }
            acc = 10;
            return Loc;
        }
    }

    // 作る場所：普通のクラスの static メソッドの中
    public static class GmStatics
    {
        public static int SM(int x) => x + 51;

        public static Func<int, int> C1() => SM;
        public static Func<int, int> C2() => x => x + 61;

        public static Func<int, int> C3(int p)
        {
            int local = p;
            Func<int, int> f = x => x * local;
            local = p + 1;
            return f;
        }

        public static Func<int, int> C4(int p) => delegate (int x) { return x + p; };

        public static Func<int, int> C5()
        {
            static int Loc(int x) => x * 5 + 2;
            return Loc;
        }

        public static Func<int, int> C6(int p)
        {
            int acc = p;
            int Loc(int x) { acc += 2; return x + acc; }
            return Loc;
        }

        public static Func<int, int> C7(GmItem it) => it.Mul;
        public static Func<int, int> C8(GmPeer p) => p.Mul;
    }
}
