using System;

namespace FeatureTests.Delegate
{
    // #134：作る場所：派生の UdonSharpBehaviour（override と base のメソッド・基底の protected のメソッド）
    public class GmDerived : GmBase
    {
        public override int V(int x) => x * 100 + hp;

        public Func<int, int> D5() => base.V;
        public Func<int, int> D6() => Prot;
        public Func<int, int> D7() { int k = 9; return x => V(x) + k; }
    }
}
