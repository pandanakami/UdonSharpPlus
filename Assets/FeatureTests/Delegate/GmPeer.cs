using System;
using UdonSharp;

namespace FeatureTests.Delegate
{
    // #134：メソッドを入れられる、別の UdonSharpBehaviour（デリゲートを受け取って、あとで呼ぶ）
    public class GmPeer : UdonSharpBehaviour
    {
        public int m = 6;
        private Func<int, int> _stored;

        public int Mul(int x) => x * m;
        public void Store(Func<int, int> f) { _stored = f; }
        public int CallStored(int x) => _stored(x);
    }
}
