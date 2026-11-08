using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.ClassBasic
{
    public class Shooter : UdonSharpBehaviour
    {
        public Bullet current;          // public でもシリアライズしない
        private Bullet[] _pool;

        private void Start()
        {
            Bullet b = new Bullet(Vector3.zero, 3f);
            b.Move(Vector3.forward);
            b.damage += 5;
            b.pos.x = 1f;
            b.Name = "first";
            b.Power = 40;
            b.Power++;

            int dealt = b.Hit(2);
            Debug.Log($"{b.Name} dealt={dealt} fast={b.IsFast} max={Bullet.MaxDamage}");

            _pool = new Bullet[3];
            _pool[0] = b;
            _pool[1] = b.Split();
            _pool[2] = Bullet.Create(2f);
            foreach (Bullet p in _pool)
                Debug.Log(p.damage);

            object o = _pool[1];
            if (o is Bullet)
                current = o as Bullet;

            Debug.Log(MathUtil.Square(3) + 4.Twice());
            Debug.Log(nameof(Bullet.Move));
        }
    }
}
