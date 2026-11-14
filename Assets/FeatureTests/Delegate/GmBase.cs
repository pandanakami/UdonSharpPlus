using System;
using UdonSharp;

namespace FeatureTests.Delegate
{
    // #134：作る場所：基底の UdonSharpBehaviour（派生が override する）
    public class GmBase : UdonSharpBehaviour
    {
        public int hp = 3;

        public virtual int V(int x) => x + hp;
        private int Priv(int x) => x - hp;
        protected int Prot(int x) => x * 2 - hp;

        public Func<int, int> D1() => V;
        public Func<int, int> D2() => Priv;
        public Func<int, int> D3() => Prot;

        public Func<int, int> D4()
        {
            hp = 3;
            Func<int, int> f = x => x * hp;
            hp = 4;
            return f;
        }
    }
}
