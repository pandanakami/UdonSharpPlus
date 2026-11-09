using UdonSharp;

namespace FeatureTests.Class
{
    // #49: 再帰するメソッドを持つ汎用クラス（連結リスト）
    public class Node
    {
        public int value;
        public Node next;

        public Node(int value, Node next)
        {
            this.value = value;
            this.next = next;
        }

        [RecursiveMethod]
        public int Sum()
        {
            int rest = next == null ? 0 : next.Sum();
            return value + rest;
        }
    }
}
