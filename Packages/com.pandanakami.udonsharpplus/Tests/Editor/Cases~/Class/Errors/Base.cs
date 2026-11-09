namespace UdonSharpPlusTests.ClassErrors
{
    public class Base
    {
        public int a;
    }

    // 継承は未対応（エラー）
    public class Derived : Base
    {
        public int b;
    }

    public class Counter
    {
        public static int total;   // static フィールドは非対応（エラー）
        public int value;
    }
}
