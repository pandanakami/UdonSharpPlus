using UdonSharp;

namespace FeatureTests.Delegate
{
    // #129 C2: 基底と派生の両方で、同じデリゲート型のクロージャを作る
    public abstract class ClosureComboBase : UdonSharpBehaviour
    {
        protected Calc made;

        public int BaseMake(int k)
        {
            int m = k * 2;
            made = x => x + m;
            return made(1);
        }
    }
}
