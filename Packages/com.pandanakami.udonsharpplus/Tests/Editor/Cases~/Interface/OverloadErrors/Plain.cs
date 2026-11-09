using UdonSharp;

namespace UdonSharpPlusTests.OverloadErrors
{
    // インタフェース実装メソッドと同じ名前の、引数なしのメソッド（エラー）
    public class Plain : UdonSharpBehaviour, IHittable
    {
        public void Hit(int damage)
        {
        }

        public void Hit()
        {
        }
    }
}
