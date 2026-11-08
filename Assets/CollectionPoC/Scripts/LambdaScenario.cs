using System.Collections.Generic;

namespace CollectionPoC
{
    // #57: ラムダを受け取る List のメソッドのシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    public class Unit
    {
        public string name;
        public int hp;
        public int team;
        public List<int> damages = new List<int>();

        public Unit(string name, int hp, int team) { this.name = name; this.hp = hp; this.team = team; }

        // 汎用クラスのメソッドの中のラムダ（フィールドを読み書きする）
        public int TakeAll(int bonus)
        {
            int taken = 0;
            damages.ForEach(d => { hp -= d + bonus; taken++; });
            damages.Clear();
            return taken;
        }
    }

    public static class LambdaScenario
    {
        private static bool IsOdd(int v) { return v % 2 != 0; }

        private static string Join(List<int> list)
        {
            string s = "";
            foreach (int x in list)
                s += x + " ";
            return s.TrimEnd();
        }

        private static string Names(List<Unit> units)
        {
            string s = "";
            foreach (Unit u in units)
                s += u.name;
            return s;
        }

        public static string Run()
        {
            string r = "";
            var ints = new List<int> { 5, 3, 8, 1, 6 };

            // 検索（外の変数を読む）
            int limit = 4;
            r += "a:" + ints.Find(x => x > limit) + "," + ints.FindLast(x => x < limit) + "," + ints.FindIndex(x => x == 8) + ","
                 + ints.FindLastIndex(x => x > limit) + "," + ints.Find(x => x > 100) + "," + ints.FindIndex(x => x > 100) + ";";
            r += "b:" + ints.Exists(x => x == 1) + "," + ints.Exists(x => x > 100) + "," + ints.TrueForAll(x => x > 0) + "," + ints.TrueForAll(x => x > 1) + ";";

            // メソッド名を渡す
            r += "c:" + ints.FindIndex(IsOdd) + "," + Join(ints.FindAll(IsOdd)) + ";";

            // 外の変数に書く（ref で渡す）
            int sum = 10;
            ints.ForEach(x => sum += x);
            int count = 0;
            ints.ForEach(x =>
            {
                if (x > limit)
                    count++;
            });
            r += "d:" + sum + "," + count + ";";

            // 条件の中で書いた値を、同じ文の中で使う
            int seen = -1;
            if (ints.Exists(x => { seen = x; return x == 8; }))
                r += "e:" + seen + ";";

            // return のあるブロックの本体
            int firstEvenOverFive = ints.Find(x =>
            {
                if (x % 2 != 0)
                    return false;
                return x > 5;
            });
            r += "f:" + firstEvenOverFive + ";";

            // 並べ替え（降順・安定）
            var sorted = new List<int>(ints);
            sorted.Sort((a, b) => b - a);
            var units = new List<Unit> { new Unit("a", 10, 1), new Unit("b", 0, 2), new Unit("c", 5, 1), new Unit("d", 0, 1), new Unit("e", 7, 2) };
            var byTeam = new List<Unit>(units);
            byTeam.Sort((p, q) => p.team - q.team);
            r += "g:" + Join(sorted) + "," + Names(byTeam) + ";";

            // 汎用クラスの要素・FindAll / RemoveAll
            int team = 1;
            List<Unit> team1 = units.FindAll(u => u.team == team);
            Unit strongest = units.Find(u => u.hp >= 7);
            int removed = units.RemoveAll(u => u.hp <= 0);
            r += "h:" + Names(team1) + "," + strongest.name + "," + removed + "," + Names(units) + ";";

            // 入れ子のラムダ（内側で外の変数に書く）
            var other = new List<int> { 3, 6, 9 };
            int matches = 0;
            ints.ForEach(x => { if (other.Exists(y => y == x)) matches++; });
            bool shared = ints.Exists(x => other.Exists(y => y == x * 2));
            r += "i:" + matches + "," + shared + ";";

            // 汎用クラスのメソッドの中のラムダ
            Unit target = units[0];
            target.damages.Add(2);
            target.damages.Add(3);
            int taken = target.TakeAll(1);
            r += "j:" + taken + "," + target.hp + "," + target.damages.Count + ";";

            // 範囲を指定した FindIndex / FindLastIndex と、ConvertAll
            var seq = new List<int> { 7, 2, 9, 4, 9, 1 };
            r += "k:" + seq.FindIndex(2, x => x == 9) + "," + seq.FindIndex(3, x => x == 9) + "," + seq.FindIndex(1, 2, x => x > 5) + ","
                 + seq.FindIndex(3, 1, x => x > 5) + "," + seq.FindLastIndex(3, x => x == 9) + "," + seq.FindLastIndex(5, 3, x => x == 9) + ","
                 + seq.FindLastIndex(1, 2, x => x == 9) + ";";
            List<string> labels = seq.ConvertAll(x => x > 5 ? "H" + x : "L" + x);
            List<Unit> made = seq.ConvertAll(x => new Unit("u" + x, x, 0));
            List<int> lengths = labels.ConvertAll(s => s.Length);
            r += "l:" + labels[0] + labels[1] + "," + Names(made) + "," + Join(lengths) + ";";

            return r;
        }
    }
}
