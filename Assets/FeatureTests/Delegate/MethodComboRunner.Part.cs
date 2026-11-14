namespace FeatureTests.Delegate
{
    // #124 の追加テスト（組み合わせ 11）：partial のもう 1 つのファイル。別のファイルの同じ名前のメソッド（MtBox.Get・MtOther.Get）
    public partial class MethodComboRunner
    {
        private int PartUse()
        {
            var o = new MtOther(3);
            Calc a = o.Get;
            Calc b = new MtBox(4).Get;
            return a(1) * 100 + b(1);
        }
    }
}
