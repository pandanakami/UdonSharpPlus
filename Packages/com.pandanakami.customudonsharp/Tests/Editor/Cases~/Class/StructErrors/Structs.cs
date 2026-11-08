namespace CustomUdonSharpTests.StructErrors
{
    // UdonSharpBehaviour から使う：エラー（1 つの構造体につき 1 つ）
    public struct Point
    {
        public int x, y;
        public Point(int x, int y) { this.x = x; this.y = y; }
        public int Sum() { return x + y; }
    }

    // 汎用クラスの中から使う：エラー
    public struct Size
    {
        public int w, h;
    }

    // 使わない：エラーにしない
    public struct Unused
    {
        public int v;
    }

    public class Box
    {
        public int Area(Size s) { return s.w * s.h; }
    }
}
