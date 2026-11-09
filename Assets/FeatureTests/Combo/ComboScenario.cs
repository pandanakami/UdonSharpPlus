using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace FeatureTests.Combo
{
    // #63: 機能の組み合わせのシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    // （a〜g は #63 の注記の「組み合わせ」の記号）
    public interface IFighter
    {
        string Name();
        int Power();
        void Boost(int by);
    }

    // c: 普通のクラスで実装するインタフェース。引数・戻り値が List / Dictionary
    public interface IRoster
    {
        List<string> Strong(List<IFighter> fighters, int limit);
        Dictionary<string, int> PowerByName(List<IFighter> fighters);
    }

    public class Knight : IFighter
    {
        public int power = 5;
        public string Name() { return "knight"; }
        public int Power() { return power; }
        public void Boost(int by) { power += by; }
    }

    public class Archer : IFighter
    {
        public int power = 3;
        public List<int> arrows = new List<int> { 1, 2, 3 };
        public string Name() { return "archer"; }
        // b: 実装メソッドの中の LINQ
        public int Power() { return power + arrows.Count(a => a >= 2); }
        public void Boost(int by) { arrows.Add(by + 1); }
    }

    public class Mage : IFighter
    {
        public List<int> spells = new List<int> { 30 };
        public string Name() { return "mage"; }
        public int Power() { return spells.Sum(); }
        // b: 実装メソッドの中のラムダ（引数を読む）
        public void Boost(int by)
        {
            spells.Add(by * 2);
            spells.RemoveAll(s => s > by * 10);
        }
    }

    public class Thief : IFighter
    {
        public int power = 1;
        public string Name() { return "thief"; }
        public int Power() { return power * 4; }
        public void Boost(int by) { power += by; }
    }

    // d: ジェネリッククラスによるインタフェースの実装
    public class Hired<T> : IFighter
    {
        public T tag;
        public int power;
        public Hired(T tag, int power)
        {
            this.tag = tag;
            this.power = power;
        }
        public string Name() { return "hired:" + tag; }
        public int Power() { return power; }
        public void Boost(int by) { power += by * 3; }
    }

    public class Roster : IRoster
    {
        public List<string> Strong(List<IFighter> fighters, int limit)
        {
            return fighters.Where(f => f.Power() >= limit).Select(f => f.Name()).OrderBy(n => n).ToList();
        }

        public Dictionary<string, int> PowerByName(List<IFighter> fighters)
        {
            return fighters.ToDictionary(f => f.Name(), f => f.Power());
        }
    }

    // d: 型引数にインタフェースを渡すジェネリッククラス
    public class ComboPool<T>
    {
        private List<T> _items = new List<T>();
        public int Count => _items.Count;
        public void Add(T item) { _items.Add(item); }
        public T Get(int i) { return _items[i]; }
    }

    // e: ジェネリッククラスの中の Dictionary<string, T> に LINQ
    public class Registry<T>
    {
        private Dictionary<string, T> _map = new Dictionary<string, T>();
        public void Put(string key, T value) { _map[key] = value; }
        public int CountPrefix(string prefix) { return _map.Count(kv => kv.Key.StartsWith(prefix)); }
        public List<T> SortedValues() { return _map.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(); }
        public bool Has(T value) { return _map.Values.Any(v => v.Equals(value)); }
    }

    public class Soldier
    {
        public string name;
        public int hp;
        public string Title { get; set; } = "";
        public int Health
        {
            get { return hp; }
            set { hp = value; }
        }
        public Soldier(string name, int hp)
        {
            this.name = name;
            this.hp = hp;
        }
    }

    // i: 普通のクラスのフィールドにある List / Dictionary の要素への複合代入・インクリメント（#74）
    public class Tally
    {
        public List<int> hits = new List<int> { 0, 0 };
        public Dictionary<string, int> counts = new Dictionary<string, int> { { "a", 0 } };
        public Tally inner;
        public void Hit(int i)
        {
            hits[i] += 2;
            counts["a"]++;
        }
    }

    // f: 普通のクラスの各メンバーの中のラムダ・LINQ
    public class Squad
    {
        public List<Soldier> members;

        // コンストラクタ
        public Squad(List<Soldier> all, int minHp) { members = all.Where(s => s.hp >= minHp).ToList(); }

        // 式形式のプロパティ
        public int Alive => members.Count(s => s.hp > 0);

        // 本体付きのプロパティ
        public string Leader
        {
            get { return members.OrderByDescending(s => s.hp).First().name; }
        }

        // [RecursiveMethod] のメソッドの中の List
        [RecursiveMethod]
        public int SumFrom(int i)
        {
            if (i >= members.Count)
                return 0;
            return members[i].hp + SumFrom(i + 1);
        }

        // 引数を読み、要素のフィールドに書くラムダ
        public void HitAll(int damage) { members.ForEach(s => s.hp -= damage); }
    }

    public static class ComboExt
    {
        // f: 拡張メソッドの中の LINQ
        public static int TotalHp(this List<Soldier> soldiers) { return soldiers.Sum(s => s.hp); }

        // e: T を使うラムダ（制約付きの CompareTo、T のキーでの並べ替え）
        public static List<T> Above<T>(List<T> list, T limit) where T : IComparable<T>
        {
            return list.Where(x => x.CompareTo(limit) > 0).ToList();
        }

        public static T Best<T>(List<T> list, T fallback)
        {
            return list.Count == 0 ? fallback : list.OrderByDescending(x => x).First();
        }

        // g: ジェネリックメソッドの中のラムダの中の LINQ
        public static int CountContaining<T>(List<List<T>> lists, T value)
        {
            return lists.Count(l => l.Any(x => x.Equals(value)));
        }
    }

    public static class ComboScenario
    {
        private static string Join(List<string> list)
        {
            string s = "";
            foreach (string x in list)
                s += x + " ";
            return s.TrimEnd();
        }

        private static string JoinInts(List<int> list)
        {
            string s = "";
            foreach (int x in list)
                s += x + " ";
            return s.TrimEnd();
        }

        // golem は UdonSharpBehaviour による IFighter の実装
        public static string Run(IFighter golem)
        {
            string r = "";

            // a: List<IFighter> に、普通のクラス（4 個以上なので switch で振り分ける）、ジェネリッククラス、UdonSharpBehaviour を混ぜる
            var fighters = new List<IFighter> { new Knight(), new Archer(), new Mage(), new Thief(), golem };
            fighters.Add(new Hired<string>("x", 2));
            int strong = fighters.Where(f => f.Power() > 4).Count();
            IFighter top = fighters.OrderByDescending(f => f.Power()).ThenBy(f => f.Name()).First();
            int sum = fighters.Sum(f => f.Power());
            IFighter[] array = fighters.ToArray();
            int weak = array.Count(f => f.Power() < 5);
            var byName = new Dictionary<string, IFighter>();
            foreach (IFighter f in fighters)
                byName[f.Name()] = f;
            byName["knight"].Boost(2);
            IFighter found;
            bool has = byName.TryGetValue("mage", out found);
            List<string> lowNames = byName.Where(kv => kv.Value.Power() < 5).Select(kv => kv.Key).OrderBy(k => k).ToList();
            r += "a:" + strong + "," + top.Name() + "," + sum + "," + weak + "," + byName["knight"].Power() + "," + has + found.Name() + ","
                 + Join(lowNames) + ";";

            // b: ラムダの中でインタフェースのメソッドを呼ぶ
            fighters.ForEach(f => f.Boost(1));
            IFighter mage = fighters.Find(f => f.Name() == "mage");
            int zeroIndex = fighters.FindIndex(f => f.Power() == 0);
            bool allPositive = fighters.TrueForAll(f => f.Power() > 0);
            List<int> powers = fighters.ConvertAll(f => f.Power());
            int longNamed = 0;
            fighters.ForEach(f =>
            {
                if (f.Name().Length > 5)
                    longNamed += f.Power();
            });
            r += "b:" + mage.Power() + "," + zeroIndex + "," + allPositive + "," + JoinInts(powers) + "," + longNamed + ";";

            // c: 普通のクラスによるインタフェース実装の、List / Dictionary の引数・戻り値
            IRoster roster = new Roster();
            List<string> strongNames = roster.Strong(fighters, 5);
            Dictionary<string, int> powerByName = roster.PowerByName(fighters);
            r += "c:" + Join(strongNames) + "," + powerByName.Count + "," + powerByName["thief"] + "," + powerByName["golem"] + ";";

            // d: ジェネリクス × インタフェース
            var pool = new ComboPool<IFighter>();
            pool.Add(new Thief());
            pool.Add(golem);
            pool.Add(new Hired<int>(7, 1));
            pool.Get(2).Boost(1);
            IFighter[] picked = { pool.Get(0), pool.Get(2) };
            var hireds = new List<Hired<int>> { new Hired<int>(1, 5), new Hired<int>(2, 8) };
            r += "d:" + pool.Count + "," + pool.Get(1).Name() + "," + pool.Get(2).Name() + pool.Get(2).Power() + "," + (picked[1] is Hired<int>) + ","
                 + (picked[0] is Hired<int>) + "," + hireds.Sum(h => h.Power()) + "," + hireds.Find(h => h.tag == 2).power + ";";

            // e: ジェネリクス × ラムダ・LINQ
            var registry = new Registry<int>();
            registry.Put("ab", 3);
            registry.Put("ac", 1);
            registry.Put("b", 2);
            var soldiers = new Registry<Soldier>();
            soldiers.Put("z", new Soldier("zed", 9));
            soldiers.Put("y", new Soldier("yan", 4));
            List<int> above = ComboExt.Above(new List<int> { 5, 1, 7, 3 }, 3);
            List<string> aboveWords = ComboExt.Above(new List<string> { "b", "a", "c" }, "a");
            r += "e:" + registry.CountPrefix("a") + "," + JoinInts(registry.SortedValues()) + "," + registry.Has(2) + registry.Has(9) + ","
                 + soldiers.SortedValues()[0].name + "," + JoinInts(above) + "," + Join(aboveWords) + "," + ComboExt.Best(above, 0)
                 + ComboExt.Best(new List<string>(), "none") + ";";

            // f: 普通のクラスの各メンバー × ラムダ・LINQ
            var all = new List<Soldier> { new Soldier("a", 10), new Soldier("b", 3), new Soldier("c", 7), new Soldier("d", 0) };
            var squad = new Squad(all, 3);
            int aliveBefore = squad.Alive;
            squad.HitAll(4);
            r += "f:" + squad.members.Count + "," + aliveBefore + "," + squad.Alive + "," + squad.Leader + "," + squad.SumFrom(0) + "," + all.TotalHp() + ";";

            // g: 入れ子
            var teams = new List<List<Soldier>> { new List<Soldier> { all[0], all[1] }, new List<Soldier> { all[2] }, new List<Soldier>() };
            List<int> aliveCounts = teams.Select(t => t.Count(s => s.hp > 0)).ToList();
            int withStrong = teams.Count(t => t.Any(s => s.hp > 5));
            var grid = new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 2, 3 }, new List<int> { 4 } };
            int containing = ComboExt.CountContaining(grid, 2);
            int evenSum = grid.Sum(l => l.Where(x => x % 2 == 0).Sum());
            r += "g:" + JoinInts(aliveCounts) + "," + withStrong + "," + containing + "," + evenSum + ";";

            // h: 戻り値が void のラムダの式の本体での代入・インクリメント（#73）
            var tally = new Dictionary<int, int>();
            var nums = new List<int> { 1, 2, 2 };
            nums.ForEach(x => tally[x] = 0);
            nums.ForEach(x => tally[x]++);
            var acc = new List<int> { 0 };
            nums.ForEach(x => acc[0] += x);
            all.ForEach(s => s.Title = "t" + s.name);
            all.ForEach(s => s.Health += 10);
            all.ForEach(s => s.hp = s.hp * 2);
            all.ForEach(s => s.hp++);
            r += "h:" + tally[1] + tally[2] + "," + acc[0] + "," + all[1].Title + "," + all[0].hp + "," + all[3].Health + ";";

            // i: 普通のクラスのフィールドにある List / Dictionary の要素への複合代入・インクリメント（#74）
            var tl = new Tally();
            tl.inner = new Tally();
            tl.hits[0] += 5;
            tl.hits[0]++;
            --tl.hits[1];
            tl.counts["a"] += 3;
            tl.counts["a"]--;
            tl.inner.hits[1] += 7;
            tl.Hit(1);
            var tallies = new List<Tally> { tl, tl.inner };
            tallies.ForEach(x => x.hits[0] += 10);
            tallies.ForEach(x => x.counts["a"]++);
            r += "i:" + tl.hits[0] + "," + tl.hits[1] + "," + tl.counts["a"] + "," + tl.inner.hits[0] + tl.inner.hits[1] + "," + tl.inner.counts["a"] + ";";

            return r;
        }
    }
}
