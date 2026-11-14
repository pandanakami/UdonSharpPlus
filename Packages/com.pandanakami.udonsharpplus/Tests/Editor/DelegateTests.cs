using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// ソースで宣言したデリゲート（Redmine #69 / #119）。書き換え後のソースと、エラーにする書き方を確かめる
    /// （呼び出しの動きそのものは FeatureTests の Delegate パネルで確かめる）
    /// </summary>
    public class DelegateTests
    {
        private const string Types = @"
namespace UdonSharpPlusTests.Dlg
{
    public delegate int Calc(int x);
    public delegate void Notify(Item item, string text);

    public class Item
    {
        public int v;
        public Calc calc;
        public Item(int v) { this.v = v; }
        public int Apply(int x) => calc(x) + v;
    }

    public static class MathUtil
    {
        public static int Twice(int x) => x * 2;
    }
}";

        private const string User = @"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Dlg
{
    public class User : UdonSharpBehaviour
    {
        public delegate bool Check(int x);

        public Calc publicCalc;
        private Calc _calc;
        private Calc[] _calcs = new Calc[2];
        private Check _check;
        private Notify _notify;
        private string _log = """";

        private static int Square(int x) => x * x;
        private int AddField(int x) => x + _log.Length;
        private bool IsEven(int x) => x % 2 == 0;
        private void OnNotify(Item item, string text) { _log += text + item.v; }

        private int Run(Calc c, int x) => c(x);
        private Calc Pick(bool square) => square ? (Calc)Square : MathUtil.Twice;

        private void Start()
        {
            _calc = Square;
            _calcs[0] = MathUtil.Twice;
            _calcs[1] = new Calc(AddField);
            _check = this.IsEven;
            _notify = OnNotify;
            Calc local = null;
            if (local == null) local = _calc;

            int r = _calc(3) + _calcs[0].Invoke(4) + _calcs[1](5) + Run(Square, 6) + Pick(false)(7) + local(1);
            bool even = _check(r);
            _notify(new Item(1), ""a"");

            var item = new Item(10);
            item.calc = MathUtil.Twice;
            r += item.Apply(5);
            Debug.Log(r + "" "" + even + "" "" + _log);
        }
    }
}";

        private string _dir;
        private string _user;
        private string _types;
        private CompileResult _result;

        [OneTimeSetUp]
        public void Compile()
        {
            _dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegate"));
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);

            _result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("Types.cs", Types), new TestSource("User.cs", User));
            string dumped = Path.Combine(_dir, "Assets", "UdonSharpPlusTests");
            _user = File.Exists(Path.Combine(dumped, "User.cs")) ? File.ReadAllText(Path.Combine(dumped, "User.cs")) : "";
            _types = File.Exists(Path.Combine(dumped, "Types.cs")) ? File.ReadAllText(Path.Combine(dumped, "Types.cs")) : "";
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_dir != null && Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CompilesWithoutDiagnostics()
        {
            Assert.That(_result.Diagnostics, Is.Empty, _user);
            Assert.That(_result.Programs.Select(p => p.TypeName), Does.Contain("UdonSharpPlusTests.Dlg.User"));
        }

        [Test]
        public void KeepsLineCount()
        {
            Assert.That(_user.Split('\n').Length, Is.EqualTo(User.Split('\n').Length));
            Assert.That(_types.Split('\n').Length, Is.EqualTo(Types.Split('\n').Length));
        }

        [Test]
        public void TrampolinesAreExportedWithFixedNames()
        {
            ProgramResult p = _result["User"];
            var entries = p.EntryPoints.ToList();
            Assert.That(entries, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_Calc__UdonSharpPlusTests_Dlg_User_Square__UdonSharpPlusTests_Dlg_User"));
            Assert.That(entries, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_Calc__UdonSharpPlusTests_Dlg_MathUtil_Twice__UdonSharpPlusTests_Dlg_User"));
            Assert.That(entries, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_User_Check__UdonSharpPlusTests_Dlg_User_IsEven__UdonSharpPlusTests_Dlg_User"));
            Assert.That(p.Uasm, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_Calc__p0:"));
            Assert.That(p.Uasm, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_Calc__ret:"));
            Assert.That(p.Uasm, Does.Contain("__dlg_UdonSharpPlusTests_Dlg_Notify__p1:"));
        }

        [Test]
        public void CallsCheckNullAtTheCallSite()
        {
            Assert.That(_user, Does.Contain("__invoke((((_calc).Length < 0) ? null : (_calc)), 3)").Or.Contain("((_calc).Length < 0)"));
        }

        [Test]
        public void PublicDelegateFieldIsNotSerialized()
        {
            ProgramResult p = _result["User"];
            Assert.That(p.Uasm, Does.Contain("publicCalc: %SystemObjectArray"));
        }

        // ---- エラー ----------------------------------------------------------

        private static CompileResult CompileUser(string body, string extra = "")
        {
            string src = @"
using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);
public delegate T Gen<T>(T x);
public delegate void ByRef(ref int x);
" + extra + @"
public class Bad : UdonSharpBehaviour
{
    private Calc _calc;
    private static int Square(int x) => x * x;
" + body + @"
}";
            return UdonSharpTestCompiler.Compile(new TestSource("Bad.cs", src));
        }

        [TestCase("void Start() { for (int i = 0; i < 2; i++) _calc = x => x + i; }", "'i' : この場所で宣言した変数（for の変数")]
        [TestCase("void Start() { var l = new System.Collections.Generic.List<int>(); l.ForEach(v => { _calc = x => x + v; }); }", "'v' : List・LINQ のメソッドに渡すラムダの引数")]
        [TestCase("void Start() { int k = 1; _calc = x => x + k; Inc(ref k); } void Inc(ref int a) { a++; }", "'k' : デリゲートに入れるラムダで使う変数は、ref / out / in で渡せません")]
        [TestCase("void Start() { Vector3 v = Vector3.zero; _calc = x => x + (int)v.x; v.x = 1; }", "'v' : デリゲートに入れるラムダで使う構造体の変数の、フィールド・プロパティへの代入は未対応です")]
        [TestCase("int P { set { _calc = x => x + value; } }", "'value' : この引数を、デリゲートに入れるラムダで使うことは未対応です")]
        [TestCase("private Calc _init = x => x;", "フィールドの初期化子の中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("static void S() { Calc c = x => x; }", "static メソッドの中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("void Start() { _calc = delegate (int x) { return x; }; }", "匿名メソッド（delegate { ... }）をデリゲートに入れることは、まだ未対応です")]
        [TestCase("Calc[] _cs = new Calc[2]; int Idx() => 0; void Start() { _cs[Idx()] += Square; }", "この形の受け側への += / -= は未対応です")]
        [TestCase("void Start() { _calc?.Invoke(1); }", "?.Invoke は未対応です")]
        [TestCase("void Start() { bool b = _calc == _calc; }", "デリゲートどうしの == / != は未対応です")]
        [TestCase("void Start() { var m = _calc.Method; }", "デリゲートのメンバーは、Invoke のほかは未対応です")]
        [TestCase("[SerializeField] private Calc _serialized;", "シリアライズ・同期できません")]
        [TestCase("private Calc _init = Square;", "フィールドの初期化子の中でデリゲートを作ることは未対応です")]
        [TestCase("static void S() { Calc c = Square; }", "static メソッドの中でデリゲートを作ることは未対応です")]
        [TestCase("void Start() { Gen<int> g = null; }", "ジェネリックのデリゲートは、まだ未対応です")]
        [TestCase("void Start() { ByRef g = null; }", "ref / out / in の引数を持つデリゲートは未対応です")]
        [TestCase("void Start() { var l = new System.Collections.Generic.List<Calc>(); }", "型引数（List<Calc> など）に使うことは、まだ未対応です")]
        [TestCase("void Start() { object o = null; bool b = o is Calc; }", "typeof・nameof・is・as・型パターンは未対応です")]
        [TestCase("void Start() { Other o = null; _calc = o.Inc; }", "ほかのインスタンスのメソッド（クラスのメソッド）をデリゲートに入れることは、まだ未対応です")]
        [TestCase("event Calc OnCalc;", "event は未対応です")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = CompileUser(body, "public class Other { public int Inc(int x) => x + 1; }");
            Assert.That(r.Diagnostics.Any(d => d.Contains(message)), r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics));
        }

        [Test]
        public void CreatingInPlainClassIsAnError()
        {
            CompileResult r = CompileUser("void Start() { new Maker().Make(); }",
                "public class Maker { static int Id(int x) => x; public Calc Make() => Id; }");
            Assert.That(r.Diagnostics.Any(d => d.Contains("普通のクラスの中でデリゲートを作ることは、まだ未対応です")), string.Join("\n", r.Diagnostics));
        }

        // ---- ラムダ（#120） ----------------------------------------------------

        private const string LambdaUser = @"using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);
public delegate void Pair(int a, string b);

public class LambdaUser : UdonSharpBehaviour
{
    private Calc _calc;
    private Calc _rec;
    private int _base = 10;

    private int Apply(Calc c, int x) => c(x);

    private void Start()
    {
        _calc = x => x * 2 + _base;
        _calc = x =>
        {
            if (x > 0)
                return x;
            return -x;
        };
        _rec = n => n <= 1 ? 1 : n * _rec(n - 1);
        Pair p = (_, _) => Debug.Log(_base);
        Calc nested = x => { Calc inner = y => y + 1; return inner(x); };
        Debug.Log(Apply(x => x - 1, 3) + _calc(-2) + _rec(4) + nested(1));
        p(1, ""a"");
    }
}";

        [Test]
        public void LambdaCompilesAndKeepsLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateLambda"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("LambdaUser.cs", LambdaUser));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "LambdaUser.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(LambdaUser.Split('\n').Length), lowered);
                Assert.That(lowered, Does.Contain("[global::UdonSharp.RecursiveMethod] private int __dlg_Calc__lambda2__LambdaUser__body(int n)"),
                    "デリゲートを呼ぶラムダ（再帰できる）は [RecursiveMethod]");
                Assert.That(lowered, Does.Contain("private int __dlg_Calc__lambda0__LambdaUser__body(int x)"));
                Assert.That(lowered, Does.Not.Contain("[global::UdonSharp.RecursiveMethod] private int __dlg_Calc__lambda0__"));
                Assert.That(lowered, Does.Contain("private void __dlg_Pair__lambda3__LambdaUser__body(int __a0, string __a1)"), "捨てる引数は名前を付け直す");
                var entries = r.Programs.Single().EntryPoints.ToList();
                Assert.That(entries, Does.Contain("__dlg_Calc__lambda0__LambdaUser"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ---- クロージャ（#121） -------------------------------------------------

        private const string ClosureUser = @"using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);
public delegate Calc MakeCalc(int k);

public class ClosureUser : UdonSharpBehaviour
{
    private Calc[] _calcs = new Calc[3];

    private Calc Adder(int k) => x => x + k;

    private void Start()
    {
        int a = 1,
            b = 2;
        const int c = 5;
        _calcs[0] = x => x + a + b + c;
        a++;
        a += 10;
        int v = a++ + (b = 7);
        for (int i = 0; i < 2; i++)
        {
            int j = i;
            _calcs[i + 1] = x => x * j;
        }
        foreach (int w in new int[] { 3, 4 })
            _calcs[2] = x => x - w;
        MakeCalc mk = k => y => y + k + a;
        Debug.Log(_calcs[0](1) + Adder(2)(3) + mk(1)(2) + v);
    }
}";

        [Test]
        public void ClosureCompilesAndKeepsLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateClosure"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("ClosureUser.cs", ClosureUser));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "ClosureUser.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(ClosureUser.Split('\n').Length), lowered);
                Assert.That(lowered, Does.Contain("object __e = __d[__i + 2]; if (__e != null) __u.SetProgramVariable(\"__dlg_Calc__env\", __e);"));
                Assert.That(lowered, Does.Contain("(object[])__env[0]"), "ラムダの本体で、受け取った環境を取り出す");
                Assert.That(lowered, Does.Contain("((int)(5))"), "定数は値にする");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// 環境に置く変数の複数行の宣言・式形式のメソッドを書き換えても、うしろのエラーは元の行に出る（#129 D3）。
        /// UdonSharp が対応していない System.IO.File.Exists をエラーの印にする
        /// </summary>
        [Test]
        public void ErrorsAfterClosureRewritesKeepLines()
        {
            const string src = @"using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);

public class ClosureLines : UdonSharpBehaviour
{
    private Calc Make(int k) =>
        x =>
            x + k;

    private void Start()
    {
        int a = 1,
            b = 2;
        Calc c = x => x + a + b;
        Debug.Log(System.IO.File.Exists(""a""));
        Debug.Log(c(1) + Make(1)(2));
    }

    private void Update() { Debug.Log(System.IO.File.Exists(""b"")); }
}";
            // UdonSharp は最初のエラーで止まるので、1 つずつ確かめる
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("ClosureLines.cs", src.Replace("System.IO.File.Exists(\"b\")", "GetType().Name")));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("ClosureLines.cs(17,"), "複数行の宣言のうしろ（17 行目）");
            r = UdonSharpTestCompiler.Compile(new TestSource("ClosureLines.cs", src.Replace("System.IO.File.Exists(\"a\")", "GetType().Name")));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("ClosureLines.cs(21,"), "複数行の式形式のメソッドのうしろ（21 行目）");
        }

        // ---- += / -=（#122） ----------------------------------------------------

        private const string CombineUser = @"using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);

public class Box { public Calc calc; }

public class CombineUser : UdonSharpBehaviour
{
    private Calc _calc;
    private Calc[] _calcs = new Calc[2];
    private Calc Prop { get; set; }

    private static int Square(int x) => x * x;

    private void Start()
    {
        _calc += Square;
        _calc -= Square;
        _calcs[1] += x => x + 1;
        Prop += Square;
        var box = new Box();
        box.calc += Square;
        int k = 1;
        Calc cap = null;
        Calc use = x => cap(x) + k;
        cap += Square;
        Calc sum = _calc + Prop - cap;
        string text = ""d"" + _calc;
        Debug.Log(text + use(1) + sum(2) + (_calc = _calc + Square)(3));
    }
}";

        [Test]
        public void CombineCompilesAndKeepsLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateCombine"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("CombineUser.cs", CombineUser));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "CombineUser.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(CombineUser.Split('\n').Length), lowered);
                Assert.That(lowered, Does.Contain("_calc = __cls___dlg_Calc____combine__objectArray__objectArray(_calc, "));
                Assert.That(lowered, Does.Contain("_calc = __cls___dlg_Calc____remove__objectArray__objectArray(_calc, "));
                Assert.That(lowered, Does.Contain("\"d\" + _calc"), "文字列の連結はそのまま");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// 受け側を 2 回評価すると結果が変わる += / -= のエラーは、元の行の受け側を指す。添字が定数・変数の配列の要素は通る（#122 の追加テスト）
        /// </summary>
        [TestCase("+=")]
        [TestCase("-=")]
        public void CombineOnUnrepeatableReceiverPointsToOriginalLine(string op)
        {
            string src = @"using UdonSharp;

public delegate int Calc(int x);

public class CombineError : UdonSharpBehaviour
{
    private Calc[] _arr = new Calc[2];
    private int _i;
    private static int Square(int x) => x * x;
    private int Next() => _i++;

    private void Start()
    {
        _arr[0] += Square;
        _arr[_i] -= Square;
        _arr[Next()] " + op + @" Square;
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("CombineError.cs", src));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("CombineError.cs(16,9)"), "_arr[Next()] の位置（16 行目の 9 列目）");
            Assert.That(r.Diagnostics[0], Does.Contain("この形の受け側への += / -= は未対応です"));
        }

        /// <summary>ラムダの本体の中のエラーは、元の行に出る（複数行のブロックの本体でも、文ごとの行）</summary>
        [Test]
        public void LambdaBodyErrorPointsToOriginalLine()
        {
            const string src = @"using UdonSharp;
using UnityEngine;

public delegate bool PathCheck(string p);

public class LambdaError : UdonSharpBehaviour
{
    private void Start()
    {
        PathCheck c = p =>
        {
            Debug.Log(p);
            return System.IO.File.Exists(p);
        };
        Debug.Log(c(""a""));
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("LambdaError.cs", src));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("LambdaError.cs(13,"), "return の行（13 行目）");
        }
    }
}
