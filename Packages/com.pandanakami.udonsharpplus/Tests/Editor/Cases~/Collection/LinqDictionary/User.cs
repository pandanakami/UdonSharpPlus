using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLinqDictionary
{
    public class Item
    {
        public string name;
        public int price;
        public Item(string n, int p) { name = n; price = p; }

        // 汎用クラスのコピーの中の KeyValuePair（完全修飾の名前になる）
        public static string Show(KeyValuePair<int, Item> kv) { return kv.Key + kv.Value.name; }
    }

    public class User : UdonSharpBehaviour
    {
        private Dictionary<string, int> _scores = new Dictionary<string, int> { { "ann", 70 }, { "bob", 40 } };
        private Dictionary<int, Item> _items = new Dictionary<int, Item>();

        private static bool IsHigh(KeyValuePair<string, int> kv) { return kv.Value >= 60; }

        private void Start()
        {
            List<string> passed = _scores.Where(kv => kv.Value >= 60).Select(kv => kv.Key).ToList();
            int count = _scores.Count(kv => kv.Value >= 60);
            string top = _scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).First();
            var passedScores = _scores.Where(kv => kv.Value >= 60).ToDictionary(kv => kv.Key, kv => kv.Value);
            int total = _scores.Values.Sum();
            List<string> names = _scores.Keys.Where(k => k.Length == 3).ToList();

            // KeyValuePair を外に出す（object[] になる）
            var best = _scores.OrderByDescending(kv => kv.Value).First();
            List<KeyValuePair<string, int>> high = _scores.Where(kv => kv.Value >= 60).ToList();
            int viaMethod = _scores.Count(IsHigh);
            int viaBlock = _scores.Count(kv => { int v = kv.Value; return v > 50; });
            var pair = new KeyValuePair<string, int>("cid", 10);

            _items.Add(1, new Item("sword", 300));
            Item cheapest = _items.OrderBy(kv => kv.Value.price).Select(kv => kv.Value).First();
            string shown = Item.Show(_items.First());

            // foreach の kv を、.Key / .Value 以外で使う
            foreach (var kv in _scores)
            {
                KeyValuePair<string, int> copy = kv;
                bool beats = _scores.Any(other => other.Value < kv.Value);
                Debug.Log(copy.Key + IsHigh(kv) + beats);
            }

            Debug.Log($"{passed.Count} {count} {top} {passedScores.Count} {total} {names.Count} {best.Key}{best.Value} {high[0].Key} {viaMethod} {viaBlock} {pair.Value} {cheapest.name} {shown}");
        }
    }
}
