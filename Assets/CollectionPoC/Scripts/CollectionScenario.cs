using System.Collections.Generic;
using UnityEngine;

namespace CollectionPoC
{
    // #56: List / Dictionary のシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    // （Dictionary の列挙順に頼らない形にしている）
    public enum ItemKind { None, Weapon, Potion }

    public class Item
    {
        public string name;
        public int qty;
        public List<string> tags = new List<string>();

        public Item(string name, int qty) { this.name = name; this.qty = qty; }
    }

    public static class CollectionScenario
    {
        private static string Join(List<int> list)
        {
            string s = "";
            foreach (int x in list)
                s += x + " ";
            return s.TrimEnd();
        }

        public static string Run()
        {
            string r = "";

            // List<int> の基本操作
            var ints = new List<int> { 5, 3, 8 };
            ints.Add(1);
            ints[0] = 10;
            ints[1] += 4;
            ints[2]++;
            ints.Insert(0, 99);
            bool removed = ints.Remove(99);
            r += "a:" + Join(ints) + "," + removed + "," + ints.Contains(9) + "," + ints.IndexOf(9) + "," + ints.Count + ";";
            ints.Sort();
            r += "b:" + Join(ints) + ";";
            ints.Reverse();
            r += "c:" + Join(ints) + ";";

            int[] arr = ints.ToArray();
            var copy = new List<int>(arr);
            copy.AddRange(new[] { 7 });
            copy.RemoveAt(0);
            r += "d:" + arr.Length + "," + Join(copy) + "," + Join(new List<int>(copy)) + ";";

            // Dictionary<string, int>
            var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
            var indexed = new Dictionary<int, string> { [1] = "one", [2] = "two" };
            r += "e0:" + indexed.Count + indexed[2] + ";";
            dict["c"] = 3;
            dict["a"] += 10;
            dict.Remove("b");
            r += "e:" + dict.Count + "," + dict.ContainsKey("b") + "," + dict.ContainsValue(3) + "," + dict["a"] + ";";
            if (dict.TryGetValue("a", out int av))
                r += "f:" + av + ";";
            if (!dict.TryGetValue("zz", out int zv))
                r += "g:" + zv + ";";

            int sum = 0, keyLength = 0, valueSum = 0;
            foreach (var kv in dict)
                sum += kv.Value * kv.Key.Length;
            foreach (string k in dict.Keys)
                keyLength += k.Length;
            foreach (int v in dict.Values)
                valueSum += v;
            r += "h:" + sum + "," + keyLength + "," + valueSum + "," + dict.Keys.Count + ";";

            // 汎用クラスの要素、要素の中の List
            var items = new List<Item>();
            items.Add(new Item("sword", 1));
            items.Add(new Item("herb", 2));
            items[0].qty += 3;
            items[1].tags.Add("heal");
            items[1].tags.Add("green");
            string itemText = "";
            foreach (Item it in items)
                itemText += it.name + it.qty + "/" + it.tags.Count + " ";
            r += "i:" + itemText.TrimEnd() + ";";

            // 入れ子、string、float、Vector3、enum
            var grid = new List<List<int>>();
            grid.Add(new List<int> { 1, 2 });
            grid[0].Add(3);
            var names = new List<string> { "b", "a", "c" };
            names.Sort();
            var floats = new List<float> { 1.5f };
            floats[0] *= 2;
            var vectors = new List<Vector3> { Vector3.up };
            vectors.Add(new Vector3(4, 5, 6));
            var kinds = new List<ItemKind> { ItemKind.Potion };
            kinds.Add(ItemKind.Weapon);
            var byKind = new Dictionary<ItemKind, List<string>>();
            byKind[ItemKind.Weapon] = new List<string> { "sword" };
            byKind[ItemKind.Weapon].Add("axe");
            r += "j:" + grid[0][2] + "," + names[0] + names[2] + "," + floats[0] + "," + vectors[1].y + "," + (int)kinds[0] + (int)kinds[1]
                 + "," + byKind[ItemKind.Weapon].Count + byKind[ItemKind.Weapon][1] + ";";

            return r;
        }
    }
}
