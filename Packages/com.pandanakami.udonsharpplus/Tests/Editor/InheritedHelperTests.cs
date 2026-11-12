using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 基底と派生の UdonSharpBehaviour の両方で同じものを使っても、生成したメソッドの名前が重ならないこと（Redmine #111）。
    /// U# は、private static でも、基底と同じ名前のメソッドを「基底のメソッドを隠している」とみなしてエラーにする
    /// </summary>
    public class InheritedHelperTests
    {
        private const string Shared = @"
using System.Collections.Generic;

namespace UdonSharpPlusTests.InheritedHelper
{
    public interface ISize { int GetWidth(); }

    public class Box : ISize
    {
        public int w = 2;
        public int GetWidth() => w;
        public int Pure(int x) => x * 2;
    }

    public class Pair<T>
    {
        public T value;
        public T Get() => value;
    }
}";

        // 基底と派生で、同じ書き方を 1 つずつ使う
        private static string Uses(string name) => $@"
            ISize s = new Box();
            object o = s;
            Box b = new Box();
            var list = new List<int> {{ 1, 2, 3 }};
            var pair = new Pair<int>();
            int {name} = s.GetWidth()                                    // インタフェースの呼び出し
                + (o is ISize ? 1 : 0) + ((o as ISize) != null ? 1 : 0)  // インタフェースの is / as
                + b.Pure(1) + (o is Box ? 1 : 0) + ((o as Box) != null ? 1 : 0)   // 普通のクラス
                + list.FindIndex(x => x > 1) + list.Where(x => x > 1).Count()     // ラムダ・LINQ
                + pair.Get() + Echo<int>(1);                                      // ジェネリック";

        private static string Base => @"
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace UdonSharpPlusTests.InheritedHelper
{
    public abstract class BaseUser : UdonSharpBehaviour
    {
        protected T Echo<T>(T v) => v;

        public int BaseWidth()
        {" + Uses("r") + @"
            return r;
        }
    }
}";

        private static string Derived => @"
using System.Collections.Generic;
using System.Linq;
using UdonSharp;

namespace UdonSharpPlusTests.InheritedHelper
{
    public class DerivedUser : BaseUser
    {
        public int DerivedWidth()
        {" + Uses("r") + @"
            return r;
        }
    }
}";

        private static string Third => @"
using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.InheritedHelper
{
    public class ThirdUser : DerivedUser
    {
        public int ThirdWidth()
        {" + Uses("r") + @"
            return r;
        }
    }
}";

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = System.IO.Path.GetFullPath(System.IO.Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusInherited"));
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

        [Test]
        public void BaseAndDerivedUseSameFeatures()
        {
            CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("Shared.cs", Shared), new TestSource("BaseUser.cs", Base), new TestSource("DerivedUser.cs", Derived));
            Assert.That(result.Diagnostics, Is.Empty);

            // 基底の名前はそのまま、派生の側だけ「名前__クラス名」になる（宣言と呼び出しの両方）
            string baseText = Dumped("BaseUser.cs");
            string derived = Dumped("DerivedUser.cs");
            Assert.That(baseText, Does.Contain("__if_GetWidth__call("));
            Assert.That(baseText, Does.Not.Contain("__DerivedUser"));
            Assert.That(derived, Does.Contain("int __if_GetWidth__call__DerivedUser(object __t"));
            Assert.That(derived, Does.Contain("__if_GetWidth__call__DerivedUser(s)"));
            Assert.That(derived, Does.Not.Contain("__if_GetWidth__call("));
        }

        /// <summary>3 段の継承でも、それぞれの段で別の名前になる</summary>
        [Test]
        public void ThreeLevels()
        {
            CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("Shared.cs", Shared), new TestSource("BaseUser.cs", Base), new TestSource("DerivedUser.cs", Derived),
                new TestSource("ThirdUser.cs", Third));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(Dumped("ThirdUser.cs"), Does.Contain("__if_GetWidth__call__ThirdUser("));
        }
    }
}
