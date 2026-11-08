using System.Collections.Generic;
using System.Linq;

namespace CollectionPoC
{
    // #61: Dictionary への LINQ と、KeyValuePair（object[] になる）のシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    // （Dictionary の列挙順に頼る結果は、並べ替えるか、順番によらない集計にしている）
    public class Shop
    {
        public string name;
        public Dictionary<string, int> prices = new Dictionary<string, int>();

        public Shop(string name) { this.name = name; }

        // 汎用クラスのメソッドの中の Dictionary への LINQ
        public string Cheapest() { return prices.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).First(); }
    }

    public static class DictionaryLinqScenario
    {
        private static bool IsPassed(KeyValuePair<string, int> kv) { return kv.Value >= 60; }

        private static string Describe(KeyValuePair<string, int> kv) { return kv.Key + "=" + kv.Value; }

        private static string Join(List<string> list)
        {
            string s = "";
            foreach (string x in list)
                s += x + " ";
            return s.TrimEnd();
        }

        public static string Run()
        {
            string r = "";
            var scores = new Dictionary<string, int> { { "ann", 70 }, { "bob", 40 }, { "cid", 90 }, { "dan", 60 } };

            // kv.Key / kv.Value だけを使う（キーとして流す）
            List<string> passed = scores.Where(kv => kv.Value >= 60).OrderBy(kv => kv.Key).Select(kv => kv.Key).ToList();
            int passCount = scores.Count(kv => kv.Value >= 60);
            bool anyZero = scores.Any(kv => kv.Value == 0);
            bool allPositive = scores.All(kv => kv.Value > 0);
            string top = scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).First();
            int sumPassed = scores.Where(kv => kv.Value >= 60).Sum(kv => kv.Value);
            r += "a:" + Join(passed) + "," + passCount + "," + anyZero + "," + allPositive + "," + top + "," + sumPassed + ";";

            // Dictionary から Dictionary を作る・Keys / Values
            Dictionary<string, int> bonus = scores.Where(kv => kv.Value < 80).ToDictionary(kv => kv.Key, kv => kv.Value + 10);
            int total = scores.Values.Sum();
            int maxScore = scores.Values.Max();
            List<string> longNames = scores.Keys.Where(k => k != "bob").OrderBy(k => k).ToList();
            r += "b:" + bonus.Count + "," + bonus["bob"] + "," + bonus.ContainsKey("cid") + "," + total + "," + maxScore + "," + Join(longNames) + ";";

            // KeyValuePair を外に出す（object[] になる）
            var best = scores.OrderByDescending(kv => kv.Value).First();
            List<KeyValuePair<string, int>> high = scores.Where(kv => kv.Value >= 70).OrderBy(kv => kv.Key).ToList();
            KeyValuePair<string, int> worst = scores.OrderBy(kv => kv.Value).Last();
            var made = new KeyValuePair<string, int>("eve", 55);
            r += "c:" + best.Key + best.Value + "," + high.Count + high[0].Key + high[1].Value + "," + worst.Key + "," + Describe(made) + ";";

            // メソッド名・ブロックの本体・kv をメソッドに渡す
            int viaMethod = scores.Count(IsPassed);
            int viaBlock = scores.Count(kv => { int v = kv.Value; return v % 20 == 0; });
            List<string> described = scores.Where(kv => kv.Value > 60).OrderBy(kv => kv.Key).Select(kv => Describe(kv)).ToList();
            r += "d:" + viaMethod + "," + viaBlock + "," + Join(described) + ";";

            // foreach の kv を、.Key / .Value 以外で使う（変数に入れる・メソッドに渡す・ラムダの中で使う）
            int beatsCount = 0;
            string passList = "";
            var picked = new List<KeyValuePair<string, int>>();
            foreach (var kv in scores)
            {
                KeyValuePair<string, int> copy = kv;
                if (IsPassed(kv))
                    picked.Add(copy);
                if (scores.Count(other => other.Value < kv.Value) >= 2)
                    beatsCount++;
            }
            foreach (var kv in picked.OrderBy(p => p.Key))
                passList += Describe(kv) + " ";
            r += "e:" + beatsCount + "," + passList.TrimEnd() + ";";

            // 汎用クラスの中
            var shop = new Shop("s");
            shop.prices["apple"] = 120;
            shop.prices["bread"] = 80;
            shop.prices["cake"] = 80;
            r += "f:" + shop.Cheapest() + ";";

            return r;
        }
    }
}
