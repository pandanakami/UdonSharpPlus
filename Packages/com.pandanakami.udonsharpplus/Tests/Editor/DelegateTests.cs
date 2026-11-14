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

        [TestCase("void Start() { int k = 1; _calc = x => x + k; }", "外のローカル変数・引数を使うラムダを、デリゲートに入れることは、まだ未対応です")]
        [TestCase("void M(int k) { _calc = x => { return x * k; }; }", "'k' : 外のローカル変数・引数を使うラムダ")]
        [TestCase("private Calc _init = x => x;", "フィールドの初期化子の中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("static void S() { Calc c = x => x; }", "static メソッドの中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("void Start() { _calc = delegate (int x) { return x; }; }", "匿名メソッド（delegate { ... }）をデリゲートに入れることは、まだ未対応です")]
        [TestCase("void Start() { _calc += Square; }", "デリゲートの += / -= は、まだ未対応です")]
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
