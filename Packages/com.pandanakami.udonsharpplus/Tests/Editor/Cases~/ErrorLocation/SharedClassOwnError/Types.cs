namespace UdonSharpPlusTests.ErrorLocationSharedClassOwnError
{
    // #79 m: 本パッケージのエラー（普通のクラスへの ?.）が、2 つの UdonSharpBehaviour から使っても、
    // ジェネリクスのコピーが 2 つあっても、1 つにまとまる
    public class Node
    {
        public Node next;
        public int v;

        public int NextValue() { return next?.v ?? 0; }
    }

    public class Box<T>
    {
        public Node node;

        public int Value() { return node?.v ?? 0; }
    }
}
