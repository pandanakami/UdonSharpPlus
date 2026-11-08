namespace ClassPoC
{
    // #50: 汎用クラスによるインタフェースの実装
    public interface IShape
    {
        int Area();
        string Describe(string prefix);
        void Grow(int by);
    }

    public class Circle : IShape
    {
        public int r;
        public Circle(int r) { this.r = r; }
        public int Area() { return 3 * r * r; }
        public string Describe(string prefix) { return prefix + "circle"; }
        public void Grow(int by) { r += by; }
    }

    public class Square : IShape
    {
        public int side;
        public Square(int side) { this.side = side; }
        public int Area() { return side * side; }
        public string Describe(string prefix) { return prefix + "square"; }
        public void Grow(int by) { side += by; }
    }

    // 汎用クラスの中からインタフェースを使う
    public class ShapeTotal
    {
        public int Sum(IShape[] shapes)
        {
            int sum = 0;
            foreach (IShape s in shapes)
                sum += s.Area();
            return sum;
        }
    }
}
