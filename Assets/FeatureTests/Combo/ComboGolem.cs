using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace FeatureTests.Combo
{
    // #63: UdonSharpBehaviour による IFighter の実装（実装メソッドの中で List・LINQ を使う）
    public class ComboGolem : UdonSharpBehaviour, IFighter
    {
        private List<int> _boosts = new List<int>();

        public string Name() { return "golem"; }
        public int Power() { return 6 + _boosts.Sum(); }
        public void Boost(int by) { _boosts.Add(by); }

        public void ResetUnit() { _boosts.Clear(); }
    }
}
