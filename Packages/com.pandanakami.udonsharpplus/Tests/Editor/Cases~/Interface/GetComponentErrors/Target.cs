using UdonSharp;

namespace UdonSharpPlusTests.GetComponentErrors
{
    public class Target : UdonSharpBehaviour, IHittable
    {
        public void Hit(int damage) { }
    }
}
