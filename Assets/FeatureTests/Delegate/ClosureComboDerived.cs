namespace FeatureTests.Delegate
{
    // #129 C2: 派生。基底と同じデリゲート型（Calc）のクロージャを、派生でも作る
    public class ClosureComboDerived : ClosureComboBase
    {
        public int DerivedMake(int k)
        {
            int n = k + 5;
            Calc d = x => x * n;
            return d(2) * 100 + BaseMake(k);
        }
    }
}
