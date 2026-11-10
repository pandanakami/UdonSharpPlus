namespace UdonSharpPlusTests.Syntax
{
    // #79 n: インタフェースがインタフェースを継承する
    public interface IBase
    {
        int A(int x);
    }

    public interface IDerived : IBase
    {
        int B(int x);
    }

    // 複数のインタフェースを継承し、同じ名前・同じ引数のメソッドがある
    public interface ILeft
    {
        int Same(int x);
    }

    public interface IRight
    {
        int Same(int x);
    }

    public interface IBoth : ILeft, IRight
    {
        int Own();
    }

    // 普通のクラスの実装が 4 個以上（型番号の switch になる）
    public class D1 : IDerived
    {
        public int A(int x) => x + 1;
        public int B(int x) => x * 10 + 1;
    }

    public class D2 : IDerived
    {
        public int A(int x) => x + 2;
        public int B(int x) => x * 10 + 2;
    }

    public class D3 : IDerived
    {
        public int A(int x) => x + 3;
        public int B(int x) => x * 10 + 3;
    }

    public class D4 : IDerived
    {
        public int A(int x) => x + 4;
        public int B(int x) => x * 10 + 4;
    }

    // 基底のインタフェースだけを実装する
    public class OnlyBase : IBase
    {
        public int A(int x) => -x;
    }

    public class Both : IBoth
    {
        public int Same(int x) => x * 7;
        public int Own() => 5;
    }
}
