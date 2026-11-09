using UnityEngine;

namespace FeatureTests.Class
{
    // #49: 汎用クラス（UdonSharpBehaviour ではない普通のクラス）
    public class Bullet
    {
        public const int MaxDamage = 99;

        public Vector3 pos;
        public int damage = 10;
        public float speed;
        public string Name { get; set; } = "bullet";
        public Bullet next;

        public Bullet(Vector3 p) : this(p, 1f)
        {
        }

        public Bullet(Vector3 p, float s)
        {
            pos = p;
            speed = s;
        }

        public bool IsFast => speed > 5f;

        public int Power
        {
            get { return damage * 2; }
            set { damage = value / 2; }
        }

        public void Move(Vector3 d)
        {
            pos += d * speed;
            pos.y = 0f;
            damage++;
            this.damage -= 1;
        }

        public int Hit(int armor)
        {
            int dealt = Mathf.Max(0, damage - armor);
            return Mathf.Min(dealt, MaxDamage);
        }

        public Bullet Split()
        {
            Bullet child = new Bullet(pos, speed * 2f);
            child.damage = damage / 2;
            next = child;
            return child;
        }

        public static Bullet Create(float x)
        {
            return new Bullet(new Vector3(x, 0f, 0f));
        }
    }
}
