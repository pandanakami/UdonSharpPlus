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
        [TestCase("int P { set { _calc = x => x + value; } }", "'value' : この引数を、デリゲートに入れるラムダ・ローカル関数で使うことは未対応です")]
        [TestCase("private Calc _init = x => x;", "フィールドの初期化子の中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("static void S() { Calc c = x => x; }", "static メソッドの中でラムダをデリゲートに入れることは未対応です")]
        [TestCase("void Start() { int G<T>(int x) => x; G<int>(1); }", "'G' : ジェネリックのローカル関数は未対応です")]
        [TestCase("void Start() { switch (1) { case 1: int F() => 1; break; } }", "'F' : この場所のローカル関数は未対応です")]
        [TestCase("void Start() { static int F(int x) { Calc c = Square; return c(x); } F(1); }", "static のローカル関数の中でデリゲートを作ることは未対応です")]
        [TestCase("void Start() { int k = 1; void F(int x) { k += x; } new System.Collections.Generic.List<int>().ForEach(F); }", "'F' : 外の変数を使うローカル関数を、ここで使うことは未対応です")]
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
        [TestCase("void Start() { _calc = new System.Random().Next; }", "'Next' : このインスタンスのメソッドをデリゲートに入れることは未対応です")]
        [TestCase("event Calc OnCalc;", "event は未対応です")]
        [TestCase("void Start() { System.Func<string> fs = () => \"\"; System.Func<object> fo = fs; }", "'Func<string>' : 型の違うデリゲート（'Func<object>'）への変換は未対応です")]
        [TestCase("void Start() { System.EventHandler h = null; }", "'EventHandler' : この .NET のデリゲートは未対応です")]
        [TestCase("void Start() { System.Converter<int, string> c = null; }", "'Converter<int, string>' : この .NET のデリゲートは未対応です")]
        [TestCase("void Start() { System.Action<object> ao = null; System.Action<string> a = ao; }", "'Action<object>' : 型の違うデリゲート（'Action<string>'）への変換は未対応です")]
        [TestCase("void Start() { System.Func<int, bool> f = null; System.Predicate<int> p = new System.Predicate<int>(f); }", "'Predicate<int>' : この形の new は未対応です")]
        [TestCase("void Start() { var l = new System.Collections.Generic.List<System.Action>(); }", "型引数（List<Action> など）に使うことは、まだ未対応です")]
        [TestCase("event System.Action<int> OnDone;", "event は未対応です")]
        [TestCase("[SerializeField] private System.Action<int> _serializedAction;", "シリアライズ・同期できません")]
        [TestCase("void Start() { System.Action a = null; a?.Invoke(); }", "?.Invoke は未対応です")]
        public void ReportsUnsupported(string body, string message)
        {
            CompileResult r = CompileUser(body, "public class Other { public int Inc(int x) => x + 1; }");
            Assert.That(r.Diagnostics.Any(d => d.Contains(message)), r.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", r.Diagnostics));
        }

        /// <summary>普通のクラスの中で、UdonSharpBehaviour の static メソッドは入れられない（ほかのプログラムの static メソッドは呼べないため）</summary>
        [Test]
        public void UsbStaticMethodInPlainClassIsAnError()
        {
            CompileResult r = CompileUser("public static int Tick(int x) => x; void Start() { new Maker().MakeUsb(); }",
                "public class Maker { public Calc MakeUsb() => Bad.Tick; }");
            Assert.That(r.Diagnostics.Any(d => d.Contains("'Tick' : 普通のクラスの中で、UdonSharpBehaviour の static メソッドをデリゲートに入れることは未対応です")), string.Join("\n", r.Diagnostics));
        }

        /// <summary>普通のクラスの中で作る・ほかのインスタンスのメソッドを入れる（#124）は、コンパイルが通る</summary>
        [Test]
        public void CreatingInPlainClassCompiles()
        {
            CompileResult r = CompileUser("void Start() { var m = new Maker(); _calc = m.Make(); _calc = m.Inc; _calc = m.MakeLambda(2); Debug.Log(_calc(1)); }",
                "public class Maker { int k = 1; static int Id(int x) => x; public Calc Make() => Id; public int Inc(int x) => x + k; public Calc MakeLambda(int b) => x => x + b + k; }");
            Assert.That(r.Diagnostics, Is.Empty);
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

        // ---- 匿名メソッド・ローカル関数（#123） -------------------------------------

        private const string LocalUser = @"using UdonSharp;
using UnityEngine;

public delegate int Calc(int x);
public delegate void Act();

public class LocalUser : UdonSharpBehaviour
{
    private int _base = 10;

    private void Start()
    {
        int k = 2;
        int Add(int x) => x + k;
        int Fact(int n)
        {
            if (n <= 1)
                return 1;
            return n * Fact(n - 1);
        }
        void Bump() { k++; }
        static int Twice(int x) => x * 2;
        Calc c = Add;
        Calc anon = delegate (int x) { return x + k + _base; };
        Act any = delegate { k += 10; };
        Bump();
        any();
        Debug.Log(Add(1) + Fact(4) + Twice(3) + c(1) + anon(1));
    }
}";

        [Test]
        public void LocalFunctionsAndAnonymousMethodsCompileAndKeepLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateLocal"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("LocalUser.cs", LocalUser));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "LocalUser.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(LocalUser.Split('\n').Length), lowered);
                Assert.That(lowered, Does.Contain("private int __lf_Add__0_0(object[] __env0, int x)"), "外の変数の環境を引数で受け取る");
                Assert.That(lowered, Does.Contain("[global::UdonSharp.RecursiveMethod] private int __lf_Fact__0_1(int n)"), "自分を呼ぶローカル関数は [RecursiveMethod]");
                Assert.That(lowered, Does.Contain("private static int __lf_Twice__0_3(int x)"));
                Assert.That(lowered, Does.Not.Contain("int Add(int x)"));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// ローカル関数・匿名メソッドの中のエラーが、元のファイル・行を指す（#123 の追加テスト 10）。
        /// UdonSharp が対応していない System.IO.File.Exists をエラーの印にする。どれも 1 つのエラーだけ
        /// </summary>
        [TestCase("Plain", "Types.cs(9,")]
        [TestCase("NestedInLambda", "Main.cs(14,")]
        [TestCase("AnonymousInPartial", "Main.Part.cs(9,")]
        [TestCase("LocalInLinq", "Main.cs(16,")]
        public void LocalFunctionErrorsPointToOriginalLocation(string where, string location)
        {
            string Mark(string name, string code) => where == name ? code : "0";
            string types = @"public delegate int Calc(int x);

public class Plain
{
    public int Run(int x)
    {
        int Local(int y)
        {
            return y + (System.IO.File.Exists(""a"") ? 1 : " + Mark("Plain", "0") + @");
        }
        return Local(x);
    }
    public Calc Make() => " + (where == "AnonymousInPlainClass" ? "delegate (int x) { return x; }" : "null") + @";
}";
            if (where != "Plain")
                types = types.Replace(@"System.IO.File.Exists(""a"")", "true");
            string main = @"using UdonSharp;
using System.Linq;

public partial class Main : UdonSharpBehaviour
{
    private void Start()
    {
        int k = 1;
        Calc c = x =>
        {
            int Inner(int y)
            {
                y += k;
                return y + (" + Mark("NestedInLambda", @"System.IO.File.Exists(""b"") ? 1 : 0") + @");
            }
            int Env(int y) => y + k; var list = new System.Collections.Generic.List<int>(); var e = list.Select(" + (where == "LocalInLinq" ? "Env" : "y => y") + @");
            return Inner(x) + Env(x);
        };
        UnityEngine.Debug.Log(c(1) + new Plain().Run(1) + Part(1));
    }
}";
            string part = @"using UdonSharp;

public partial class Main
{
    private int Part(int x)
    {
        Calc c = delegate (int y)
        {
            return y + (" + Mark("AnonymousInPartial", @"System.IO.File.Exists(""c"") ? 1 : 0") + @");
        };
        return c(x);
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Types.cs", types), new TestSource("Main.cs", main), new TestSource("Main.Part.cs", part));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain(location), r.Diagnostics[0]);
        }

        /// <summary>
        /// クラスのメソッドを入れたときのエラーが、元のファイル・行を指す（#124 の追加テスト 12・7）。
        /// UdonSharp が対応していない System.IO.File.Exists をエラーの印にする。どれも 1 つのエラーだけ
        /// </summary>
        [TestCase("PlainMethod", "Types.cs(8,")]
        [TestCase("PlainLambda", "Types.cs(12,")]
        [TestCase("PrivateUsb", "Main.cs(9,")]
        [TestCase("LinqMethodGroup", "Main.cs(17,")]
        public void ClassMethodErrorsPointToOriginalLocation(string where, string location)
        {
            string Mark(string name) => where == name ? @"(System.IO.File.Exists(""a"") ? 1 : 0)" : "0";
            string types = @"public delegate int Calc(int x);

public class Box
{
    public int v;
    public int Get(int x)
    {
        return x + v + " + Mark("PlainMethod") + @";
    }
    public Calc Make() => y =>
    {
        return y + " + Mark("PlainLambda") + @";
    };
}";
            string main = @"using UdonSharp;
using System.Linq;

public class Main : UdonSharpBehaviour
{
    public int hp;
    private int Priv(int x)
    {
        return x + hp + " + Mark("PrivateUsb") + @";
    }

    private void Start()
    {
        var box = new Box();
        Calc a = box.Get, b = box.Make(), c = Priv;
        var nums = new System.Collections.Generic.List<int> { 1 };
        var e = nums.Select(" + (where == "LinqMethodGroup" ? "box.Get" : "x => x") + @").ToList();
        UnityEngine.Debug.Log(a(1) + b(1) + c(1) + e.Count);
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("Types.cs", types), new TestSource("Main.cs", main));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain(location), r.Diagnostics[0]);
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

        private const string FuncUser = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class FuncHolder
{
    public Func<int, int> map;
    public int Sum(List<int> l) => l.Select(map).Sum();
}

public class FuncUser : UdonSharpBehaviour
{
    private Func<int, bool> _big;
    private Action<string> _log;
    private Comparison<int> _cmp = null;
    private static int Square(int x) => x * x;

    private void Start()
    {
        var l = new List<int> { 3, 1, 2 };
        int k = 1;
        _big = x => x > k;
        Action tick = () => k++;
        tick();
        Predicate<int> odd = x => x % 2 == 1;
        Func<int, int> sq = Square;
        _cmp = (a, b) => b - a;
        _log += s => k += s.Length;
        _log(""a"");
        int n = l.Where(_big).Select(sq).Sum() + l.Count(x => x > 1) + l.Select(Square).Sum();
        bool e = l.Exists(odd);
        l.Sort(_cmp);
        l.ForEach(x => k += x);
        var h = new FuncHolder();
        h.map = sq;
        n += h.Sum(l);
    }
}";

        private const string FuncOther = @"using UdonSharp;
using IntFn = System.Func<int, int>;

namespace Other.Space
{
    public class FuncOther : UdonSharpBehaviour
    {
        private IntFn _a;
        private System.Func<int, int> _b;

        private void Start()
        {
            _a = x => x + 1;
            _b = _a;
            int n = _b(1);
        }
    }
}";

        /// <summary>デリゲートの変数を、対応していない LINQ のメソッドに渡したときのエラーは、元の呼び出しの行に出る（#125）</summary>
        [Test]
        public void ExternalDelegatePassedToUnsupportedLinqPointsToOriginalLine()
        {
            const string src = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class FuncLinqError : UdonSharpBehaviour
{
    private void Start()
    {
        Func<int, int> key = x => x % 2;
        var l = new List<int> { 1 };
        int ok = l.Select(key).Sum();
        var g = l.GroupBy(key);
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("FuncLinqError.cs", src));
            Assert.That(r.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(r.Diagnostics.All(d => d.Contains("FuncLinqError.cs(13,")), string.Join("\n", r.Diagnostics));
        }

        /// <summary>
        /// .NET のデリゲート（Action・Func・Predicate・Comparison。#125）：型・作る・呼ぶ・+=・LINQ と List のメソッドに変数を渡す。
        /// 同じ型の Func があっても、LINQ に直接書いたラムダ・メソッド名は今までどおり埋め込む。行は変えない
        /// </summary>
        [Test]
        public void ExternalDelegatesCompileAndKeepLines()
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateFunc"));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir,
                    new TestSource("FuncUser.cs", FuncUser), new TestSource("FuncOther.cs", FuncOther));
                Assert.That(r.Diagnostics, Is.Empty);
                string lowered = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "FuncUser.cs"));
                string other = File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", "FuncOther.cs"));
                Assert.That(lowered.Split('\n').Length, Is.EqualTo(FuncUser.Split('\n').Length), lowered);
                Assert.That(other.Split('\n').Length, Is.EqualTo(FuncOther.Split('\n').Length), other);
                Assert.That(lowered, Does.Not.Contain("Func<"), lowered);
                Assert.That(lowered, Does.Not.Contain("Predicate<"), lowered);
                Assert.That(lowered, Does.Not.Contain("Comparison<"), lowered);

                // 変数を渡したところだけ、デリゲートを呼ぶ形になる（渡すときに 1 回だけ評価して、生成メソッドの引数にする）。
                // 直接書いたラムダ・メソッド名は、今までどおり本体を埋め込む
                Assert.That(lowered, Does.Contain("__dv0"), lowered);
                Assert.That(lowered, Does.Contain("__dlgarg"), lowered);
                Assert.That(lowered, Does.Match(@"\(/\*cus@\d+\*/x > 1\)"), "直接書いたラムダは埋め込む\n" + lowered);
                Assert.That(lowered, Does.Match(@"/\*cus@\d+\*/Square\(__a0\)"), "直接書いたメソッド名は埋め込む\n" + lowered);

                // 同じ型（Func<int, int>）を、別のファイル・別の名前空間・完全修飾名・using の別名で使っても、呼び出しの static クラスは 1 つ
                const string helper = "static class __dlg_System_Func_int__int_";
                int count = System.Text.RegularExpressions.Regex.Matches(lowered + other, System.Text.RegularExpressions.Regex.Escape(helper) + @"\b").Count;
                Assert.That(count, Is.EqualTo(1), lowered + "\n----\n" + other);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>Func に入れたラムダの中のエラーは、元の行に出る（そのラムダの変数を LINQ に渡しても。#125）</summary>
        [Test]
        public void ExternalDelegateLambdaErrorPointsToOriginalLine()
        {
            const string src = @"using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

public class FuncError : UdonSharpBehaviour
{
    private void Start()
    {
        Func<string, bool> exists = p =>
        {
            int n = p.Length;
            return System.IO.File.Exists(p);
        };
        var l = new List<string> { ""a"" };
        int c = l.Where(exists).Count();
    }
}";
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("FuncError.cs", src));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("FuncError.cs(13,"), "return の行（13 行目）");
        }
    }
}
