using UdonSharp;

namespace FeatureTests.Delegate
{
    // #126 A7: 基底と派生の両方で、同じデリゲート型を使う
    public abstract class DelegateComboBase : UdonSharpBehaviour
    {
        protected Calc baseCalc;

        public virtual int Virt(int x) => x + 1;

        private int BaseOwn(int x) => x + 100;

        // 基底のメソッドの中で作る。派生のインスタンスでは、Virt は override が呼ばれる
        public int BaseMake()
        {
            baseCalc = Virt;
            Calc own = BaseOwn;
            return baseCalc(3) * 1000 + own(1);
        }
    }
}
