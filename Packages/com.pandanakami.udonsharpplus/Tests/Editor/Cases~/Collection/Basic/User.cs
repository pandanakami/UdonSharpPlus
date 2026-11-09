using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionBasic
{
    public class Bullet
    {
        public int damage;
        public List<int> hits = new List<int>();
        public Bullet(int d) { damage = d; }
        public int Total() { int s = 0; foreach (int h in hits) s += h; return s; }
    }

    public class User : UdonSharpBehaviour
    {
        private List<int> _ints = new List<int> { 1, 2, 3 };
        private Dictionary<string, int> _scores = new Dictionary<string, int> { { "a", 1 }, ["b"] = 2 };
        private List<Bullet> _bullets = new List<Bullet>();
        private List<List<string>> _nested = new List<List<string>>();

        private int Sum(List<int> xs)
        {
            int s = 0;
            foreach (var x in xs)
                s += x;
            return s;
        }

        private void Start()
        {
            _ints.Add(4);
            _ints[0] = 10;
            _ints[1] += 5;
            _ints[2]++;
            _ints.Insert(0, 99);
            _ints.Remove(99);
            bool has = _ints.Contains(4);
            int idx = _ints.IndexOf(4);
            _ints.Sort();
            _ints.Reverse();
            int[] arr = _ints.ToArray();
            var copy = new List<int>(arr);
            copy.AddRange(new[] { 7, 8 });
            Debug.Log($"{Sum(_ints)} {has} {idx} {arr.Length} {copy.Count}");

            _scores["c"] = 3;
            _scores["a"] += 10;
            if (_scores.TryGetValue("a", out int a))
                Debug.Log(a);
            if (!_scores.ContainsKey("z"))
                Debug.Log("no z");
            foreach (var kv in _scores)
                Debug.Log(kv.Key + "=" + kv.Value);
            foreach (string k in _scores.Keys)
                Debug.Log(k);
            Debug.Log(_scores.Values.Count);

            _bullets.Add(new Bullet(3));
            _bullets[0].hits.Add(5);
            Debug.Log(_bullets[0].damage + _bullets[0].Total());

            _nested.Add(new List<string> { "x" });
            _nested[0].Add("y");
            Debug.Log(_nested[0][1]);
        }
    }
}
