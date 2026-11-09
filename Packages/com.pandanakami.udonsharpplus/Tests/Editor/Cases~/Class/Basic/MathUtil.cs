namespace UdonSharpPlusTests.ClassBasic
{
    // static クラス（static メソッドと拡張メソッド）
    public static class MathUtil
    {
        public static int Square(int x)
        {
            return x * x;
        }

        public static int Twice(this int x) => x * 2;
    }
}
