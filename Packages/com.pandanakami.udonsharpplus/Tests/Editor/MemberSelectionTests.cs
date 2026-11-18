using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 書き換えの段が、対象の名前も書き方も含まないメンバーの中を見ない（TreeFilter.SelectMembers。Redmine #165）とき、
    /// 型の名前を書かずに値を手に入れるメンバーも、見落とさずに書き換えること。
    /// 見落とすと List が残り、U# のコンパイルがエラーになる。
    /// 使う側には LINQ と同じ名前（Count・Join など）を書かない（その名前でも選ぶので、確かめたいことが隠れる）。
    /// 要素は型の決まった変数に入れる（書き換えないと DataToken のままで、コンパイルが通らないようにする）
    /// </summary>
    public class MemberSelectionTests
    {
        private static void AssertCompiles(string typeName, params TestSource[] sources)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(sources);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            Assert.That(r[typeName].Uasm, Does.Contain("VRCSDK3DataDataList"));
        }

        [Test]
        public void ListFromMethodOfAnotherClass()
        {
            // List を返すメソッドの名前（Make）だけを書き、var で受ける
            AssertCompiles("SelUse",
                new TestSource("SelMaker.cs", "using System.Collections.Generic;\npublic static class SelMaker { public static List<int> Make() => new List<int> { 1, 2 }; }"),
                new TestSource("SelUse.cs", "using UdonSharp;\npublic class SelUse : UdonSharpBehaviour { void Start() { var xs = SelMaker.Make(); xs.Add(3); int v = xs[0] + 1; UnityEngine.Debug.Log(v); } }"));
        }

        [Test]
        public void ListFieldDeclaredInAnotherPartialFile()
        {
            // フィールドの宣言は別のファイル。使う側のメンバーには、フィールドの名前だけがある
            AssertCompiles("SelPart",
                new TestSource("SelPart.cs", "using System.Collections.Generic;\nusing UdonSharp;\npublic partial class SelPart : UdonSharpBehaviour { private List<int> items = new List<int>(); }"),
                new TestSource("SelPart.Use.cs", "public partial class SelPart { void Start() { items.Add(1); int v = items[0] + 1; UnityEngine.Debug.Log(v); } }"));
        }

        [Test]
        public void ListFieldOfOtherObject()
        {
            // 別のオブジェクトのフィールド（Items）が List
            AssertCompiles("SelProp",
                new TestSource("SelBox.cs", "using System.Collections.Generic;\npublic class SelBox { public List<string> Items = new List<string>(); }"),
                new TestSource("SelProp.cs", "using UdonSharp;\npublic class SelProp : UdonSharpBehaviour { void Start() { var b = new SelBox(); b.Items.Add(\"a\"); string v = b.Items[0]; UnityEngine.Debug.Log(v); } }"));
        }

        [Test]
        public void LinqOnArrayWithoutListName()
        {
            // 配列への LINQ（List の名前はなく、LINQ のメソッドの名前だけ）
            AssertCompiles("SelArr",
                new TestSource("SelArr.cs", "using System.Linq;\nusing UdonSharp;\npublic class SelArr : UdonSharpBehaviour { void Start() { int[] a = { 3, 1, 2 }; var b = a.Where(x => x > 1).ToList(); UnityEngine.Debug.Log(b.Count); } }"));
        }
    }
}
