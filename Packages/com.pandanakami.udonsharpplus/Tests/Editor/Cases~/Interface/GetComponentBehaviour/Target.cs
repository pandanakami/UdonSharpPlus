using UdonSharp;

namespace UdonSharpPlusTests.GetComponentBehaviour
{
    public class Target : UdonSharpBehaviour, IHittable
    {
        public void Hit(int damage) { }
    }
}
