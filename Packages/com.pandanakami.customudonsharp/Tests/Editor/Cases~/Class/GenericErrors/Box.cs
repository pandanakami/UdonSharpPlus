using System.Collections.Generic;

namespace CustomUdonSharpTests.ClassGenericErrors
{
    public class Node
    {
        public int v;
    }

    public class Box<T>
    {
        public Node node;

        public bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

        // コピーの中のエラーは、元の行と「汎用クラス Box<int> の…」で出る
        public int Value() => node?.v ?? 0;
    }

    public static class Deep
    {
        public static int Nest<T>(T x, int n)
        {
            if (n <= 0)
                return 0;
            return Nest(new List<T> { x }, n - 1);
        }
    }
}
