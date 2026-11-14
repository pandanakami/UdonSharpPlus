namespace FeatureTests.Delegate
{
    // #123 7: partial のもう 1 つのファイル。もう一方のファイルと同じ名前のローカル関数を置き、こちらでは匿名メソッドを作る
    public partial class LocalComboRunner
    {
        private int PartRun()
        {
            int Helper(int x) => x + 100;
            Calc c = delegate (int x) { return Helper(x) * 2; };
            return c(1);
        }
    }
}
