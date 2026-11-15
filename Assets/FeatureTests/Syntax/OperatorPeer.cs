using UdonSharp;

namespace FeatureTests.Syntax
{
    // #105：UdonSharpBehaviour に宣言した演算子・変換（以前は、宣言しただけで U# の内部の例外になった）
    public class OperatorPeer : UdonSharpBehaviour
    {
        public int v = 3;

        public static int operator +(OperatorPeer p, int n) => p.v * 10 + n;
        public static int operator -(OperatorPeer p) => -p.v;
        public static explicit operator int(OperatorPeer p) => p.v + 100;

        public static int Half(OperatorPeer p) => (int)p / 2;   // 自分の変換を、クラスの中から使う
        public int Plus(int n) => this + n;                       // 自分の演算子を、インスタンスのメソッドから使う
    }
}
