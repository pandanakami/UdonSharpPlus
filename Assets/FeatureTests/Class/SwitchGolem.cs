using UdonSharp;

namespace FeatureTests.Class
{
    // #65: UdonSharpBehaviour による実装（switch に入らず、SendCustomEvent で呼ばれる）。
    // Measure は、別のプログラムで作った汎用クラスのインスタンスを、このプログラムの振り分けで呼ぶ
    public class SwitchGolem : UdonSharpBehaviour, ISwitchUnit
    {
        public int power = 20;
        public int Power() { return power * 2; }
        public void Boost(int by) { power += by; }

        public void ResetUnit() { power = 20; }

        public int Measure(ISwitchUnit u) { return u.Power(); }
    }
}
