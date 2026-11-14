using UdonSharp;

namespace FeatureTests.Delegate
{
    // #129: 別の UdonSharpBehaviour。クロージャを受け取って呼ぶ（C1）・インタフェースの実装（A3）・
    // 自分のクロージャを作ってから、受け取ったクロージャを呼ぶ（A2）
    public class ClosureComboRelay : UdonSharpBehaviour, IClosureTarget
    {
        private Calc _stored;

        public int Take(Calc c, int x) => c(x) + 1000;

        public void Store(Calc c) { _stored = c; }

        public int CallStored(int x) => _stored(x);

        public int CallBack(Calc c, int x)
        {
            int local = 7;
            Calc mine = y => y + local;
            return c(mine(x));
        }
    }
}
