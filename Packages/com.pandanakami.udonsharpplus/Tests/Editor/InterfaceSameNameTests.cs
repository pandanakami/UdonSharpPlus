using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 別々のインタフェースに、名前と引数が同じで戻り値の型が違うメソッドがあるとき（#140 で見つけた不具合）。
    /// 呼び出しの補助メソッドを同じ名前で 1 つにまとめていたため、片方の戻り値の型になり、C# のエラー（CS0029）になっていた
    /// </summary>
    public class InterfaceSameNameTests
    {
        private const string Source = @"using UdonSharp;
using UnityEngine;

public interface ICount { int Size(); }
public interface ILabel { string Size(); }
public interface IPair { (int, int) Size(); }
public interface ISame { int Size(); }
public class Box : ICount, ISame { public int n; public int Size() => n; }
public class Tag : ILabel { public string s; public string Size() => s; }
public class Two : IPair { public (int, int) Size() => (1, 2); }
public class Big : ICount { public int Size() => 100; }

public class SameNameUser : UdonSharpBehaviour
{
    private void Start()
    {
        ICount c = new Box();
        ILabel l = new Tag();
        IPair p = new Two();
        ISame s = new Box();
        ICount b = new Big();
        int n = c.Size() + s.Size() + b.Size();
        string t = l.Size();
        int first = p.Size().Item1;
        Debug.Log(n + t + first);
    }
}";

        [Test]
        public void CompilesWithDifferentReturnTypes()
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("SameNameUser.cs", Source));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
        }
    }
}
