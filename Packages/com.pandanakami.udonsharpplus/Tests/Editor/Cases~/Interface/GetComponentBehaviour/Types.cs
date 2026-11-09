namespace UdonSharpPlusTests.GetComponentBehaviour
{
    public interface IHittable
    {
        void Hit(int damage);
    }

    public class Bullet
    {
        public int damage;
    }
}
