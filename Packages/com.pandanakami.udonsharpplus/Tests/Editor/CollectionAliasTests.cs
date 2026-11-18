using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// List / Dictionary の using の別名（using IntList = List&lt;int&gt;;）を型に使ったもの（Redmine #167）。
    /// 別名の名前も、指す型として DataList / DataDictionary に置き換える。置き換え漏れがあると、宣言の型が List のまま残り、
    /// U# のコンパイルがエラーになる（CS0029 など）
    /// </summary>
    public class CollectionAliasTests
    {
        private static void AssertCompiles(string typeName, string expectedUdonType, params TestSource[] sources)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(sources);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            Assert.That(r[typeName].Uasm, Does.Contain(expectedUdonType));
        }

        private const string Usings = "using UdonSharp;\nusing UnityEngine;\nusing IntList = System.Collections.Generic.List<int>;\n";

        [Test]
        public void LocalVariable()
        {
            AssertCompiles("AliasLocal", "VRCSDK3DataDataList",
                new TestSource("AliasLocal.cs", Usings + "public class AliasLocal : UdonSharpBehaviour { void Start() { IntList xs = new IntList(); xs.Add(1); int v = xs[0] + 1; Debug.Log(v + xs.Count); } }"));
        }

        [Test]
        public void FieldParameterAndReturnType()
        {
            AssertCompiles("AliasMembers", "VRCSDK3DataDataList",
                new TestSource("AliasMembers.cs", Usings + @"public class AliasMembers : UdonSharpBehaviour
{
    private IntList _items = new IntList();
    private IntList Make(int n) { IntList r = new IntList(); r.Add(n); return r; }
    private int First(IntList xs) => xs[0];
    void Start() { _items = Make(3); int v = First(_items) + 1; Debug.Log(v); }
}"));
        }

        [Test]
        public void ArrayOfAlias()
        {
            AssertCompiles("AliasArray", "VRCSDK3DataDataList",
                new TestSource("AliasArray.cs", Usings + "public class AliasArray : UdonSharpBehaviour { void Start() { IntList[] lists = new IntList[1]; lists[0] = new IntList(); lists[0].Add(2); int v = lists[0][0] + 1; Debug.Log(v); } }"));
        }

        [Test]
        public void DictionaryAliasInsideNamespace()
        {
            AssertCompiles("AliasNs.AliasDict", "VRCSDK3DataDataDictionary",
                new TestSource("AliasDict.cs", @"namespace AliasNs
{
    using UdonSharp;
    using UnityEngine;
    using Scores = System.Collections.Generic.Dictionary<string, int>;
    public class AliasDict : UdonSharpBehaviour
    {
        private Scores _scores = new Scores();
        void Start() { _scores[""a""] = 1; int v = _scores[""a""] + 1; Debug.Log(v); }
    }
}"));
        }

        [Test]
        public void KeyValuePairAliasInForeach()
        {
            AssertCompiles("AliasPair", "VRCSDK3DataDataDictionary",
                new TestSource("AliasPair.cs", @"using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using Entry = System.Collections.Generic.KeyValuePair<string, int>;
public class AliasPair : UdonSharpBehaviour
{
    void Start()
    {
        var d = new Dictionary<string, int>();
        d[""a""] = 1;
        foreach (Entry e in d) { int v = e.Value + 1; Debug.Log(e.Key + v); }
    }
}"));
        }

        [Test]
        public void AliasInPlainClassUsedByBehaviour()
        {
            // 普通のクラスは UdonSharpBehaviour にコピーされる。別名は、コピー先のファイルからは見えない
            AssertCompiles("AliasPlainUser", "VRCSDK3DataDataList",
                new TestSource("AliasPlain.cs", "using IntList = System.Collections.Generic.List<int>;\npublic class AliasPlain { private IntList _xs = new IntList(); public void Add(int v) { _xs.Add(v); } public int At(int i) { int v = _xs[i]; return v; } }"),
                new TestSource("AliasPlainUser.cs", "using UdonSharp;\nusing UnityEngine;\npublic class AliasPlainUser : UdonSharpBehaviour { void Start() { var p = new AliasPlain(); p.Add(4); Debug.Log(p.At(0)); } }"));
        }
    }
}
