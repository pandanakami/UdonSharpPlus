namespace FeatureTests.Delegate
{
    // #124: クラスのメソッドをデリゲートに入れる・普通のクラスの中でデリゲートを作る

    public delegate int CmMeasure();

    public interface ICmShape
    {
        int Area();
    }

    public class CmRect : ICmShape
    {
        public int w, h;

        public CmRect(int w, int h) { this.w = w; this.h = h; }

        public int Area() => w * h;
    }

    // 普通のクラスの中で作る：private のメソッド・static・ラムダ（this と外の変数）・匿名メソッド・ローカル関数・+=
    public class CmCounter
    {
        public int count;
        public int step;

        public CmCounter(int step) { this.step = step; }

        private int Add(int x) { count += x * step; return count; }
        public int Peek(int x) => count + x;
        private static int Neg(int x) => -x;

        public Calc MakeAdd() => Add;
        public Calc MakeNeg() => Neg;

        public Calc MakeLambda(int bonus)
        {
            int local = 1;
            return x => { local++; return x + bonus + local + step; };
        }

        public Calc MakeAnon() => delegate (int x) { return x * step; };

        public Calc MakeLocal()
        {
            int Twice(int x) => x * 2 + step;
            return Twice;
        }

        public static Calc MakeStatic(int k) => x => x * k;

        public Calc Chain()
        {
            Calc c = Add;
            c += Peek;
            return c;
        }
    }
}
