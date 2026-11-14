using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using IntFn = System.Func<int, int>;

namespace FeatureTests.Delegate
{
    // #125：.NET のデリゲートを受け取る、別の UdonSharpBehaviour（using の別名で書いても同じ型）
    public class FuncPeer : UdonSharpBehaviour
    {
        public int hp = 100;
        private IntFn _stored;

        public void Store(IntFn f) { _stored = f; }
        public int CallStored(int x) => _stored(x);
        public int Hit(int d) { hp -= d; return hp; }

        public int Pub(int x) => hp + x;
        private int Priv(int x) => hp * x;
        public IntFn MakePriv() => Priv;
        public virtual int Rate(int x) => x * 2;

        // 受け取った引数を LINQ に渡す
        public int ApplyAll(List<int> list, IntFn f) => list.Select(f).Sum();
    }
}
