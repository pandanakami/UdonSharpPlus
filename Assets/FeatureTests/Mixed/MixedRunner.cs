using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Mixed
{
    // #150：0.1.5 の新機能（匿名型・タプル・演算子・LINQ・GroupBy・匿名メソッド）と、
    // 既存の機能（普通のクラス・インタフェース・ジェネリクス・デリゲート）を、1 つの流れで混ぜる

    public interface IMxScore { int Score(); }

    // 演算子（#105）を持つ普通のクラス
    public class MxVec
    {
        public int x;
        public int y;
        public MxVec(int x, int y) { this.x = x; this.y = y; }
        public static MxVec operator +(MxVec a, MxVec b) => new MxVec(a.x + b.x, a.y + b.y);
        public static MxVec operator *(MxVec a, int k) => new MxVec(a.x * k, a.y * k);
        public static implicit operator int(MxVec v) => v.x * 10 + v.y;
    }

    public class MxPlayer : IMxScore
    {
        public string name;
        public string team;
        public MxVec pos;
        public int hp;
        public MxPlayer(string name, string team, MxVec pos, int hp) { this.name = name; this.team = team; this.pos = pos; this.hp = hp; }
        public int Score() => hp + pos;
    }

    public class MxBonus : IMxScore
    {
        public int v;
        public MxBonus(int v) { this.v = v; }
        public int Score() => v * 100;
    }

    public static class MxUtil
    {
        public static T Pick<T>(List<T> xs, int i) => xs[i];
        public static string Tag<T>(T x) => "<" + x + ">";
        public static (int min, int max) Range(List<int> xs) => (xs.Min(), xs.Max());
    }

    // 0.1.5 で直したもの（#135・#136・#105・#118・#140）
    public class MxKw
    {
        public int v;
        public MxKw(int @class, int @event) { v = @class * 10 + @event; }
        public int Sum(int @class) => v + @class;
    }

    public class MxHolder
    {
        public global::FeatureTests.Mixed.IMxScore s;
        public int[] n = { 4, 5 };
    }

    public interface IMxSize { int Size(); }
    public interface IMxLabel { string Size(); }
    public class MxBox : IMxSize { public int Size() => 7; }
    public class MxTag : IMxLabel { public string Size() => "t"; }

    // #150 のパネル
    public class MixedRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "M1={ Team = red, N = 2, Hp = 80, Far = 40 },{ Team = blue, N = 2, Hp = 60, Far = 91 },{ Team = green, N = 1, Hp = 10, Far = 82 }|red80|bobevedananncid|12/51/40/82/91;M2=444,392,<bob><4>,10-50,0bob 1eve 2cid 3ann;M3=red1,none2,blue1,00,red1,150,5;M4=15,20025,11,7t,14";

        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var players = new List<MxPlayer>
            {
                new MxPlayer("ann", "red", new MxVec(1, 2), 30),
                new MxPlayer("bob", "blue", new MxVec(3, 1), 20),
                new MxPlayer("cid", "red", new MxVec(0, 0), 50),
                new MxPlayer("dan", "green", new MxVec(2, 2), 10),
                new MxPlayer("eve", "blue", new MxVec(1, 1), 40),
            };

            // M1：普通のクラスのリスト → 演算子 → 添字付きの Select → 匿名型 → GroupBy → 匿名型で集計 → タプル → OrderBy・SelectMany → string.Join
            var moved = players.Select((p, i) => new { p.name, p.team, Pos = p.pos + new MxVec(i, 0) * 2, p.hp }).ToList();
            var byTeam = moved.GroupBy(m => m.team)
                .Select(g => new { Team = g.Key, N = g.Count(), Hp = g.Sum(m => m.hp), Far = g.Max(m => (int)m.Pos) })
                .ToList();
            var best = byTeam.OrderByDescending(t => t.Hp).ThenBy(t => t.Team).Select(t => (t.Team, t.Hp)).First();
            var names = byTeam.OrderBy(t => t.Team).SelectMany(t => moved.Where(m => m.team == t.Team).Select(m => m.name)).ToList();
            s += "M1=" + string.Join(",", byTeam.Select(x => x.ToString())) + "|" + best.Item1 + best.Item2 + "|" + string.Join("", names)
                 + "|" + string.Join("/", moved.Select(m => (int)m.Pos)) + ";";

            // M2：LINQ に直接書いたラムダ・Func の変数・匿名メソッド、インタフェース、ジェネリックメソッド、タプルを返すメソッド
            System.Func<MxPlayer, bool> alive = p => p.hp > 15;
            int a1 = players.Count(alive);
            int a2 = players.Count(p => p.hp > 15);
            int a3 = players.Count(delegate (MxPlayer p) { return p.hp > 15; });
            var scores = new List<IMxScore> { players[0], new MxBonus(3), players[2] };
            int sc = scores.Sum(x => x.Score());
            string tags = MxUtil.Tag(MxUtil.Pick(players, 1).name) + MxUtil.Tag(MxUtil.Pick(new List<int> { 4, 5 }, 0));
            var (mn, mx) = MxUtil.Range(players.Select(p => p.hp).ToList());
            var ranked = players.Where(alive).OrderBy(p => -p.Score()).Select((p, i) => (i, p.name)).ToList();
            s += "M2=" + a1 + a2 + a3 + "," + sc + "," + tags + "," + mn + "-" + mx + "," + string.Join(" ", ranked.Select(r => r.Item1 + r.Item2)) + ";";

            // M3：null を含む・空・1 つ・全部同じキー・全部違うキー
            var withNull = new List<MxPlayer> { players[0], null, players[1], null };
            var teamOrNone = withNull.GroupBy(p => p == null ? "none" : p.team).Select(g => g.Key + g.Count()).ToList();
            var empty = new List<MxPlayer>();
            var emptyGroups = empty.GroupBy(p => p.team).Select(g => new { g.Key }).ToList();
            var single = players.Take(1).GroupBy(p => p.team).Select(g => (g.Key, g.Count())).ToList();
            int same = players.GroupBy(p => 0).Select(g => g.Sum(p => p.hp)).First();
            int distinct = players.GroupBy(p => p.name).Count();
            s += "M3=" + string.Join(",", teamOrNone) + "," + emptyGroups.Count + empty.Select(p => (p.name, p.hp)).Count() + "," + single[0].Item1 + single[0].Item2
                 + "," + same + "," + distinct + ";";

            // M4：0.1.5 で直したもの（@ のキーワード名の引数・global:: の型・配列の初期化子の省略形・TryGetValue の文と out _・
            // 戻り値の型だけ違う同名のインタフェースのメソッド・複数行に分けた普通のクラスのメソッドの呼び出し）
            var kw = new MxKw(1, 2);
            int kv = kw.Sum(3);
            var h = new MxHolder();
            h.s = new MxBonus(2);
            var dict = new Dictionary<string, int> { { "a", 1 } };
            dict.TryGetValue("a", out var tv);
            dict.TryGetValue("zz", out _);
            int tv2;
            dict.TryGetValue("a", out tv2);
            IMxSize sz = new MxBox();
            IMxLabel lb = new MxTag();
            int multi = kw.Sum(
                tv
                + tv2);
            s += "M4=" + kv + "," + h.s.Score() + h.n.Length + h.n[1] + "," + tv + tv2 + "," + sz.Size() + lb.Size() + "," + multi;
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#150 Mixed\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#150 Mixed"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #150 Mixed " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
