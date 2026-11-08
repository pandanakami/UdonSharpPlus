using System.Collections.Generic;
using System.Linq;

namespace CollectionPoC
{
    // #58: LINQ のシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    public class Player
    {
        public string name;
        public int score;
        public float speed;

        public Player(string name, int score, float speed) { this.name = name; this.score = score; this.speed = speed; }

        // 汎用クラスのメソッドの中の LINQ
        public bool IsTop(List<Player> all) { return score == all.Max(p => p.score); }
    }

    public static class LinqScenario
    {
        private static bool IsEven(int v) { return v % 2 == 0; }

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
            var ints = new List<int> { 5, 3, 8, 1, 6, 3 };
            int[] array = { 4, 9, 2, 7 };
            int limit = 3;

            // つないだ呼び出し・ToList / ToArray
            List<int> doubled = ints.Where(x => x > limit).Select(x => x * 2).ToList();
            int[] evens = array.Where(IsEven).ToArray();
            r += "a:" + Join(doubled) + "," + evens.Length + evens[0] + evens[1] + ";";

            // 変数に入れたクエリ（何度使っても同じ結果）。元を後で変える場合は C# と結果が違う（遅延評価にしないため。README に記載）
            var big = ints.Where(x => x >= 5);
            int bigCount = big.Count();
            string bigText = "";
            foreach (int x in big)
                bigText += x;
            foreach (string s in big.Select(x => "<" + x + ">"))
                bigText += s;
            r += "b:" + bigCount + "," + bigText + ";";

            // 判定・数え上げ
            r += "c:" + ints.Any() + "," + ints.Any(x => x > 7) + "," + array.Any(x => x > 100) + "," + ints.All(x => x > 0) + ","
                 + array.All(x => x > 2) + "," + ints.Count() + "," + ints.Count(x => x == 3) + "," + array.Contains(9) + "," + ints.Contains(42) + ";";

            // 先頭・末尾
            r += "d:" + ints.First() + "," + ints.First(x => x > limit) + "," + ints.FirstOrDefault(x => x > 100) + ","
                 + array.Last() + "," + ints.Last(x => x < 5) + "," + ints.LastOrDefault(x => x > 100) + ";";

            // 集計
            r += "e:" + ints.Sum() + "," + array.Sum(x => x * 10) + "," + ints.Min() + "," + array.Max() + "," + ints.Max(x => -x) + ","
                 + array.Average() + "," + array.Average(x => x * 0.5f) + ";";

            // 途中で止まる（ラムダを呼ぶ回数も C# と同じ）
            int calls = 0;
            bool hit = ints.Where(x => { calls++; return x > 6; }).Any();
            int calls2 = 0;
            int firstOver = ints.Select(x => { calls2++; return x * 3; }).First(x => x > 10);
            r += "f:" + hit + calls + "," + firstOver + "," + calls2 + ";";

            // 汎用クラス
            var players = new List<Player> { new Player("ann", 30, 1.5f), new Player("bob", 50, 2f), new Player("cid", 10, 0.5f) };
            List<string> names = players.Where(p => p.score >= 30).Select(p => p.name).ToList();
            Player best = players.First(p => p.IsTop(players));
            float totalSpeed = players.Sum(p => p.speed);
            Player[] slow = players.Where(p => p.speed < 2f).ToArray();
            r += "g:" + names[0] + names[1] + names.Count + "," + best.name + "," + totalSpeed + "," + slow.Length + slow[1].name + ";";

            // 入れ子（ラムダの中の LINQ）・外の変数に書く
            var other = new List<int> { 3, 6, 9 };
            int shared = ints.Count(x => other.Contains(x));
            int total = 0;
            int matched = ints.Where(x => other.Any(y => y == x)).Select(x => { total += x; return x; }).Count();
            r += "h:" + shared + "," + matched + "," + total + ";";

            return r;
        }
    }
}
