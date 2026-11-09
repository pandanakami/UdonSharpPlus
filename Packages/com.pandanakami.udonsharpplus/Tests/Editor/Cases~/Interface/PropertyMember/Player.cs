using UdonSharp;

namespace UdonSharpPlusTests.PropertyMember
{
    public class Player : UdonSharpBehaviour, IHealth
    {
        private int _hp;

        public int Hp => _hp;

        public void Heal(int amount)
        {
            _hp += amount;
        }
    }
}
