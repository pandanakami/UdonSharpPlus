using UdonSharp;

namespace CustomUdonSharpTests.PropertyMember
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
