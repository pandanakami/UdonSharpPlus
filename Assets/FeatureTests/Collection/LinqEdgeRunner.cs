using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #142〜#146 の追加テスト（境界・評価の回数・ほかの機能との組み合わせ）。普通のクラスの中で使う
    public class LeCalc
    {
        public int bias = 1;

        public string Run(List<int> nums)
        {
            string s = "";
            s += nums.Select((x, i) => x * i + bias).Sum() + ",";
            s += nums.SelectMany(x => Enumerable.Range(0, x)).Count() + ",";
            s += nums.Where(x => x > 1).Concat(nums.Take(1)).Aggregate(0, (a, x) => a * 2 + x) + ",";
            s += string.Join("/", nums.Select(x => x + bias)) + ",";
            s += nums.TakeWhile(x => x < 3).Append(9).Prepend(8).ElementAt(1) + "," + nums.Where(x => x == 2).Single();
            return s;
        }
    }

    // ジェネリックメソッドの中で使う
    public static class LeGen
    {
        public static string Describe<T>(List<T> list)
        {
            return list.Select((x, i) => i).Sum() + ":" + list.Concat(list).Count() + ":" + list.SelectMany(x => Enumerable.Repeat(x, 2)).Count() + ":" + list.Skip(1).Append(list[0]).Count();
        }
    }

    public class LinqEdgeRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "X1=0123,2345,0123,8,01;X2=46,012;X3=31,52;X4=01,66,00,01,61,06;X5=101,102,303;X6=a0,b1,c2|b|1|b;XC=012,7,45,642;Y1=x1x2z3,5,3;Y4=2px,2qx,1rz;Y5=00,12,12,True2,34;Y6=123,13,001012;Y7=x1,x2,z3,3,0;YC=32,99,6,36,3,12;Z2=0,False,True,False,True,True,6;Z3=7,5,-19,b<a<c,123456,21.5,21,5;Z4=0,7,0,77,1,2,61,6,c--a,a--c-a,-a--c-,33;Z6=3,11;ZC=32,34563,6,4,10;ZA=321,6,9,8,2;G1=0,5,0,x;G2=2147483647,2147483647,-6,-2147483647;G3=122,667;G4=--,True3,True1;G5=False0,True0,True0;G6=9,3,3,9;G7=00,01,31,True1;GC1=5555,03,2,3;GC2=001012,112;GC3=321,812007,24,4,13;GC4=44,n23;GC5=1+2+3,abab,[][];J1=[][7][][v7];J2=a,,c|a,,c|ac|[];J4=8,10,1,2;J5=1,20000000000|0.5,2|0.25,10000000000|True,False|x,y|xy|1.5,3,4.5,6,7.5,9;J6=123456|b, a, c|bac|b~\ta~\tc;J7=3,1,2|1,2,3,4,5,6|1,2|b,a,c;JC=6401|0b,1a,2c|{ A = 1 },{ A = 2 },{ A = 3 }|<1><2>|101,102|{1},{2}|/23//4/;P=11,6,15,2/3/4,1,2|3:6:6:3|15:12:12:6";

        public Text display;

        private int _c;
        private int _d;
        private List<int> _a;
        private List<int> _b;

        private int Next() => ++_c;
        private List<int> GetA() { _c++; return _a; }
        private List<int> GetB() { _d++; return _b; }
        private List<int> ItemsOf(LmBox b) => b.items;
        private string Fmt(int x) => "{" + x + "}";

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            var nums = new List<int> { 1, 2, 3, 4, 5, 6 };
            int[] arr = { 3, 1, 2 };
            var words = new List<string> { "b", "a", "c" };
            var empty = new List<int>();
            var one = new List<int> { 7 };
            var boxes = new List<LmBox>
            {
                new LmBox("e", new List<int>(), new string[0]),
                new LmBox("x", new List<int> { 1, 2 }, new[] { "p", "q" }),
                new LmBox("y", new List<int>(), new string[0]),
                new LmBox("z", new List<int> { 3 }, new[] { "r" }),
                new LmBox("w", new List<int>(), new string[0]),
            };
            var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, { "c", 3 } };
            _a = new List<int> { 4, 5 };
            _b = new List<int> { 6 };

            // ---- #142：添字付きのラムダ ----
            // X1：添字は、その段に来た要素の順番（Skip・Where・Take の前後）
            s += "X1=" + string.Join("", nums.Skip(2).Select((x, i) => i)) + "," + string.Join("", nums.Select((x, i) => i).Skip(2)) + "," + string.Join("", nums.Where(x => x > 2).Select((x, i) => i)) + ","
                 + nums.Take(3).Select((x, i) => x * i).Sum() + "," + string.Join("", nums.Select((x, i) => i).Take(2)) + ";";
            // X2：Where の添字は、通らなかった要素も数える
            s += "X2=" + string.Join("", nums.Where((x, i) => i >= 2 && x % 2 == 0)) + "," + string.Join("", nums.Where((x, i) => i % 2 == 1).Select((x, i) => i)) + ";";
            // X3：添字付きの段を続ける（段ごとに別に数える）
            s += "X3=" + string.Join(",", nums.Where((x, i) => i % 2 == 0).Select((x, i) => x * 10 + i).Where((x, i) => i > 0)) + ";";
            // X4：TakeWhile・SkipWhile の境界と、条件を呼ぶ回数
            _c = 0; int x4a = nums.TakeWhile((x, i) => { _c++; return i < 0; }).Count(); int c4a = _c;
            _c = 0; int x4b = nums.TakeWhile((x, i) => { _c++; return i < 99; }).Count(); int c4b = _c;
            _c = 0; int x4c = empty.TakeWhile((x, i) => { _c++; return true; }).Count(); int c4c = _c;
            _c = 0; int x4d = one.SkipWhile((x, i) => { _c++; return i < 1; }).Count(); int c4d = _c;
            _c = 0; int x4e = nums.SkipWhile((x, i) => { _c++; return false; }).Count(); int c4e = _c;
            _c = 0; int x4f = nums.SkipWhile((x, i) => { _c++; return i < 99; }).Count(); int c4f = _c;
            s += "X4=" + x4a + c4a + "," + x4b + c4b + "," + x4c + c4c + "," + x4d + c4d + "," + x4e + c4e + "," + x4f + c4f + ";";
            // X5：SelectMany の添字は外側の要素の順番（内側が空でも進む）
            s += "X5=" + string.Join(",", boxes.SelectMany((b, i) => b.items.Select(x => x + i * 100))) + ";";
            // X6：Dictionary の要素と添字
            s += "X6=" + string.Join(",", dict.Select((kv, i) => kv.Key + i)) + "|" + string.Join(",", dict.Where((kv, i) => i == 1).Select(kv => kv.Key)) + "|" + dict.TakeWhile((kv, i) => i < 1).Count() + "|" + dict.SkipWhile((kv, i) => i < 1).First().Key + ";";
            // XC：ほかの段との組み合わせ・入れ子・匿名型
            s += "XC=" + string.Join("", boxes.SelectMany(b => b.items).Select((x, i) => i)) + "," + arr.Reverse().Select((x, i) => x * i).Sum() + "," + nums.Select((x, i) => arr.Select((y, j) => y * j).Sum() + i).Sum() + ","
                 + string.Join("", nums.Select((x, i) => new { x, i }).Where(a => a.i % 2 == 1).OrderByDescending(a => a.x).Select(a => a.x)) + ";";

            // ---- #143：SelectMany ----
            // Y1：空の内側（先頭・途中・末尾）。集まりのラムダは外側の要素ごとに 1 回、結果のラムダは出力ごとに 1 回
            _c = 0; _d = 0;
            string y1 = string.Join("", boxes.SelectMany(b => { _c++; return b.items; }, (b, x) => { _d++; return b.name + x; }));
            s += "Y1=" + y1 + "," + _c + "," + _d + ";";
            // Y4：結果のラムダで、外側と内側の両方を使う
            s += "Y4=" + string.Join(",", boxes.SelectMany(b => b.tags, (b, t) => b.items.Count + t + b.name)) + ";";
            // Y5：途中で止めたときの、集まりのラムダを呼ぶ回数
            _c = 0; int y5a = boxes.SelectMany(b => { _c++; return b.items; }).Take(0).Count(); int c5a = _c;
            _c = 0; int y5b = boxes.SelectMany(b => { _c++; return b.items; }).Take(1).Sum(); int c5b = _c;
            _c = 0; int y5c = boxes.SelectMany(b => { _c++; return b.items; }).First(); int c5c = _c;
            _c = 0; bool y5d = boxes.SelectMany(b => { _c++; return b.items; }).Any(); int c5d = _c;
            _c = 0; int y5e = boxes.SelectMany(b => { _c++; return b.items; }).Take(3).Count(); int c5e = _c;
            s += "Y5=" + y5a + c5a + "," + y5b + c5b + "," + y5c + c5c + "," + y5d + c5d + "," + y5e + c5e + ";";
            // Y6：3 段（List → List → 配列、内側に LINQ の結果）
            var deep = new List<List<int[]>> { new List<int[]> { new[] { 1, 2 }, new int[0] }, new List<int[]>(), new List<int[]> { new[] { 3 } } };
            s += "Y6=" + string.Join("", deep.SelectMany(l => l).SelectMany(a => a)) + "," + string.Join("", deep.SelectMany(l => l.SelectMany(a => a.Where(v => v != 2)))) + ","
                 + string.Join("", boxes.SelectMany(b => b.items.SelectMany(i => Enumerable.Range(0, i)))) + ";";
            // Y7：匿名型を外側・内側の要素に使う。空の配列
            var rows = boxes.Select(b => new { b.name, b.items }).ToList();
            s += "Y7=" + string.Join(",", rows.SelectMany(r => r.items, (r, i) => new { r.name, i }).Select(p => p.name + p.i)) + "," + boxes.SelectMany(b => b.tags).Count() + "," + new[] { new int[0] }.SelectMany(a => a).Count() + ";";
            // YC：内側の LINQ・あとの段・デリゲートの変数・クロージャ・メソッド名・入れ子の SelectMany
            Func<LmBox, List<int>> sel = b => b.items;
            int bonus = 10;
            s += "YC=" + string.Join("", boxes.SelectMany(b => b.items.Where(i => i > 1).OrderByDescending(i => i)).Reverse()) + "," + boxes.SelectMany(b => b.items.TakeWhile(i => i < 2)).Aggregate(100, (a, x) => a - x) + ","
                 + boxes.SelectMany(sel).Sum() + "," + boxes.SelectMany(b => b.items.Select(i => i + bonus)).Sum() + "," + boxes.SelectMany(ItemsOf).Count() + ","
                 + boxes.SelectMany(b => boxes.SelectMany(c => c.items).Where(i => i > b.items.Count)).Count() + ";";

            // ---- #144：足したメソッド ----
            // Z2：OrDefault の既定値・負の添字
            var flags = new List<bool> { true };
            s += "Z2=" + nums.ElementAtOrDefault(-1) + "," + flags.ElementAtOrDefault(5) + "," + (words.ElementAtOrDefault(-2) == null) + "," + flags.SingleOrDefault(f => !f) + "," + (words.SingleOrDefault(w => w == "zz") == null) + ","
                 + (boxes.SingleOrDefault(b => b.name == "none") == null) + "," + nums.ElementAtOrDefault(5) + ";";
            // Z3：Aggregate（1 要素・空で最初の値あり・順番・型の違う集計・ラムダを呼ぶ回数）
            _c = 0;
            s += "Z3=" + one.Aggregate((a, b) => a * 100 + b) + "," + empty.Aggregate(5, (a, x) => a + x) + "," + nums.Aggregate((a, b) => a - b) + "," + words.Aggregate((a, b) => a + "<" + b) + "," + nums.Aggregate("", (a, x) => a + x) + ","
                 + nums.Aggregate(0.5f, (a, x) => a + x) + "," + nums.Aggregate((a, b) => { _c++; return a + b; }) + "," + _c + ";";
            // Z4：Reverse・Concat・Append・Prepend（空・1 要素・同じ列・null の要素・元の列は変わらない）
            var wn = new List<string> { "a", null, "c" };
            string[] none = new string[0];
            s += "Z4=" + none.Reverse().Count() + "," + string.Join("", one.Where(x => true).Reverse()) + "," + empty.Concat(empty).Count() + "," + string.Join("", one.Concat(one)) + "," + string.Join("", empty.Append(1)) + "," + string.Join("", empty.Prepend(2)) + ","
                 + string.Join("", nums.Concat(nums).Skip(5).Take(2)) + "," + nums.Count + "," + string.Join("-", wn.Where(w => true).Reverse()) + "," + string.Join("-", wn.Concat(wn).Take(4)) + "," + string.Join("-", wn.Append(null).Prepend(null)) + "," + arr.Length + arr[0] + ";";
            // Z6：Concat の左右の式は、1 回ずつ評価する
            _c = 0; _d = 0;
            s += "Z6=" + GetA().Concat(GetB()).Count() + "," + _c + _d + ";";
            // ZC：新しい段をつなぐ・Range / Repeat から・入れ子
            s += "ZC=" + string.Join("", boxes.SelectMany(b => b.items).Reverse().Take(2)) + "," + nums.Concat(arr).Where(x => x > 2).Aggregate((a, b) => a * 10 + b) + "," + Enumerable.Range(1, 9).TakeWhile(x => x < 4).Aggregate((a, b) => a + b) + "," + Enumerable.Repeat(4, 3).ElementAt(2) + ","
                 + nums.Select(x => nums.Where(y => y <= x).Aggregate((a, b) => a + b)).Single(v => v == 10) + ";";
            // ZA：匿名型のリスト
            var arows = nums.Take(3).Select(x => new { A = x }).ToList();
            s += "ZA=" + string.Join("", arows.Where(r => true).Reverse().Select(r => r.A)) + "," + arows.Concat(arows).Count() + "," + arows.Append(new { A = 9 }).Last().A + "," + arows.Prepend(new { A = 8 }).First().A + "," + arows.Single(r => r.A == 2).A + ";";

            // ---- #145：Enumerable.Range・Repeat・Empty ----
            s += "G1=" + Enumerable.Range(5, 0).Count() + "," + string.Join("", Enumerable.Range(5, 1)) + "," + Enumerable.Repeat("x", 0).Count() + "," + string.Join("", Enumerable.Repeat("x", 1)) + ";";
            // G2：int の範囲の端
            s += "G2=" + Enumerable.Range(int.MaxValue - 2, 3).Last() + "," + Enumerable.Range(int.MaxValue, 1).First() + "," + Enumerable.Range(-3, 3).Sum() + "," + Enumerable.Range(int.MinValue, 2).Last() + ";";
            // G3：引数は左から 1 回ずつ評価する
            _c = 0;
            var r12 = Enumerable.Range(Next(), Next());
            string g3 = string.Join("", r12) + _c;
            _c = 5;
            string g3b = string.Join("", Enumerable.Repeat(Next(), Next() - 5)) + _c;
            s += "G3=" + g3 + "," + g3b + ";";
            // G4：Repeat の値（null・普通のクラス・匿名型は同じ参照が並ぶ）
            string nstr = null;
            var rep = Enumerable.Repeat(boxes[1], 3).ToList();
            var anonRep = Enumerable.Repeat(new { K = 1 }, 2).ToList();
            s += "G4=" + string.Join("-", Enumerable.Repeat(nstr, 3)) + "," + (rep[0] == rep[2]) + rep.Count + "," + (anonRep[0] == anonRep[1]) + anonRep[1].K + ";";
            // G5：Empty
            s += "G5=" + Enumerable.Empty<int>().Any() + Enumerable.Empty<int>().FirstOrDefault() + "," + (Enumerable.Empty<string>().FirstOrDefault() == null) + Enumerable.Empty<string>().ToList().Count + ","
                 + (Enumerable.Empty<LmBox>().FirstOrDefault() == null) + Enumerable.Empty<LmBox>().ToArray().Length + ";";
            // G6：変数に入れた列を何回も使う
            var saved = Enumerable.Range(2, 3);
            int g6 = 0;
            foreach (int x in saved)
                g6 += x;
            s += "G6=" + g6 + "," + saved.Count() + "," + saved.ToList().Count + "," + saved.Sum() + ";";
            // G7：途中で止めたときの、ラムダを呼ぶ回数
            _c = 0; int g7a = Enumerable.Range(0, 5).Select(x => { _c++; return x; }).Take(0).Count(); int c7a = _c;
            _c = 0; int g7b = Enumerable.Range(0, 5).Select(x => { _c++; return x; }).Take(1).Sum(); int c7b = _c;
            _c = 0; int g7c = Enumerable.Repeat(3, 5).Select(x => { _c++; return x; }).First(); int c7c = _c;
            _c = 0; bool g7d = Enumerable.Range(0, 5).Select(x => { _c++; return x; }).Any(); int c7d = _c;
            s += "G7=" + g7a + c7a + "," + g7b + c7b + "," + g7c + c7c + "," + g7d + c7d + ";";
            // GC：ほかの段との組み合わせ
            s += "GC1=" + string.Join("", Enumerable.Range(5, 4).Select((x, i) => x - i)) + "," + string.Join("", Enumerable.Range(0, 6).Where((x, i) => i % 3 == 0)) + "," + Enumerable.Repeat(1, 5).TakeWhile((x, i) => i < 2).Count() + "," + Enumerable.Range(0, 5).SkipWhile((x, i) => i < 3).First() + ";";
            s += "GC2=" + string.Join("", Enumerable.Range(0, 4).SelectMany(i => Enumerable.Range(0, i))) + "," + string.Join("", Enumerable.Range(1, 3).SelectMany(i => Enumerable.Repeat(i, 3 - i))) + ";";
            s += "GC3=" + string.Join("", Enumerable.Range(1, 3).Reverse()) + "," + string.Join("", Enumerable.Range(1, 2).Concat(Enumerable.Repeat(0, 2)).Append(7).Prepend(8)) + "," + Enumerable.Range(1, 4).Aggregate((a, b) => a * b) + "," + Enumerable.Range(4, 1).Single() + "," + Enumerable.Range(10, 5).ElementAt(3) + ";";
            var ga = Enumerable.Range(1, 4).Select(i => new { Id = i, Sq = i * i }).ToList();
            var gd = Enumerable.Range(1, 3).Select(i => new { Id = i, Name = "n" + i }).ToDictionary(a => a.Id, a => a.Name);
            s += "GC4=" + ga.Count + ga.OrderByDescending(a => a.Sq).First().Id + "," + gd[2] + gd.Count + ";";
            s += "GC5=" + string.Join("+", Enumerable.Range(1, 3)) + "," + string.Concat(Enumerable.Repeat("ab", 2)) + ",[" + string.Join(",", Enumerable.Empty<string>()) + "][" + string.Concat(Enumerable.Empty<int>()) + "];";

            // ---- #146：そのまま string.Join / string.Concat に渡す ----
            // J1：空・1 要素
            s += "J1=[" + string.Join(",", empty) + "][" + string.Join(",", one) + "][" + string.Concat(empty.Select(x => "v" + x)) + "][" + string.Concat(one.Select(x => "v" + x)) + "];";
            // J2：null の要素は空の文字列
            var sn = new List<string> { "a", null, "c" };
            s += "J2=" + string.Join(",", sn) + "|" + string.Join(",", sn.Select(x => x)) + "|" + string.Concat(sn) + "|[" + string.Concat(sn.Where(x => x == null)) + "];";
            // J4：元の列の式は 1 回、ラムダは要素ごとに 1 回
            _c = 0; _d = 0;
            string j4 = string.Join(",", GetA().Select(x => { _d++; return x * 2; }));
            s += "J4=" + j4 + "," + _c + "," + _d + ";";
            // J5：要素の型
            var longs = new List<long> { 1L, 20000000000L };
            var floats = new List<float> { 0.5f, 2f };
            var doubles = new List<double> { 0.25, 1e10 };
            var bools = new List<bool> { true, false };
            var chars = new List<char> { 'x', 'y' };
            s += "J5=" + string.Join(",", longs) + "|" + string.Join(",", floats) + "|" + string.Join(",", doubles) + "|" + string.Join(",", bools) + "|" + string.Join(",", chars) + "|" + string.Concat(chars.Select(c => c)) + "|" + string.Join(",", nums.Select(x => x * 1.5)) + ";";
            // J6：区切りの文字列（空・複数の文字・null・特殊な文字）
            string nsep = null;
            s += "J6=" + string.Join("", nums) + "|" + string.Join(", ", words) + "|" + string.Join(nsep, words) + "|" + string.Join("~\t", words.Select(w => w)) + ";";
            // J7：配列・List・LINQ の結果・Range
            s += "J7=" + string.Join(",", arr) + "|" + string.Join(",", nums.ToArray()) + "|" + string.Join(",", Enumerable.Range(1, 2)) + "|" + string.Join(",", words.ToArray()) + ";";
            // JC：つないだ結果・添字付き・匿名型・デリゲートの変数・クロージャ・メソッド名・入れ子
            Func<int, string> fmt = x => "<" + x + ">";
            int off = 100;
            s += "JC=" + string.Join("", boxes.SelectMany(b => b.items).Where(x => x > 1).Select(x => x * 2).Reverse().Concat(Enumerable.Range(0, 2))) + "|" + string.Join(",", words.Select((w, i) => i + w)) + "|" + string.Join(",", arows.Select(r => r.ToString())) + "|"
                 + string.Join("", nums.Take(2).Select(fmt)) + "|" + string.Join(",", nums.Take(2).Select(x => x + off)) + "|" + string.Join(",", nums.Take(2).Select(Fmt)) + "|" + string.Join("/", boxes.Select(b => string.Join("", b.items.Select(i => i + 1)))) + ";";

            // ---- 普通のクラス・ジェネリックメソッドの中 ----
            s += "P=" + new LeCalc().Run(nums.Take(3).ToList()) + "|" + LeGen.Describe(words) + "|" + LeGen.Describe(nums);
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#137 LINQ edge\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#137 LINQ edge"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #137 linq edge " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
