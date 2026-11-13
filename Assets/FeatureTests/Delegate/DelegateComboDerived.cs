namespace FeatureTests.Delegate
{
    // #126 A7: 派生。基底と同じデリゲート型（Calc）を、派生でも作って呼ぶ。派生だけが使うデリゲート型（NoArgs）も
    public class DelegateComboDerived : DelegateComboBase
    {
        public override int Virt(int x) => x + 2;

        private int DerivedOwn(int x) => x + 200;

        private int Seven() => 7;

        // A9：呼び出しの中から、同じデリゲート型の別の相手を呼ぶ（入れ子）
        public Calc GetVirt() => Virt;

        public int DerivedMake()
        {
            Calc v = Virt;
            Calc own = DerivedOwn;
            NoArgs n = Seven;
            return v(1) * 10000 + own(1) * 10 + n();
        }
    }
}
