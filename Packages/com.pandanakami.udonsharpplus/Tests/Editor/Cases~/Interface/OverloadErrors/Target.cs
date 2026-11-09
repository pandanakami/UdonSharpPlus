using UdonSharp;

namespace UdonSharpPlusTests.OverloadErrors
{
    // 同じ名前で引数の違うメソッドを持つ 2 つのインタフェースを実装（エラー）
    public class Target : UdonSharpBehaviour, IHittable, IHittableWithSource, IResettable
    {
        public void Hit(int damage)
        {
        }

        public void Hit(int damage, string source)
        {
        }
    }
}
