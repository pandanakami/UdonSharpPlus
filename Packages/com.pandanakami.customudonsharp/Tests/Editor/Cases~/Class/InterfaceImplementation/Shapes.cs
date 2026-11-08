namespace CustomUdonSharpTests.ClassInterface
{
    public interface IShape
    {
        float Area();
        string Describe(string prefix);
        void Grow(float by);
    }

    // 汎用クラスによるインタフェースの実装（#50）
    public class Circle : IShape
    {
        public float r;
        public Circle(float r) { this.r = r; }
        public float Area() { return 3f * r * r; }
        public string Describe(string prefix) { return prefix + "circle"; }
        public void Grow(float by) { r += by; }
    }

    public class Square : IShape
    {
        public float side;
        public Square(float side) { this.side = side; }
        public float Area() { return side * side; }
        public string Describe(string prefix) { return prefix + "square"; }
        public void Grow(float by) { side += by; }
    }

    // 汎用クラスの中からインタフェースを使う
    public class Total
    {
        public float Sum(IShape[] shapes)
        {
            float sum = 0f;
            foreach (IShape s in shapes)
                sum += s.Area();
            return sum;
        }
    }
}
