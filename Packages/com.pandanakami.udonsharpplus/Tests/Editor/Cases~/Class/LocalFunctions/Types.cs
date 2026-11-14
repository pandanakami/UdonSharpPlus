namespace UdonSharpPlusTests.LocalFunctions
{
    // #123: 普通のクラスの中のローカル関数（#87 ではエラーにしていた）。同じクラスのメソッドに移して使える
    public class Counter
    {
        public int v;

        public int AddTwice()
        {
            void Add() { v += 1; }
            int Twice(int x) => x * 2;                       // 式形式
            Add();
            return Twice(v);
        }
    }

    // static メソッドの中
    public static class Util
    {
        public static int Sum(int a, int b)
        {
            int Plus() => a + b;
            return Plus();
        }
    }

    // ジェネリッククラスのコピーが 2 つ
    public class Box<T>
    {
        public T value;

        public T Get()
        {
            T Inner() => value;
            return Inner();
        }
    }

    // 使わないクラス
    public class Unused
    {
        public int Run()
        {
            int One() => 1;
            return One();
        }
    }
}
