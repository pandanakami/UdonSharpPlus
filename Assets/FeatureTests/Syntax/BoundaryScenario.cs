using System.Collections.Generic;
using System.Linq;
using FeatureTests.Syntax.Alpha;

namespace FeatureTests.Syntax
{
    // #79: 境界のシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    // （e〜h は #79 の注記の観点の記号。例外になるもの（null のインタフェース変数の呼び出しなど）は NullProbe で確かめる）
    public class Link
    {
        public Link next;
        public Item item;
        public int v;
    }

    public static class BoundaryScenario
    {
        public static string Run()
        {
            string r = "";
            r += "e1:" + EmptyList() + ";";
            r += "e2:" + EmptyLinq() + ";";
            r += "e3:" + SingleElement() + ";";
            r += "e4:" + Dictionaries() + ";";
            r += "f1:" + NullElements() + ";";
            r += "f2:" + NullFields() + ";";
            r += "g:" + SameKey() + ";";
            r += "h1:" + NestedLists() + ";";
            r += "h2:" + NestedDictionaries() + ";";
            return r;
        }

        private static string B(bool b) => b ? "T" : "F";

        // e: 空の List のメソッド
        private static string EmptyList()
        {
            List<int> empty = new List<int>();
            string s = empty.Count + "," + B(empty.Contains(1)) + "," + empty.IndexOf(1) + "," + B(empty.Remove(1)) + ",";
            s += B(empty.Exists(x => true)) + B(empty.TrueForAll(x => false)) + "," + empty.FindIndex(x => true) + "," + empty.Find(x => true) + ",";
            s += empty.FindAll(x => true).Count + "," + empty.ConvertAll(x => x * 2).Count + "," + empty.RemoveAll(x => true) + ",";
            s += empty.ToArray().Length + "," + empty.GetRange(0, 0).Count + ",";
            empty.Sort();
            empty.Reverse();
            int sum = 0;
            empty.ForEach(x => sum += x);
            foreach (int x in empty)
                sum += x;
            return s + sum + "," + empty.Count;
        }

        // e: 空の List の LINQ（例外にならないもの）
        private static string EmptyLinq()
        {
            List<int> empty = new List<int>();
            string s = empty.Where(x => true).Count() + "," + B(empty.Any()) + B(empty.Any(x => true)) + B(empty.All(x => false)) + ",";
            s += empty.Sum() + "," + empty.FirstOrDefault() + "," + empty.LastOrDefault() + "," + B(empty.Contains(1)) + ",";
            s += empty.OrderBy(x => x).ToList().Count + "," + empty.Distinct().Count() + "," + empty.Skip(1).Count() + "," + empty.Take(2).Count() + ",";
            s += empty.ToDictionary(x => x).Count + "," + empty.Select(x => x * 2).ToArray().Length + ",";
            List<string> names = new List<string>();
            return s + (names.FirstOrDefault() == null ? "null" : "x") + "," + names.Select(n => n + "!").Count();
        }

        // e: 要素 1 つ
        private static string SingleElement()
        {
            List<int> one = new List<int> { 5 };
            string s = one.First() + "," + one.Last() + "," + one.Min() + "," + one.Max() + "," + one.Average() + "," + one.Sum() + ",";
            s += one.Skip(1).Count() + "," + one.Take(5).Count() + "," + one.OrderByDescending(x => x).First() + "," + one.Distinct().Count() + ",";
            s += one.IndexOf(5) + "," + one.FindLastIndex(x => x == 5) + "," + one.GetRange(0, 1)[0] + ",";
            one.Sort((a, b) => b - a);
            one.Insert(0, 4);
            one.RemoveAt(1);
            s += one[0] + "," + one.Count + ",";
            s += B(one.Remove(4)) + "," + one.Count;
            return s;
        }

        // e: 空・要素 1 つの Dictionary
        private static string Dictionaries()
        {
            Dictionary<string, int> empty = new Dictionary<string, int>();
            int got;
            bool found = empty.TryGetValue("x", out got);
            string s = empty.Count + "," + B(empty.ContainsKey("x")) + B(empty.ContainsValue(0)) + B(empty.Remove("x")) + "," + B(found) + got + ",";
            s += empty.Keys.Count() + "," + empty.Values.Sum() + "," + empty.Count(kv => true) + ",";
            int loops = 0;
            foreach (KeyValuePair<string, int> kv in empty)
                loops++;
            s += loops + ",";

            Dictionary<string, int> one = new Dictionary<string, int> { { "a", 3 } };
            KeyValuePair<string, int> first = one.First();
            s += first.Key + first.Value + "," + one.Values.Sum() + "," + one.Keys.First() + "," + B(one.Remove("a")) + one.Count;
            return s;
        }

