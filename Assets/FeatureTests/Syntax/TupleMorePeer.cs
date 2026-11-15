using UdonSharp;

namespace FeatureTests.Syntax
{
    // #82：別の UdonSharpBehaviour にタプルを渡す・返してもらう
    public class TupleMorePeer : UdonSharpBehaviour
    {
        public string lastSeen = "";

        public (int, string) Echo((int, string) p)
        {
            lastSeen = p.ToString();
            p = (p.Item1 + 100, p.Item2 + "!");      // 作り直しても、呼び元のタプルは変わらない
            return p;
        }

        public (int, (int, string)) Nest((int, (int, string)) p) => (p.Item1 * 2, (p.Item2.Item1 * 2, p.Item2.Item2 + p.Item2.Item2));

        public (int b, int a) Swap((int a, int b) p)
        {
            lastSeen = "s" + p.a;
            return (p.b, p.a);
        }

        public int FromItem((int w, int h) s) => s.w + s.h;

        public (int, string) Pair(int x) => (x, "p" + x);
    }
}
