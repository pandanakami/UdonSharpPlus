using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    public enum GbKind { Red, Green, Blue }

    // #140：GroupBy の要素・キーにする普通のクラス
    public class GbItem
    {
        public string name;
        public int score;
        public GbKind kind;
        public GbItem(string name, int score, GbKind kind) { this.name = name; this.score = score; this.kind = kind; }
    }

    public class GbTeam
    {
        public string label;
        public GbTeam(string label) { this.label = label; }
    }

    public enum GbCode { A, B, C = A, N = -3 }

    public interface IGbShape { int Size(); }

    public class GbSq : IGbShape
    {
        public int n;
        public GbSq(int n) { this.n = n; }
        public int Size() => n;
    }

    // 演算子（#105）を持つクラス。Equals は override しないので、C# の GroupBy も参照で比べる
    public class GbMoney
    {
        public int v;
        public GbMoney(int v) { this.v = v; }
        public static GbMoney operator +(GbMoney a, GbMoney b) => new GbMoney(a.v + b.v);
        public static bool operator ==(GbMoney a, GbMoney b) => (object)a == null ? (object)b == null : (object)b != null && a.v == b.v;
        public static bool operator !=(GbMoney a, GbMoney b) => !(a == b);
        public static implicit operator int(GbMoney m) => m.v;
    }

    // グループを持つ普通のクラス
    public class GbBag
    {
        public IGrouping<bool, int> group;
        public List<IGrouping<int, int>> groups;
    }

    public static class GbUtil
    {
        public static string FloatName(float f) =>
            f != f ? "NaN" : f > float.MaxValue ? "+Inf" : f < -float.MaxValue ? "-Inf" : f == 0 ? (1 / f > 0 ? "+0" : "-0") : ((int)(f * 10)).ToString();

        // グループを受け取る・返す
        public static string Describe<K>(IGrouping<K, int> g) => g.Key + "[" + string.Join("", g) + "]";
        public static IGrouping<int, int> Biggest(List<int> xs) => xs.GroupBy(x => x % 3).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        public static int KeyPlusCount(IGrouping<int, int> g) => g.Key * 100 + g.Count();

        // デリゲートを受け取るジェネリックメソッド
        public static string CountBy<T, K>(List<T> xs, System.Func<T, K> f) => string.Join("", xs.GroupBy(f).Select(g => g.Count()));
        public static int Mod3(int x) => x % 3;

        // 普通のクラス（static）の中の GroupBy
        public static string Summary(List<int> xs) => string.Join(",", xs.GroupBy(x => x % 2 == 0).Select(g => g.Key + "=" + g.Count()));

        // ジェネリックメソッドの中の GroupBy
        public static int LargestGroup<T>(List<T> xs) => xs.Count == 0 ? 0 : xs.GroupBy(x => x).Max(g => g.Count());
    }

    // #140 のパネル
    public class GroupByRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "G1=0:3963|1:141|2:5255;G2=4:PEAR,KIWI,PLUM,DATE|3:FIG|5:APPLE|6:BANANA,{ Key = 0, N = 3, Total = 15, Best = 8 } { Key = 2, N = 2, Total = 10, Best = 7 } { Key = 1, N = 1, Total = 1, Best = 1 };G3=5x3,1x2,3x2,2x1,4x1,6x1,9x1,3,1,5;G4=False5,True6,p2,f1,a1,k1,d1,b1,0k3,2k2,1k1,p2,f1,a1,null2,k1,d1,b1,t13,t22,null1;G5=0:bd|1:ace,False3,True12;G6=1[abde]0[cf]TF,3:33,414;G7=0False,20,6/9/4,2/6/3,31159535426,True;G8=False=8,True=3,320,4x3,2x2,7x1,3,12,6;G9=11,11,2,22;G10=0:15:acf,2:10:be,1:1:d;G11=3-1-1-5-9-5-3-5,426,pear/kiwi/plum/date,39631415255;G12=-2147483648x2,2147483647x2,-1x2,0x1,-9223372036854775808x2,5x1,-5x1,255x2,0x1,7x1,2,2,3,15x2,NaNx2,+0x2,+Infx2,-Infx1,NaNx2,-0x2,2.25x1;G13=[ab]2|null2|[]2|[AB]1|[é]1|[日本]2|[e\u0301]1;G14=0x2,-3x2,99x2,1x1;G15=t13,t1b2,null2;G16=A:123|B:12|C:1,aa1a2a3|cc1|bb2,AA3A2A1|BB2B1|CC1,1C1B1A1|2B2A2|3A3;G17=00,17,1x5,6,53:600:0.37.21.10.47:68871:67146714;G18=k1e1k2e2k3e3k4e4,40,12340;G19=0:4,2[5255],p8/f3/a5/k4/d4/b6,False[314123]26,TrueTrue;G20=11,8True351TrueFalse,31159535,1932,4,88,6488,[][,]pear++kiwi+plum+date++banana;G21=0:30c,2:20e,1:2d|1:abde7,0:cf8|0n3,1n1,2n5|1816|302;G22=5x1,2x4,3x1,0x1,8x1,1x3,7x1,3=2,1=6,0=3,2=2,39634555,5,1,2:31,1:4926,3:5,60/3/9,20/3/11,17;G23=3:anncy,5:bobed,8:dee|2:8cy,3:16ann|51,61,71,81,111|3221358;G24=832112111321,330,32,32/12;G25=3,2,3:18,4:8,4True,2/2;G26=3:37/2|2:26/2|1:159/3|0:48/2,0:48/2,3:3711/3|2:2/1|1:1/1,2,0,1=10,2=14,0=18,0=18,6/1";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5 };
            var words = new List<string> { "pear", "fig", "apple", null, "kiwi", "plum", "date", null, "banana" };
            var t1 = new GbTeam("t1");
            var t2 = new GbTeam("t2");
            var items = new List<GbItem>
            {
                new GbItem("a", 5, GbKind.Red), new GbItem("b", 3, GbKind.Blue), new GbItem("c", 8, GbKind.Red),
                new GbItem("d", 1, GbKind.Green), new GbItem("e", 7, GbKind.Blue), new GbItem("f", 2, GbKind.Red),
            };

            // G1：基本（グループの順番は最初に出た順、グループの中も元の順）
            s += "G1=" + string.Join("|", nums.GroupBy(x => x % 3).Select(g => g.Key + ":" + string.Join("", g))) + ";";

            // G2：要素を変換する形・集計・匿名型で受ける
            var byLength = words.Where(w => w != null).GroupBy(w => w.Length, w => w.ToUpper()).ToList();
            var stats = items.GroupBy(i => i.kind).Select(g => new { Key = (int)g.Key, N = g.Count(), Total = g.Sum(i => i.score), Best = g.Max(i => i.score) }).ToList();
            s += "G2=" + string.Join("|", byLength.Select(g => g.Key + ":" + string.Join(",", g))) + "," + string.Join(" ", stats.Select(x => x.ToString())) + ";";

            // G3：グループを並べ替える・絞る
            s += "G3=" + string.Join(",", nums.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", nums.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key)) + ";";

            // G4：キーの型（bool・char・enum・null を含む string・参照）
            var teamOf = new List<GbTeam> { t1, t2, t1, null, t2, t1 };
            s += "G4=" + string.Join(",", nums.GroupBy(x => x > 3).Select(g => g.Key + "" + g.Count()))
                 + "," + string.Join(",", words.Where(w => w != null).GroupBy(w => w[0]).Select(g => g.Key + "" + g.Count()))
                 + "," + string.Join(",", items.GroupBy(i => i.kind).Select(g => (int)g.Key + "k" + g.Count()))
                 + "," + string.Join(",", words.GroupBy(w => w == null ? null : w.Substring(0, 1)).Select(g => (g.Key ?? "null") + g.Count()))
                 + "," + string.Join(",", teamOf.GroupBy(t => t).Select(g => (g.Key == null ? "null" : g.Key.label) + g.Count())) + ";";

            // G5：Dictionary を元にする（Dictionary の順番は C# と同じとは限らないので、並べ替えてから見る）
            var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, { "c", 3 }, { "d", 4 }, { "e", 5 } };
            s += "G5=" + string.Join("|", dict.GroupBy(kv => kv.Value % 2, kv => kv.Key).OrderBy(g => g.Key).Select(g => g.Key + ":" + string.Join("", g.OrderBy(k => k))))
                 + "," + string.Join(",", dict.GroupBy(kv => kv.Value > 2).OrderBy(g => g.Key).Select(g => g.Key + "" + g.Sum(kv => kv.Value))) + ";";

            // G6：foreach・型を書いた変数・First・ToDictionary
            string g6 = "";
            foreach (var g in items.GroupBy(i => i.score % 2))
            {
                g6 += g.Key + "[";
                foreach (GbItem i in g)
                    g6 += i.name;
                g6 += "]";
            }
            foreach (IGrouping<bool, int> g in nums.GroupBy(x => x < 4))
                g6 += g.Key ? "T" : "F";
            IGrouping<int, int> firstGroup = nums.GroupBy(x => x % 4).First();
            var counts = words.Where(w => w != null).GroupBy(w => w.Length).ToDictionary(g => g.Key, g => g.Count());
            s += "G6=" + g6 + "," + firstGroup.Key + ":" + string.Join("", firstGroup) + "," + counts[4] + counts[3] + counts.Count + ";";

            // G7：空の元・グループの中の LINQ・グループのリスト
            var empty = new List<int>();
            var groups = nums.GroupBy(x => x % 2).ToList();
            s += "G7=" + empty.GroupBy(x => x).Count() + empty.GroupBy(x => x).Any() + "," + groups.Count + groups[1].Key
                 + "," + string.Join(",", groups.Select(g => g.Where(x => x > 2).Count() + "/" + g.OrderBy(x => -x).First() + "/" + g.Distinct().Count()))
                 + "," + string.Join("", groups.SelectMany(g => g.ToList())) + "," + nums.GroupBy(x => x).Any(g => g.Count() > 2) + ";";

            // G8：普通のクラス・ジェネリックメソッドの中、配列・Range を元にする
            int[] arr = { 4, 4, 2, 7, 2, 4 };
            s += "G8=" + GbUtil.Summary(nums) + "," + GbUtil.LargestGroup(nums) + GbUtil.LargestGroup(words) + GbUtil.LargestGroup(new List<int>())
                 + "," + string.Join(",", arr.GroupBy(x => x).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", Enumerable.Range(0, 7).GroupBy(i => i / 3).Select(g => g.Sum())) + ";";

            // G9：キーのラムダ・要素のラムダは、要素ごとに 1 回ずつ（C# と同じ）。GroupBy の結果を変数に入れて 2 回使う
            int keyCalls = 0, elementCalls = 0;
            var counted = nums.GroupBy(x => { keyCalls++; return x % 2; }, x => { elementCalls++; return x * 10; }).ToList();
            var kept = nums.GroupBy(x => x % 2);
            s += "G9=" + keyCalls + "," + elementCalls + "," + counted.Count + "," + kept.Count() + kept.Count() + ";";

            // G10：匿名型の集計からの並べ替え（スコアボード）
            var board = items.GroupBy(i => i.kind)
                .Select(g => new { Kind = (int)g.Key, Total = g.Sum(i => i.score), Names = string.Join("", g.Select(i => i.name)) })
                .OrderByDescending(x => x.Total)
                .ToList();
            s += "G10=" + string.Join(",", board.Select(b => b.Kind + ":" + b.Total + ":" + b.Names)) + ";";

            // G11：グループをそのまま渡す（string.Join・string.Concat・SelectMany(g => g)）
            var parity = nums.GroupBy(x => x % 2).ToList();
            var initials = words.Where(w => w != null).GroupBy(w => w.Length).First();
            s += "G11=" + string.Join("-", parity[0]) + "," + string.Concat(parity[1]) + "," + string.Join("/", initials) + "," + string.Join("", nums.GroupBy(x => x % 3).SelectMany(g => g)) + ";";
            return s + More(nums, words, items);
        }

        // Behaviour のフィールドに入れるグループ
        private IGrouping<int, int> keptGroup;
        private List<IGrouping<string, int>> keptGroups;

        // #140 の追加テスト案（G12〜）
        private string More(List<int> nums, List<string> words, List<GbItem> items)
        {
            string s = "";

            // G12：数値キーの境界（最小・最大・負数・long・byte・float/double の NaN・±0・±∞）
            var big = new List<int> { int.MinValue, int.MaxValue, -1, 0, int.MinValue, -1, int.MaxValue };
            var longs = new List<long> { long.MinValue, 5L, long.MinValue, -5L };
            var bytes = new List<byte> { 255, 0, 255, 7 };
            var fs = new List<float> { 1.5f, float.NaN, 0f, -0f, float.PositiveInfinity, float.NaN, float.NegativeInfinity, 1.5f, float.PositiveInfinity };
            var ds = new List<double> { double.NaN, -0.0, 0.0, double.NaN, 2.25 };
            s += "G12=" + string.Join(",", big.GroupBy(x => x).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", longs.GroupBy(x => x).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", bytes.GroupBy(x => x).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", big.GroupBy(x => (long)x / 2).Select(g => g.Count()))
                 + "," + string.Join(",", fs.GroupBy(x => x).Select(g => GbUtil.FloatName(g.Key) + "x" + g.Count()))
                 + "," + string.Join(",", ds.GroupBy(x => x).Select(g => (g.Key != g.Key ? "NaN" : g.Key == 0 ? (1 / g.Key > 0 ? "+0" : "-0") : "2.25") + "x" + g.Count())) + ";";

            // G13：string のキー（null・空文字・内容が同じ別のインスタンス・大文字小文字・Unicode）
            var ss = new List<string> { "ab", null, "", "AB", "AB".ToLower(), "é", "日本", "", null, "é", "日本" };
            s += "G13=" + string.Join("|", ss.GroupBy(w => w).Select(g => (g.Key == null ? "null" : "[" + g.Key + "]") + g.Count())) + ";";

            // G14：enum のキー（同じ値の別名・負の値・定義のない値）
            var codes = new List<GbCode> { GbCode.A, GbCode.C, GbCode.N, (GbCode)99, GbCode.B, (GbCode)99, GbCode.N };
            s += "G14=" + string.Join(",", codes.GroupBy(k => k).Select(g => (int)g.Key + "x" + g.Count())) + ";";

            // G15：参照のキー（同じ参照・内容が同じ別のインスタンス・null）
            var t1 = new GbTeam("x");
            var t1b = new GbTeam("x");
            var refs = new List<GbTeam> { t1, t1b, null, t1, null, t1b, t1 };
            s += "G15=" + string.Join(",", refs.GroupBy(t => t).Select(g => (g.Key == null ? "null" : g.Key == t1 ? "t1" : "t1b") + g.Count())) + ";";

            // G16：順番（キーが交互・要素を変換・Where/Select/Reverse/OrderBy のあと）
            var seq = new List<string> { "A1", "B1", "A2", "C1", "B2", "A3" };
            s += "G16=" + string.Join("|", seq.GroupBy(x => x[0], x => x.Substring(1)).Select(g => g.Key + ":" + string.Join("", g)))
                 + "," + string.Join("|", seq.Where(x => x != "B1").Select(x => x.ToLower()).GroupBy(x => x[0]).Select(g => g.Key + string.Join("", g)))
                 + "," + string.Join("|", seq.Select(x => x).Reverse().GroupBy(x => x[0]).Select(g => g.Key + string.Join("", g)))
                 + "," + string.Join("|", seq.OrderByDescending(x => x).GroupBy(x => x[1]).Select(g => g.Key + string.Join("", g))) + ";";

            // G17：空・1 つ・全部同じキー・全部違うキー・多い
            var none = new List<int>();
            int noneCalls = 0;
            var r0 = none.GroupBy(x => { noneCalls++; return x; }).ToList();
            var one = new List<int> { 7 };
            var same = Enumerable.Repeat(4, 5).ToList();
            var diff = Enumerable.Range(0, 6).ToList();
            var many = Enumerable.Range(0, 600).Select(i => (i * 37) % 101).ToList();
            var mg = many.GroupBy(x => x % 53).ToList();
            s += "G17=" + r0.Count + noneCalls + "," + one.GroupBy(x => x).Count() + one.GroupBy(x => x).First().Key
                 + "," + same.GroupBy(x => x).Count() + "x" + same.GroupBy(x => x).First().Count() + "," + diff.GroupBy(x => x).Count()
                 + "," + mg.Count + ":" + mg.Sum(g => g.Count()) + ":" + string.Join(".", mg.Take(5).Select(g => g.Key))
                 + ":" + mg.Select((g, i) => g.Key * (i + 1) + g.Sum()).Sum() + ":" + string.Join("", mg[10].Take(4)) + ";";

            // G18：ラムダを呼ぶ順番（要素ごとにキー → 要素）。後ろに Where・Take・First があっても、GroupBy は全部を読む
            string log = "";
            var four = new List<int> { 1, 2, 3, 4 };
            var picked = four.GroupBy(x => { log += "k" + x; return x % 2; }, x => { log += "e" + x; return x * 10; }).Where(g => g.Key == 1).Take(1).First();
            string log2 = "";
            int firstKey = four.GroupBy(x => { log2 += x; return x > 2; }).First().Key ? 1 : 0;
            s += "G18=" + log + "," + picked.Sum() + "," + log2 + firstKey + ";";

            // G19：IGrouping を引数・戻り値・Behaviour のフィールド・普通のクラスのフィールドで使う。FirstOrDefault で見つからない
            IGrouping<int, int> got = GbUtil.Biggest(nums);
            keptGroup = nums.GroupBy(x => x % 3).Last();
            keptGroups = words.Where(w => w != null).GroupBy(w => w.Substring(0, 1), w => w.Length).ToList();
            var bag = new GbBag();
            bag.group = nums.GroupBy(x => x > 4).First();
            bag.groups = nums.GroupBy(x => x % 2).ToList();
            var missing = nums.GroupBy(x => x).FirstOrDefault(g => g.Key > 100);
            s += "G19=" + got.Key + ":" + got.Count() + "," + GbUtil.Describe(keptGroup) + "," + string.Join("/", keptGroups.Select(g => g.Key + g.Sum()))
                 + "," + GbUtil.Describe(bag.group) + bag.groups.Count + bag.groups[1].Max() + "," + (missing == null) + (nums.GroupBy(x => x).FirstOrDefault() != null) + ";";

            // G20：グループ自身の操作（Key を何度も読む・LINQ・2 回回す・空の結果や null の要素を Join）
            var gg = nums.GroupBy(x => x % 2).First();
            int twice = 0;
            foreach (var x in gg)
                twice += x;
            foreach (var x in gg)
                twice += x;
            s += "G20=" + gg.Key + gg.Key + "," + gg.Count() + gg.Any() + gg.First() + gg.Last() + gg.ElementAt(2) + gg.Contains(9) + gg.Contains(4)
                 + "," + gg.Aggregate((a, b) => a * 10 + b) + "," + gg.Min() + gg.Max() + gg.Sum() + "," + gg.Average() + "," + gg.ToArray().Length + gg.ToList().Count
                 + "," + twice + gg.Count() + gg.Count()
                 + ",[" + string.Join(",", gg.Where(x => x > 100)) + string.Concat(gg.Where(x => x > 100)) + "]"
                 + "[" + string.Join(",", words.GroupBy(w => w == null).First(g => g.Key)) + "]"
                 + string.Join("+", words.GroupBy(w => w == null ? 0 : w.Length % 2).First()) + ";";

            // G21：要素を匿名型・タプル・普通のクラスにする。グループのリストを匿名型に持つ。普通のクラスのメソッドに渡す
            var withAnon = items.GroupBy(i => i.kind, i => new { i.name, Double = i.score * 2 })
                .Select(g => (int)g.Key + ":" + g.Sum(e => e.Double) + g.OrderByDescending(e => e.Double).First().name).ToList();
            var withTuple = items.GroupBy(i => i.score % 2, i => (i.name, i.score)).Select(g => g.Key + ":" + string.Join("", g.Select(t => t.name)) + g.Max(t => t.score)).ToList();
            var withClass = nums.GroupBy(x => x % 3, x => new GbTeam("n" + x)).Select(g => g.Key + g.Last().label).ToList();
            var held = nums.GroupBy(x => x % 2).Select(g => new { g.Key, Items = g.ToList(), Top = g.Max() }).ToList();
            s += "G21=" + string.Join(",", withAnon) + "|" + string.Join(",", withTuple) + "|" + string.Join(",", withClass)
                 + "|" + held[0].Key + held[0].Items.Count + held[0].Items[2] + held[1].Top + "|" + GbUtil.KeyPlusCount(nums.GroupBy(x => x).First()) + ";";

            // G22：前後の LINQ（SelectMany → GroupBy・Concat → GroupBy・GroupBy → SelectMany・OrderBy → Take・GroupBy を 2 段・グループの中で何段も）
            int[] tail = { 8, 4 };
            s += "G22=" + string.Join(",", items.SelectMany(i => new[] { i.score, i.score % 3 }).GroupBy(x => x).Select(g => g.Key + "x" + g.Count()))
                 + "," + string.Join(",", nums.Concat(tail).GroupBy(x => x % 4).Select(g => g.Key + "=" + g.Count()))
                 + "," + string.Join("", nums.GroupBy(x => x % 3).SelectMany(g => g.Where(x => x > 2)))
                 + "," + string.Join(",", nums.GroupBy(x => x).OrderBy(g => -g.Count()).ThenBy(g => g.Key).Take(2).Select(g => g.Key))
                 + "," + string.Join(",", nums.GroupBy(x => x).Select(g => new { g.Key, N = g.Count() }).GroupBy(x => x.N).Select(g => g.Key + ":" + string.Join("", g.Select(x => x.Key))))
                 + "," + string.Join(",", nums.GroupBy(x => x % 2).Select(g => g.Where(x => x > 2).Select(x => x * 2).Sum() + "/" + g.GroupBy(x => x % 3).Count() + "/" + nums.Where(y => y > g.Key).Count()))
                 + "," + nums.GroupBy(x => x % 3).ToDictionary(g => g.Key, g => g.Sum())[2] + ";";

            // G23：Dictionary（キー・値・両方でまとめる。要素が KeyValuePair のグループ。Dictionary → GroupBy → ToDictionary）
            var scores = new Dictionary<string, int> { { "ann", 3 }, { "bob", 5 }, { "cy", 3 }, { "dee", 8 }, { "ed", 5 } };
            var round = scores.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Count());
            s += "G23=" + string.Join(",", scores.GroupBy(kv => kv.Value, kv => kv.Key).OrderBy(g => g.Key).Select(g => g.Key + ":" + string.Join("", g.OrderBy(k => k))))
                 + "|" + string.Join(",", scores.GroupBy(kv => kv.Key.Length).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Sum(kv => kv.Value) + g.OrderBy(kv => kv.Key).First().Key))
                 + "|" + string.Join(",", scores.GroupBy(kv => kv.Key.Length + kv.Value).OrderBy(g => g.Key).Select(g => g.Key + "" + g.Count()))
                 + "|" + round.Count + round[3] + round[5] + round[8] + string.Join("", round.Keys.OrderBy(k => k)) + ";";

            // G24：ジェネリック（TKey・TElement が違う呼び出し）・デリゲートの変数・static メソッド・インタフェースの要素とキー
            System.Func<int, int> mod = x => x % 3;
            var sq1 = new GbSq(2);
            var shapes = new List<IGbShape> { sq1, new GbSq(5), sq1, new GbSq(2) };
            s += "G24=" + GbUtil.CountBy(nums, x => x % 2) + GbUtil.CountBy(words, w => w == null ? "-" : w.Substring(0, 1)) + GbUtil.CountBy(items, i => i.kind)
                 + "," + nums.GroupBy(mod).Count() + nums.GroupBy(GbUtil.Mod3).Count() + nums.GroupBy(GbUtil.Mod3, mod).First().Sum()
                 + "," + shapes.GroupBy(sh => sh).Count() + shapes.GroupBy(sh => sh.Size()).Count()
                 + "," + string.Join("/", nums.GroupBy(x => x % 2, x => (IGbShape)new GbSq(x)).Select(g => g.Sum(sh => sh.Size()))) + ";";

            // G25：演算子・変換（#105）。参照のキーは、ユーザー定義の == ではなく参照で比べる。null の要素を null のキーにまとめる
            var m1 = new GbMoney(3);
            var m3 = new GbMoney(4);
            var ms = new List<GbMoney> { m1, new GbMoney(3), m1, m3 };
            s += "G25=" + ms.GroupBy(m => m).Count() + "," + ms.GroupBy(m => (int)m).Count()
                 + "," + string.Join(",", ms.GroupBy(m => m.v, m => m + m).Select(g => g.Key + ":" + g.Sum(x => (int)x)))
                 + "," + ms.GroupBy(m => m + m3).Count() + (ms[0] == ms[1])
                 + "," + string.Join(",", words.GroupBy(w => w).Where(g => g.Key == null).Select(g => g.Count() + "/" + g.Where(w => w == null).Count())) + ";";

            // G26（#171）：グループがつなぎの外に出ない GroupBy は、グループの入れ物を呼び出し箇所ごとに使い回す。
            // 同じ箇所を、グループの数・中身を変えて何度も呼ぶ（前の呼び出しの中身が残らない）。外に出すもの（ToList）は前のまま変わらない
            var kept1 = PoolKeep(new List<int> { 1, 2, 3 });
            var kept2 = PoolKeep(new List<int> { 7 });
            s += "G26=" + PoolA(new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 }) + "," + PoolA(new List<int> { 4, 8 }) + "," + PoolA(new List<int> { 3, 7, 11, 2, 1 })
                 + "," + PoolB(new List<string> { "ab", "ac", null, null, "b" }) + "," + PoolB(new List<string> { "x", "y" })
                 + "," + PoolC(new List<int> { 1, 2, 3, 4, 5, 6 }) + "," + PoolC(new List<int> { 3, 6 })
                 + "," + string.Join("", kept1.Select(g => g.Sum())) + "/" + kept2.Count;
            return s;
        }

        private string PoolA(List<int> xs) =>
            string.Join("|", xs.GroupBy(x => x % 4).Where(g => g.Count() > 0).OrderBy(g => -g.Key).Select(g => g.Key + ":" + string.Join("", g) + "/" + g.Count()));

        private int PoolB(List<string> ws) => ws.GroupBy(w => w == null ? null : w.Substring(0, 1)).Count(g => g.Count() > 1);

        private string PoolC(List<int> xs) => string.Join(",", xs.GroupBy(x => x % 3, x => x * 2).Select(g => new { g.Key, S = g.Sum() }).Select(a => a.Key + "=" + a.S));

        private List<IGrouping<int, int>> PoolKeep(List<int> xs) => xs.GroupBy(x => 0).ToList();

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#140 GroupBy\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#140 GroupBy"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #140 GroupBy " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
