namespace FeatureTests.Delegate
{
    // #123: ローカル関数・匿名メソッドと、ほかの機能との組み合わせ

    // 3：インタフェースと、それを実装する普通のクラス（差し替える）
    public interface ILocalShape
    {
        int Area();
    }

    public class LocalSquare : ILocalShape
    {
        public int side;

        public LocalSquare(int side) { this.side = side; }

        public int Area() => side * side;
    }

    public class LocalRect : ILocalShape
    {
        public int w, h;

        public LocalRect(int w, int h) { this.w = w; this.h = h; }

        public int Area() => w * h;
    }

    // 4：普通のクラスの中のローカル関数（再帰・入れ子・兄弟のブロックの同じ名前・this のフィールド）
    public class LocalCalc
    {
        public int bias;

        public LocalCalc(int bias) { this.bias = bias; }

        public int Fib(int n)
        {
            int F(int m) => m < 2 ? m : F(m - 1) + F(m - 2);
            return F(n) + bias;
        }

        public int Nested(int x)
        {
            int Outer(int a)
            {
                int Inner(int b) => b * 2 + bias;
                return Inner(a) + 1;
            }
            return Outer(x);
        }

        public int SameName(int x)
        {
            int r = 0;
            {
                int Step(int a) => a + 1;
                r += Step(x);
            }
            {
                int Step(int a) => a * 10;
                r += Step(x);
            }
            return r;
        }
    }

    // 4：ジェネリッククラスの中のローカル関数（具体型 2 つ）
    public class LocalBox<T>
    {
        public T value;

        public LocalBox(T value) { this.value = value; }

        public int CountTimes(int n)
        {
            int c = 0;
            void Inc() { c++; }
            for (int i = 0; i < n; i++)
                Inc();
            return c;
        }

        public T Get()
        {
            T Pick() => value;
            return Pick();
        }
    }
}
