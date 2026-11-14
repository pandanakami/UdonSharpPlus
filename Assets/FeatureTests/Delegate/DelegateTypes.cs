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
    }

    public static class DelegateMath
    {
        public static int Twice(int x) => x * 2;
    }
}
