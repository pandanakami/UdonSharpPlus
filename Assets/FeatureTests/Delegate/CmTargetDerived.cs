namespace FeatureTests.Delegate
{
    // #124: 派生。基底の型の変数から入れても override が呼ばれる・base.Kind は基底の実装
    public class CmTargetDerived : CmTarget
    {
        public override int Kind(int x) => x + 2;

        public Calc BaseKind() => base.Kind;
        public Calc OwnKind() => Kind;
    }
}
