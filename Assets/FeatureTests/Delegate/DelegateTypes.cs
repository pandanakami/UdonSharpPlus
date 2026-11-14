namespace FeatureTests.Delegate
{
    // #119: ソースで宣言したデリゲート
    public delegate int Calc(int x);
    public delegate void Notify(DelegateItem item, string text);
    public delegate string Describe(int x);
    public delegate Calc CalcMaker(int k);

    // デリゲート型のフィールドを持ち、メソッドの中で呼ぶ普通のクラス
    public class DelegateItem
    {
        public int v;
        public Calc calc;

        public DelegateItem(int v) { this.v = v; }

        public int Apply(int x) => calc(x) + v;

        // #123：普通のクラスのメソッドの中のローカル関数（外の変数・this のフィールドを使う、static）
        public int SumTo(int n)
        {
            int acc = 0;
            void Add(int x) { acc += x + v; }
            static int Square(int x) => x * x;
            for (int i = 1; i <= n; i++)
                Add(Square(i));
            return acc;
        }
    }

    public static class DelegateMath
    {
        public static int Twice(int x) => x * 2;
    }
}
