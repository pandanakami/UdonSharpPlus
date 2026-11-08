using UdonSharp;

namespace CustomUdonSharpTests.ClassInterface
{
    // UdonSharpBehaviour による実装（汎用クラスの実装と混ぜて使う）
    public class Triangle : UdonSharpBehaviour, IShape
    {
        public float b = 2f, h = 3f;
        public float Area() { return b * h / 2f; }
        public string Describe(string prefix) { return prefix + "triangle"; }
        public void Grow(float by) { b += by; }
    }
}
