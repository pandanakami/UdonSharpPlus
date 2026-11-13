using UdonSharp;

namespace FeatureTests.Delegate
{
    // #119: 別の UdonSharpBehaviour。受け取ったデリゲートを呼ぶ側と、自分のメソッドからデリゲートを作って渡す側の両方を確かめる
    public class DelegateHolder : UdonSharpBehaviour
    {
        public int bonus = 10;
        private Calc _stored;

        public void Store(Calc c) { _stored = c; }

        public int CallStored(int x) => _stored(x);

        public Calc MakeAdder() => AddBonus;

        private int AddBonus(int x) => x + bonus;
    }
}
