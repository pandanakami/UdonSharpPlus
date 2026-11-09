using UdonSharp;

namespace UdonSharpPlusTests.ClassInterfaceSwitch
{
    // UdonSharpBehaviour による実装（switch に入らず、最後の SendCustomEvent に進む）
    public class Golem : UdonSharpBehaviour, IUnit
    {
        public int power = 5;
        public int Power() { return power * 5; }
        public void Boost(int by) { power += by; }
    }
}