        // f: null の要素・値
        private static string NullElements()
        {
            List<string> names = new List<string> { "a", null, "c" };
            string s = names.Count + "," + B(names[1] == null) + B(names.Contains(null)) + "," + names.IndexOf(null) + "," + names.FindIndex(n => n == null) + ",";
            s += names.Where(n => n != null).Count() + "," + names.Count(n => n == null) + ",";
            s += B(names.Remove(null)) + names.Count + ",";

            List<Item> items = new List<Item> { new Item(1), null, new Item(3) };
            s += B(items[1] == null) + "," + items.Count(i => i == null) + "," + items.FindIndex(i => i == null) + "," + items.Where(i => i != null).Sum(i => i.v) + ",";

            List<IScorer> scorers = new List<IScorer> { null, new Item(2) };
            s += B(scorers[0] == null) + "," + scorers.Where(x => x != null).Sum(x => x.Score(1)) + ",";

            Dictionary<string, string> texts = new Dictionary<string, string> { { "k", null } };
            string t;
            bool ok = texts.TryGetValue("k", out t);
            s += B(texts["k"] == null) + B(texts.ContainsKey("k")) + B(texts.ContainsValue(null)) + B(ok) + B(t == null) + ",";

            Dictionary<string, Item> owners = new Dictionary<string, Item> { { "a", null } };
            owners["b"] = new Item(9);
            s += B(owners["a"] == null) + "," + owners.Values.Count(i => i == null) + "," + owners["b"].v;
            return s;
        }

        // f: 普通のクラスの null のフィールド（読むだけ。null のメンバーへのアクセスは NullProbe）
        private static string NullFields()
        {
            Link a = new Link();
            string s = B(a.next == null) + B(a.item == null) + ",";
            a.next = new Link();
            a.next.v = 4;
            a.next.item = new Item(6);
            s += a.next.v + "," + a.next.item.v + "," + B(a.next.next == null) + ",";
            a.next = null;
            s += B(a.next == null) + ",";
            Link[] links = new Link[2];
            s += B(links[0] == null) + ",";
            links[1] = new Link();
            s += links.Count(l => l != null);
            return s;
        }

        // g: 同じキー
        private static string SameKey()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            d["a"] = 1;
            d["a"] = 2;
            string s = d.Count + "," + d["a"] + ",";
            d["a"] += 3;
            d["a"]++;
            s += d["a"] + ",";
            d.Remove("a");
            d.Add("a", 9);
            s += d.Count + "," + d["a"] + ",";
            List<string> words = new List<string> { "x", "y", "x" };
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (string w in words)
            {
                if (counts.ContainsKey(w))
                    counts[w]++;
                else
                    counts[w] = 1;
            }
            s += counts.Count + "," + counts["x"] + counts["y"] + ",";
            s += words.Distinct().Count() + "," + words.Distinct().ToDictionary(w => w, w => w.Length).Count;
            return s;
        }

        // h: 入れ子の List
        private static string NestedLists()
        {
            List<List<int>> nested = new List<List<int>> { new List<int> { 1, 2 }, new List<int>() };
            nested[0].Add(3);
            nested[1].Add(4);
            List<int> head = nested[0];
            head[1] += 10;                  // 入れ子の要素の複合代入は、いったん変数に入れる（nested[0][1] += 10 はエラー）
            nested[1][0] = nested[1][0] * 2; // 入れ子の要素への普通の代入はそのまま書ける
            nested.Add(new List<int> { 7 });
            string sums = "";
            foreach (int sum in nested.Select(l => l.Sum()))
                sums += (sums.Length > 0 ? " " : "") + sum;
            string s = nested.Count + "," + nested.Sum(l => l.Count) + "," + sums + ",";
            s += nested[0][1] + "," + nested.Where(l => l.Count > 1).Count() + ",";
            int total = 0;
            foreach (List<int> l in nested)
                foreach (int x in l)
                    total += x;
            s += total + ",";

            List<List<List<int>>> deep = new List<List<List<int>>> { new List<List<int>> { new List<int>() } };
            deep[0][0].Add(7);
            deep[0][0][0] = deep[0][0][0] * 2;
            deep[0].Add(new List<int> { 1, 1 });
            s += deep[0][0][0] + "," + deep[0].Count + "," + deep[0].Sum(l => l.Count) + ",";

            List<Dictionary<string, int>> ld = new List<Dictionary<string, int>> { new Dictionary<string, int>() };
            ld[0]["a"] = 5;
            Dictionary<string, int> firstMap = ld[0];
            firstMap["a"]++;
            s += ld[0]["a"] + "," + ld.Count(dd => dd.ContainsKey("a"));
            return s;
        }

        // h: 入れ子の Dictionary
        private static string NestedDictionaries()
        {
            Dictionary<string, List<int>> m = new Dictionary<string, List<int>>();
            m["x"] = new List<int>();
            m["x"].Add(1);
            m["x"].Add(2);
            List<int> xs = m["x"];
            xs[0] += 5;
            m["y"] = new List<int> { 9 };
            string s = m.Count + "," + m["x"][0] + "," + m["x"].Count + "," + m.Values.Sum(l => l.Sum()) + ",";

            Dictionary<string, Dictionary<string, int>> dd = new Dictionary<string, Dictionary<string, int>>();
            dd["a"] = new Dictionary<string, int>();
            dd["a"]["b"] = 1;
            dd["a"]["b"] = dd["a"]["b"] + 2;
            dd["a"]["c"] = 4;
            s += dd["a"]["b"] + "," + dd["a"].Count + "," + dd["a"].Values.Sum() + ",";

            Dictionary<int, Dictionary<int, List<string>>> three = new Dictionary<int, Dictionary<int, List<string>>>();
            three[1] = new Dictionary<int, List<string>>();
            three[1][2] = new List<string>();
            three[1][2].Add("z");
            three[1][2][0] = three[1][2][0] + "!";
            s += three[1][2][0] + "," + three[1][2].Count;
            return s;
        }
    }
}
