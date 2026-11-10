namespace UdonSharpPlusTests.LocalFunctionErrors
{
    // #87: 普通のクラスの中のローカル関数はエラー（1 つにつき 1 つ）
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

    // ジェネリッククラスのコピーが 2 つあっても、同じ位置のエラーは 1 つ
    public class Box<T>
    {
        public T value;

        public T Get()
        {
            T Inner() => value;
            return Inner();
        }
    }

    // 使わない：エラーにしない
    public class Unused
    {
        public int Run()
        {
            int One() => 1;
            return One();
        }
    }
}
