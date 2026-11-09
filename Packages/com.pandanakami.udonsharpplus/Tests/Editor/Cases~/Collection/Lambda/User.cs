using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLambda
{
    public class Enemy
    {
        public string name;
        public int hp;
        public Enemy(string n, int h) { name = n; hp = h; }
    }

    public class User : UdonSharpBehaviour
    {
        private List<int> _ints = new List<int> { 5, 3, 8, 1 };
        private List<Enemy> _enemies = new List<Enemy>();
        private int _total;

        private bool IsEven(int v) { return v % 2 == 0; }

        private void Start()
        {
            int limit = 4;
            int found = _ints.Find(x => x > limit);
            int last = _ints.FindLast(x => x < limit);
            int index = _ints.FindIndex(x => x == 8);
            int lastIndex = _ints.FindLastIndex(IsEven);
            int fromIndex = _ints.FindIndex(1, x => x > limit);
            int rangeIndex = _ints.FindIndex(1, 2, x => x > limit);
            int lastRange = _ints.FindLastIndex(2, 2, x => x > limit);
            List<string> texts = _ints.ConvertAll(x => "#" + x);
            List<Enemy> spawned = _ints.ConvertAll(x => new Enemy("e" + x, x));
            bool any = _ints.Exists(x => x > 100);
            bool all = _ints.TrueForAll(x => x > 0);
            List<int> big = _ints.FindAll(x => x >= limit);

            int sum = 0;
            _ints.ForEach(x => sum += x);
            _ints.ForEach(x =>
            {
                int doubled = x * 2;
                _total += doubled;
            });

            int firstOdd = _ints.Find(x =>
            {
                if (x % 2 == 0)
                    return false;
                return x > 1;
            });

            int seen = 0;
            if (_ints.Exists(x => { seen = x; return x == 3; }))
                Debug.Log(seen);

            _ints.Sort((a, b) => b - a);

            _enemies.Add(new Enemy("a", 10));
            _enemies.Add(new Enemy("b", 0));
            Enemy alive = _enemies.Find(e => e.hp > 0);
            int removed = _enemies.RemoveAll(e => e.hp <= 0);

            List<int> other = new List<int> { 3, 8 };
            bool shared = _ints.Exists(x => other.Exists(y => y == x));

            Debug.Log($"{found} {last} {index} {lastIndex} {any} {all} {big.Count} {sum} {_total} {firstOdd} {alive.name} {removed} {shared} {fromIndex} {rangeIndex} {lastRange} {texts[0]} {spawned[0].name}");
        }
    }
}
