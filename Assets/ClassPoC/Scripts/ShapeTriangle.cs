using UdonSharp;

namespace ClassPoC
{
    // #50: UdonSharpBehaviour による IShape の実装（汎用クラスの実装と混ぜて使う）
    public class ShapeTriangle : UdonSharpBehaviour, IShape
    {
        public int b = 4, h = 3;

        public int Area() { return b * h / 2; }
        public string Describe(string prefix) { return prefix + "triangle"; }
        public void Grow(int by) { b += by; }

        // 別のプログラムから汎用クラスのインスタンスを受け取り、インタフェース経由で呼ぶ
        public int MeasureOther(IShape other) { return other.Area(); }

        public void ResetShape() { b = 4; h = 3; }
    }
}
