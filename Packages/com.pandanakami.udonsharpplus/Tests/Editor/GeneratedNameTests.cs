using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 生成したメソッド・フィールドの名前が、ほかのものと重ならないこと（Redmine #113。#111 と同じ観点）
    /// </summary>
    public class GeneratedNameTests
    {
        private const string Shared = @"
using System.Collections.Generic;

namespace UdonSharpPlusTests.GeneratedName
{
    public interface ISize { int GetWidth(); }

    public class Box : ISize
    {
        public int w = 2;
        public int GetWidth() => w;
        public int Pure(int x) => x * 2;
    }
}";

        // 同じクラスの中で、同じ書き方を 1 つずつ使う
        private static string Uses(string name) => $@"
            ISize s = new Box();
            object o = s;
            Box b = new Box();
            var list = new List<int> {{ 1, 2, 3 }};
            int {name} = s.GetWidth() + (o is ISize ? 1 : 0) + ((o as ISize) != null ? 1 : 0)
                + b.Pure(1) + (o is Box ? 1 : 0) + ((o as Box) != null ? 1 : 0)
                + list.FindIndex(x => x > 1) + list.Where(x => x > 1).Count();";

        private static string Part(string method) => @"
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace UdonSharpPlusTests.GeneratedName
{
    public partial class Split : UdonSharpBehaviour
    {
        public int " + method + @"()
        {" + Uses("r") + @"
            return r;
        }
    }
}";

        /// <summary>A の前提：書き換えるもののない partial の UdonSharpBehaviour は、U# がそのまま扱える</summary>
        [Test]
        public void PartialBehaviourWithoutLowering()
        {
            const string first = "using UdonSharp; namespace UdonSharpPlusTests.GeneratedName { public partial class Plain : UdonSharpBehaviour { public int A() => 1; } }";
            const string second = "using UdonSharp; namespace UdonSharpPlusTests.GeneratedName { public partial class Plain : UdonSharpBehaviour { public int B() => A() + 1; } }";
            CompileResult result = UdonSharpTestCompiler.Compile(new TestSource("Plain.cs", first), new TestSource("Plain.Second.cs", second));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = System.IO.Path.GetFullPath(System.IO.Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusGeneratedName"));
            if (System.IO.Directory.Exists(_dir))
                System.IO.Directory.Delete(_dir, recursive: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (System.IO.Directory.Exists(_dir))
                System.IO.Directory.Delete(_dir, recursive: true);
        }

        private string Dumped(string file) =>
            System.IO.File.ReadAllText(System.IO.Path.Combine(_dir, "Assets", "UdonSharpPlusTests", file));

        /// <summary>A. partial の UdonSharpBehaviour の 2 つのファイルの両方で、同じ書き方を使う（#114）</summary>
        [Test]
        public void PartialBehaviourInTwoFiles()
        {
            CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("Shared.cs", Shared), new TestSource("Split.cs", Part("First")), new TestSource("Split.Second.cs", Part("Second")));
            Assert.That(result.Diagnostics, Is.Empty);

            // 1 つ目のファイルはそのまま、2 つ目のファイルの側が「__p2」付きになり、その中の呼び出しも同じ名前を指す
            string first = Dumped("Split.cs");
            string second = Dumped("Split.Second.cs");
            Assert.That(first, Does.Contain("__if_GetWidth__call(s)"));
            Assert.That(first, Does.Not.Contain("__p2"));
            Assert.That(second, Does.Contain("int __if_GetWidth__call__p2(object __t"));
            Assert.That(second, Does.Contain("__if_GetWidth__call__p2(s)"));
            Assert.That(second, Does.Contain("__lambda_FindIndex_0__p2("));
        }

        /// <summary>
        /// partial の 2 つ目のファイルだけが、普通のクラスの is / as・null 確認のヘルパーを使う（#130）。
        /// 汎用クラスのコピーは 1 つ目のファイルに置くが、ヘルパーは使う側に置く。両方が使うものは 1 つだけ置く
        /// </summary>
        [Test]
        public void PartialHelperUsedOnlyInSecondFile()
        {
            const string types = @"public class Gem { public int v; public int Twice(int x) => x * 2; }
public class Rock { }";
            const string first = @"using UdonSharp;
public partial class Mine : UdonSharpBehaviour
{
    private object _o;
    private void Start() { var g = new Gem(); _o = g; Debug(); }
}";
            const string second = @"using UdonSharp;
public partial class Mine
{
    private Gem[] _gems = new Gem[1];
    private void Debug()
    {
        bool a = _o is Gem;
        Gem g = _o as Gem;
        Rock r = _o as Rock;
        int v = GetGem().Twice(2);
        UnityEngine.Debug.Log(a + "" "" + (g != null) + "" "" + (r != null) + v);
    }
    private Gem GetGem() => _gems[0];
}";
            CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("Types.cs", types), new TestSource("Mine.cs", first), new TestSource("Mine.Second.cs", second));
            Assert.That(result.Diagnostics, Is.Empty);
            string dumpedFirst = Dumped("Mine.cs");
            string dumpedSecond = Dumped("Mine.Second.cs");
            Assert.That(dumpedFirst, Does.Contain("__cls_Gem__ctor"), "コピーは 1 つ目のファイル");
            Assert.That(dumpedSecond, Does.Contain("private static bool __cls_Gem__is(object __o)"));
            Assert.That(dumpedSecond, Does.Contain("__cls_Rock__as(object __o)"));
            Assert.That(dumpedSecond, Does.Contain("__cls__notnull("));
            Assert.That(dumpedFirst, Does.Not.Contain("__cls_Gem__is(object __o)"));
        }

        /// <summary>A と #111 の組み合わせ：partial の派生クラスの 2 つのファイルと、基底の両方で使う</summary>
        [Test]
        public void PartialDerivedBehaviour()
        {
            string Derived(string method) => Part(method).Replace("public partial class Split : UdonSharpBehaviour", "public partial class Split : Root");
            CompileResult result = UdonSharpTestCompiler.Compile(
                new TestSource("Shared.cs", Shared),
                new TestSource("Root.cs", Level("UdonSharpPlusTests.GeneratedName", "Root", "UdonSharpBehaviour", "A")),
                new TestSource("Split.cs", Derived("First")), new TestSource("Split.Second.cs", Derived("Second")));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        private static string Level(string ns, string name, string baseType, string method) => @"
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UdonSharpPlusTests.GeneratedName;

namespace " + ns + @"
{
    public " + (baseType == "UdonSharpBehaviour" ? "abstract " : "") + "class " + name + " : " + baseType + @"
    {
        public int " + method + @"()
        {" + Uses("r") + @"
            return r;
        }
    }
}";

        /// <summary>B. 名前が同じで名前空間が違う派生クラスが、2 段続く</summary>
        [Test]
        public void SameNameInDifferentNamespaces()
        {
            CompileResult result = UdonSharpTestCompiler.Compile(
                new TestSource("Shared.cs", Shared),
                new TestSource("Root.cs", Level("UdonSharpPlusTests.GeneratedName", "Root", "UdonSharpBehaviour", "A")),
                new TestSource("One/User.cs", Level("UdonSharpPlusTests.GeneratedName.One", "User", "UdonSharpPlusTests.GeneratedName.Root", "B")),
                new TestSource("Two/User.cs", Level("UdonSharpPlusTests.GeneratedName.Two", "User", "UdonSharpPlusTests.GeneratedName.One.User", "C")));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        private const string Caller = @"
using UdonSharp;

namespace UdonSharpPlusTests.GeneratedName
{
    public class Caller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public int Run(int x) { ISize s = (ISize)target; return s.GetWidth() + ((IScale)target).Scale(x); }
    }

    public interface IScale { int Scale(int x); }
}";

        /// <summary>C. 基底で実装したインタフェースを、派生でもう一度書く（実装は基底のもの）</summary>
        [Test]
        public void DerivedRedeclaresInterface()
        {
            const string types = @"
using UdonSharp;

namespace UdonSharpPlusTests.GeneratedName
{
    public class SizeBase : UdonSharpBehaviour, ISize, IScale
    {
        public int GetWidth() => 1;
        public int Scale(int x) => x;
    }

    public class SizeDerived : SizeBase, ISize, IScale
    {
    }
}";
            CompileResult result = UdonSharpTestCompiler.Compile(
                new TestSource("Shared.cs", Shared), new TestSource("Caller.cs", Caller), new TestSource("SizeTypes.cs", types));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        /// <summary>C. 基底の virtual の実装を、インタフェースをもう一度書いた派生で override する</summary>
        [Test]
        public void DerivedRedeclaresInterfaceAndOverrides()
        {
            const string types = @"
using UdonSharp;

namespace UdonSharpPlusTests.GeneratedName
{
    public class VirtualBase : UdonSharpBehaviour, ISize, IScale
    {
        public virtual int GetWidth() => 1;
        public virtual int Scale(int x) => x;
    }

    public class VirtualDerived : VirtualBase, ISize, IScale
    {
        public override int GetWidth() => 2;
        public override int Scale(int x) => x * 2;
    }
}";
            CompileResult result = UdonSharpTestCompiler.Compile(
                new TestSource("Shared.cs", Shared), new TestSource("Caller.cs", Caller), new TestSource("VirtualTypes.cs", types));
            Assert.That(result.Diagnostics, Is.Empty);
        }
    }
}
