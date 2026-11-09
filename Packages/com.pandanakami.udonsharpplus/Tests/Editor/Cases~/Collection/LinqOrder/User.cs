using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLinqOrder
{
    public enum Rank { Low, Mid, High }

    public class Enemy
    {
        public string name;
        public int hp;
        public Rank rank;
        public Enemy(string n, int h, Rank r) { name = n; hp = h; rank = r; }

        // 汎用クラスのコピー（static）の中：テンポラリは毎回作る
        public static List<string> Names(List<Enemy> all) { return all.OrderBy(e => e.name).Select(e => e.name).ToList(); }
    }

    public class User : UdonSharpBehaviour
    {
        private List<int> _ints = new List<int> { 5, 3, 8, 1, 3 };
        private string[] _words = { "pear", "apple", "fig" };
        private List<Enemy> _enemies = new List<Enemy>();

        private void Start()
        {
            List<int> asc = _ints.OrderBy(x => x).ToList();
            int[] desc = _ints.OrderByDescending(x => x).ToArray();
            var sorted = _words.OrderBy(w => w.Length).ThenByDescending(w => w);
            foreach (string w in sorted)
                Debug.Log(w);

            _enemies.Add(new Enemy("a", 10, Rank.High));
            _enemies.Add(new Enemy("b", 5, Rank.Low));
            Enemy weakest = _enemies.OrderBy(e => e.hp).First();
            List<string> byRank = _enemies.OrderByDescending(e => e.rank).ThenBy(e => e.name).Select(e => e.name).ToList();

            List<int> unique = _ints.Distinct().ToList();
            List<int> page = _ints.Skip(1).Take(2).ToList();
            int top2 = _ints.OrderByDescending(x => x).Take(2).Sum();
            var byName = _enemies.ToDictionary(e => e.name);
            var hpByName = _enemies.ToDictionary(e => e.name, e => e.hp);
            List<string> names = Enemy.Names(_enemies);

            Debug.Log($"{asc[0]} {desc[0]} {weakest.name} {byRank[0]} {unique.Count} {page[0]} {top2} {byName["a"].hp} {hpByName["b"]} {names[0]}");
        }
    }
}
