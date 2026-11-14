namespace FeatureTests.Delegate
{
    // #129 C2: partial のもう 1 つのファイルでも、同じデリゲート型のクロージャを作る
    public partial class ClosureComboRunner
    {
        private int PartMake(int k)
        {
            int p = k;
            Calc c = x => x - p;
            return c(10);
        }
    }
}
