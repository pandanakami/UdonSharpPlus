namespace FeatureTests.Delegate
{
    // #123 3: 派生。ローカル関数から、インタフェースのメソッド・override・base.Value を呼ぶ。
    // 実装を差し替えたあと・別の UdonSharpBehaviour に渡したあとも、正しい実装が呼ばれること
    public class LocalComboDerived : LocalComboBase
    {
        public LocalComboKeeper keeper;

        public override int Value(int x) => x + 2;

        public string Run(ILocalShape shape)
        {
            int Area() => shape.Area();
            int Virt(int x) => Value(x);
            int Base(int x) => base.Value(x);
            string s = Area() + "," + Virt(1) + "," + Base(1) + ",";
            Calc v = Virt;
            Calc b = Base;
            Calc a = x => Area() + x;
            keeper.Store(b);
            s += v(10) + "," + keeper.CallStored(10) + ",";
            shape = new LocalRect(2, 5);
            s += Area() + "," + a(1);
            return s;
        }
    }
}
