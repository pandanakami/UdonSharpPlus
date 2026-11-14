namespace FeatureTests.Delegate
{
    // #124 の追加テスト：派生から、基底の protected のメソッドを入れる
    public class MtTargetDerived : MtTarget
    {
        public Calc ProtFromDerived() => Prot;
        public Calc BaseProt() => base.Prot;
    }
}
