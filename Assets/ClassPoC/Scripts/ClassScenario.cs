using UnityEngine;

namespace ClassPoC
{
    // #49: 汎用クラスの機能を一通り使って、結果を 1 つの文字列にする。
    // 普通の C#（エディタ）と Udon の両方で実行し、同じ文字列になれば正しく書き換えられている。
    public static class ClassScenario
    {
        public static string Run()
        {
            string r = "";

            Bullet b = new Bullet(Vector3.zero, 3f);
            r += $"a:{b.damage},{b.speed},{b.Name},{b.pos};";

            b.Move(Vector3.forward);
            r += $"b:{b.pos},{b.damage};";

            b.damage += 5;
            b.pos.x = 1f;
            b.Name = "first";
            b.Power = 40;
            b.Power++;
            r += $"c:{b.damage},{b.pos},{b.Name},{b.Power},{b.IsFast};";
            r += $"d:{b.Hit(2)},{Bullet.MaxDamage};";

            Bullet child = b.Split();
            r += $"e:{child.speed},{child.damage},{b.next == child},{child.next == null},{child.IsFast};";

            Bullet[] pool = new Bullet[3];
            pool[0] = b;
            pool[1] = child;
            pool[2] = Bullet.Create(2f);
            int sum = 0;
            foreach (Bullet p in pool)
                sum += p.damage;
            r += $"f:{sum},{pool[2].pos},{pool[2].speed};";

            object o = pool[1];
            object s = "x";
            r += $"g:{o is Bullet},{(o as Bullet) == child},{s is Bullet},{(s as Bullet) == null};";

            r += $"h:{MathUtil.Square(3)},{4.Twice()},{nameof(Bullet.Move)};";

            Node list = new Node(1, new Node(2, new Node(3, null)));
            r += $"i:{list.Sum()};";

            return r;
        }
    }
}
