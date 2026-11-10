namespace FeatureTests.Inheritance
{
    // override する派生クラス
    public class InheritKnight : InheritUnitBase
    {
        public override int Hp() { return hp * 2; }
    }
}
