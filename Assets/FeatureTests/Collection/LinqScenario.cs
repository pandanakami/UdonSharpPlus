using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Collection
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

            // 並べ替え（安定・複数キー・降順）
            List<int> asc = ints.OrderBy(x => x).ToList();
            int[] desc = array.OrderByDescending(x => x).ToArray();
            var words = new List<string> { "pear", "fig", "apple", "kiwi", "date" };
            var byLength = words.OrderBy(w => w.Length).ThenByDescending(w => w);
            string wordText = "";
            foreach (string w in byLength)
                wordText += w + " ";
            List<string> byScore = players.OrderByDescending(p => p.score / 20).ThenBy(p => p.speed).Select(p => p.name).ToList();
            r += "i:" + Join(asc) + "," + desc[0] + desc[3] + "," + wordText.TrimEnd() + "," + byScore[0] + byScore[1] + byScore[2] + ";";

            // 並べ替えの後につなぐ・並べ替えの前に絞る（キーを計算する回数も C# と同じ）
            int keyCalls = 0;
            int smallestOver2 = ints.Where(x => x > 2).OrderBy(x => { keyCalls++; return x; }).First();
            List<int> top3 = ints.OrderByDescending(x => x).Take(3).Select(x => x * 10).ToList();
            r += "j:" + smallestOver2 + "," + keyCalls + "," + Join(top3) + ";";

            // Distinct・Skip・Take（Take は取り終えたら次を取りに行かない）
            List<int> unique = ints.Distinct().ToList();
            List<string> uniqueWords = new List<string> { "a", "b", "a", "c", "b" }.Distinct().ToList();
            List<int> page = ints.Skip(2).Take(3).ToList();
            int pulled = 0;
            int firstTwo = ints.Where(x => { pulled++; return true; }).Take(2).Sum();
            int none = ints.Take(0).Count() + ints.Skip(100).Count();
            r += "k:" + Join(unique) + "," + uniqueWords.Count + uniqueWords[2] + "," + Join(page) + "," + firstTwo + "," + pulled + "," + none + ";";

            // ToDictionary
            Dictionary<string, Player> byName = players.ToDictionary(p => p.name);
            Dictionary<string, int> scoreByName = players.ToDictionary(p => p.name, p => p.score);
            r += "l:" + byName["bob"].score + "," + scoreByName["cid"] + "," + scoreByName.Count + ";";

            // 汎用クラス・string の配列を元にする
            Player[] team = { new Player("eve", 20, 1f), new Player("fay", 80, 3f), new Player("gus", 50, 2f) };
            List<string> fast = team.Where(p => p.speed >= 2f).OrderBy(p => p.name).Select(p => p.name).ToList();
            Player slowest = team.OrderBy(p => p.speed).First();
            int teamScore = team.Sum(p => p.score);
            Player[] ranked = team.OrderByDescending(p => p.score).ToArray();
            bool hasEve = team.Any(p => p.name == "eve");
            string[] tags = { "red", "blue", "red", "green" };
            List<string> uniqueTags = tags.Distinct().OrderBy(t => t).ToList();
            r += "n:" + fast[0] + fast[1] + "," + slowest.name + "," + teamScore + "," + ranked[0].name + ranked[2].name + "," + hasEve + ","
                 + uniqueTags.Count + uniqueTags[0] + "," + tags.Count(t => t == "red") + ";";

            // o: #63 で足したもの（long・double の集計、Enumerable の static の形）
            var longs = new List<long> { 3000000000L, 5L, -2L };
            double[] doubles = { 1.5, 2.25, -0.5 };
            r += "o:" + longs.Sum() + "," + longs.Min() + "," + longs.Max() + "," + longs.Average() + "," + doubles.Sum() + "," + doubles.Min() + ","
                 + doubles.Max() + "," + doubles.Average() + "," + players.Sum(p => (long)p.score) + "," + players.Average(p => (double)p.score) + ","
                 + Enumerable.Where(ints, x => x > 4).Count() + "," + Enumerable.Sum(array) + ";";

            return r;
        }
    }
}
