namespace UdonSharpPlusTests.GetComponentErrors
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
