using System.Collections;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    public enum SwKind { None, Small, Big, Huge }

    // #198：普通のクラスの中のパターンの switch
    public class SwPlain
    {
        public int bonus;
        public SwPlain(int bonus) { this.bonus = bonus; }

        // #202：普通のクラスの中の switch 式
        public string Tag(int n) => n switch { > 0 => "p" + bonus, _ => "z" };

        public string Grade(int score)
        {
            switch (score + bonus)
            {
                case >= 90: return "A";
                case >= 70 and < 90: return "B";
                case var s when s % 2 == 0: return "E" + s;
                default: return "C";
            }
        }
    }

    // #198 のパネル：パターンのラベル（when・関係・and / or / not・var・型）を含む switch 文。UdonSharp はこれを黙って読み飛ばしていた。
    // #202 の switch 式（E・F・G・H と、コルーチンの中）も、このパネルで確かめる
    public class SwitchRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() と Co() を普通の C# として実行して確かめたもの）
        private const string Expected = "P=gbdnull;R=neg,small,small,mid,even12,odd13;M=xxzyy;K=sbLnnLnnDnone;L=z0.0.7.h.H.H.7;N=teenlong-short-;S=lomidhi;C=ABE20C;E=negsmalleven8odd;F=sb-0132KN?;G=ab40c4123p7z;H=onemany;Y=As0s1w2w3;";

        public Text display;

        private int _evals;
        private string _co = "";

        private void Start()
        {
            StartCoroutine(Co());
        }

        public override void Interact()
        {
            _co = "";
            StartCoroutine(Co());
        }

        private int Next(int n) { _evals++; return n; }

        // ---- 文字列・when ----
        private string Plain(string k, int n)
        {
            switch (k)
            {
                case "b" when n > 0: return "g";
                case "b": return "b";
                case null: return "null";
                default: return "d";
            }
        }

        // ---- 関係・and / or / not・var と when・型の宣言（必ず合うので default になる）----
        private string Range(int n)
        {
            switch (n)
            {
                case < 0: return "neg";
                case 0 or 1: return "small";
                case >= 2 and < 10: return "mid";
                case var big when big % 2 == 0: return "even" + big;
                case int odd: return "odd" + odd;
            }
        }

        // ---- 1 つの case に複数のラベル・定数と when の混在・default がほかの case と同じところ ----
        private string Multi(int n, bool flag)
        {
            string s;
            switch (n)
            {
                case 1:
                case 2 when flag:
                    s = "x";
                    break;
                case 3:
                default:
                    s = "y";
                    break;
                case 2:
                    s = "z";
                    break;
            }
            return s;
        }

        // ---- enum・char・not ----
        private string Kinds(SwKind k, char c)
        {
            string s = "";
            switch (k)
            {
                case SwKind.Small or SwKind.Big when c == 'a': s += "sb"; break;
                case not SwKind.None: s += "nn"; break;
                default: s += "none"; break;
            }
            switch (c)
            {
                case >= 'a' and <= 'z': s += "L"; break;
                case >= '0' and <= '9': s += "D"; break;
            }
            return s;
        }

        // ---- break と continue（外のループ）・入れ子・式は 1 回だけ評価 ----
        private string Loop(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; i++)
            {
                switch (Next(a[i]))
                {
                    case < 0: continue;
                    case 0 when i == 0: s += "z0"; break;
                    case var v when v > 100:
                        switch (v % 3)
                        {
                            case 0 or 1 when v > 200: s += "H"; break;
                            default: s += "h"; break;
                        }
                        break;
                    default: s += a[i]; break;
                }
                s += ".";
            }
            return s;
        }

        // ---- どれにも合わない（default なし）・string の not null ----
        private string None(int n, string t)
        {
            string s = "-";
            switch (n)
            {
                case > 10 when n < 20: s = "teen"; break;
            }
            switch (t)
            {
                case not null when t.Length > 2: s += "long"; break;
                case not null: s += "short"; break;
            }
            return s;
        }

        private static string Static(float f)
        {
            switch (f)
            {
                case < 0.5f: return "lo";
                case >= 0.5f and < 1.5f: return "mid";
                default: return "hi";
            }
        }

        // ---- #202：switch 式 ----
        private string _fieldStr = "k";

        private string Expr(int n) => n switch
        {
            < 0 => "neg",
            0 or 1 => "small",
            var x when x % 2 == 0 => "even" + x,
            _ => "odd",
        };

        private string Kind2(SwKind k) => k switch { SwKind.Small => "s", SwKind.Big or SwKind.Huge => "b", _ => "-" };

        private int Nest(int a, int b) => a switch { 0 => b switch { 0 => 0, _ => 1 }, _ => b switch { > 5 => 3, _ => 2 } };

        private string FieldSw() => _fieldStr switch { "k" => "K", null => "N", _ => "?" };

        // 調べる式がメソッドの呼び出し（1 回だけ評価する）：return・宣言・代入・プロパティ
        private int Prop => Next(4) switch { 4 => 40, _ => 0 };

        private string Calls()
        {
            _evals = 0;
            var a = Next(1) switch { 1 => "a", _ => "-" };
            string b;
            b = Next(2) switch { 2 => "b", _ => "-" };
            int p = Prop;
            return a + b + p + Ret() + _evals;
        }

        private string Ret()
        {
            return Next(3) switch { < 3 => "lo", 3 => "c", _ => "hi" };
        }

        private static int Fl(float f) => f switch { < 0.5f => 1, >= 0.5f and < 2f => 2, _ => 3 };

        public string Exprs()
        {
            string r = "";
            r += "E=" + Expr(-1) + Expr(1) + Expr(8) + Expr(9) + ";";
            r += "F=" + Kind2(SwKind.Small) + Kind2(SwKind.Huge) + Kind2(SwKind.None) + Nest(0, 0) + Nest(0, 9) + Nest(1, 9) + Nest(1, 1) + FieldSw();
            _fieldStr = null;
            r += FieldSw();
            _fieldStr = "z";
            r += FieldSw() + ";";
            r += "G=" + Calls() + Fl(0.1f) + Fl(1f) + Fl(5f) + new SwPlain(7).Tag(1) + new SwPlain(7).Tag(0) + ";";
            System.Func<int, string> lam = x => x switch { 1 => "one", _ => "many" };
            r += "H=" + lam(1) + lam(5) + ";";
            return r;
        }

        public string Scenario()
        {
            string r = "";
            r += "P=" + Plain("b", 1) + Plain("b", 0) + Plain("a", 1) + Plain(null, 1) + ";";
            r += "R=" + Range(-3) + "," + Range(0) + "," + Range(1) + "," + Range(5) + "," + Range(12) + "," + Range(13) + ";";
            r += "M=" + Multi(1, false) + Multi(2, true) + Multi(2, false) + Multi(3, true) + Multi(9, true) + ";";
            r += "K=" + Kinds(SwKind.Small, 'a') + Kinds(SwKind.Big, 'b') + Kinds(SwKind.Huge, '5') + Kinds(SwKind.None, '#') + ";";
            _evals = 0;
            r += "L=" + Loop(new[] { 0, -1, 0, 7, 150, 301, 202 }) + _evals + ";";
            r += "N=" + None(15, "abc") + None(5, "ab") + None(25, null) + ";";
            r += "S=" + Static(0.2f) + Static(1f) + Static(2f) + ";";
            var plain = new SwPlain(5);
            r += "C=" + plain.Grade(90) + plain.Grade(70) + plain.Grade(15) + plain.Grade(20) + ";";
            return r;
        }

        // ---- コルーチンの中で、yield をまたぐパターンの switch ----
        private IEnumerator Co()
        {
            for (int i = 0; i < 4; i++)
            {
                _co += i switch { 0 => "A", _ => "" };   // #202：コルーチンの中の switch 式
                switch (i)
                {
                    case var k when k >= 2:
                        yield return null;
                        _co += "w" + k;
                        break;
                    case 0 or 1:
                        _co += "s" + i;
                        yield return null;
                        break;
                }
            }
            Run();
        }

        public void Run()
        {
            string actual = Scenario() + Exprs() + "Y=" + _co + ";";
            bool ok = actual == Expected;
            string text = "#198 switch patterns\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#198 switch patterns"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #198 switch patterns " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
