namespace FeatureTests.Inheritance
{
    // override しない派生クラス
    public class InheritArcher : InheritUnitBase
    {
        // #111: 基底の BaseHelpers と同じものを使う
        public int DerivedHelpers()
        {
            IInheritUnit self = this;
            var counter = new InheritCounter();
            counter.Add(hp);
            var list = new System.Collections.Generic.List<int> { 1, 2, 3 };
            return self.Hp() * 2 + counter.total + list.FindIndex(x => x > 2);
        }
    }
}
