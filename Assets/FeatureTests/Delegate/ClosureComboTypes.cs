namespace FeatureTests.Delegate
{
    // #129: クロージャ（外の変数を使うラムダ）と、ほかの機能との組み合わせ

    public enum ClosureKind { None, Fire, Ice }

    public delegate string ShowText();
    public delegate void Tick();

    // A3・C1：デリゲートを受け取るメソッドを持つインタフェース
    public interface IClosureTarget
    {
        int Take(Calc c, int x);
    }

    // A3：インタフェースを実装する普通のクラス
    public class ClosureTaker : IClosureTarget
    {
        public int Take(Calc c, int x) => c(x) * 2;
    }

    // C1：デリゲート型のフィールドを持ち、メソッドの中で呼ぶ普通のクラス
    public class ClosureBag
    {
        public Calc calc;

        public int Use(int x) => calc(x);
    }

    // A4：ジェネリッククラス
    public class ClosureBox<T>
    {
        public T value;

        public ClosureBox(T value) { this.value = value; }
    }
}
