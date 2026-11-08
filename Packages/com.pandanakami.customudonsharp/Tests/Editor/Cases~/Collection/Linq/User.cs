using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.CollectionLinq
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
        private int[] _array = { 4, 9, 2 };
        private List<Enemy> _enemies = new List<Enemy>();

        private static bool IsOdd(int v) { return v % 2 != 0; }

        private void Start()
        {
            int limit = 2;
            List<int> doubled = _ints.Where(x => x > limit).Select(x => x * 2).ToList();
            int[] arr = _array.Where(IsOdd).ToArray();
            var query = _ints.Where(x => x != 3);
            int queryCount = query.Count();
            foreach (int q in query)
                Debug.Log(q);
            foreach (string s in _ints.Select(x => "#" + x))
                Debug.Log(s);

            bool any = _ints.Any();
            bool anyBig = _ints.Any(x => x > 7);
            bool all = _array.All(x => x > 1);
            int count = _ints.Count(x => x % 2 == 0);
            int first = _ints.First();
            int firstBig = _ints.First(x => x > limit);
            int none = _ints.FirstOrDefault(x => x > 100);
            int last = _array.Last();
            int lastSmall = _ints.LastOrDefault(x => x < 4);
            int sum = _ints.Sum();
            float sumF = _enemies.Sum(e => e.hp * 0.5f);
            int min = _array.Min();
            int max = _ints.Max(x => x * 10);
            double avg = _ints.Average();
            bool has = _array.Contains(9);

            _enemies.Add(new Enemy("a", 10));
            _enemies.Add(new Enemy("b", 0));
            List<string> alive = _enemies.Where(e => e.hp > 0).Select(e => e.name).ToList();
            Enemy strongest = _enemies.First(e => e.hp == _enemies.Max(x => x.hp));

            int seen = 0;
            bool found = _ints.Any(x => { seen++; return x == 3; });

            Debug.Log($"{doubled.Count} {arr.Length} {queryCount} {any} {anyBig} {all} {count} {first} {firstBig} {none} {last} {lastSmall} {sum} {sumF} {min} {max} {avg} {has} {alive[0]} {strongest.name} {found} {seen}");
        }
    }
}
