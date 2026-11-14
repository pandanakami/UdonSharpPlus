using System;

namespace FeatureTests.Delegate
{
    // #125：override したメソッドと base のメソッドを Func に入れる
    public class FuncPeerDerived : FuncPeer
    {
        public override int Rate(int x) => x * 3 + hp;
        public Func<int, int> BaseRate() => base.Rate;
    }
}
