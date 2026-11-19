using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// デリゲートの呼び出しの、相手が複数のときの部品（__many・__loop・__combine）は、コンパイルするコードのどこかで
    /// その型に + / += を使っているときだけ入れる（Redmine #173）。つないだデリゲートは、ほかの UdonSharpBehaviour に渡されて
    /// そこで呼ばれることがあるので、+= を書いていないプログラムでも、型として使われていれば入れる
    /// </summary>
    public class DelegateMulticastPartsTests
    {
        private const string Head = "using System;\nusing UdonSharp;\nusing UnityEngine;\n";

        private static CompileResult Compile(params TestSource[] sources)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(sources);
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            return r;
        }

        private static bool HasMulticastParts(CompileResult r, string type) => r[type].Uasm.Contains("__loop");

        [Test]
        public void NotAddedWithoutCombine()
        {
            CompileResult r = Compile(new TestSource("McNone.cs", Head + @"public delegate int McCalc(int x);
public class McNone : UdonSharpBehaviour
{
    private int Twice(int x) => x * 2;
    void Start() { McCalc c = Twice; Func<int, int> f = x => x + 1; c -= Twice; Debug.Log(f(1) + (c == null ? 0 : c(2))); }
}"));
            Assert.That(HasMulticastParts(r, "McNone"), Is.False);
            Assert.That(r["McNone"].Uasm, Does.Not.Contain("__combine"));
        }

        [Test]
        public void AddedToCallerWhenAnotherBehaviourCombines()
        {
            CompileResult r = Compile(
                new TestSource("McTypes.cs", Head + "public delegate int McChain(int x);"),
                new TestSource("McMaker.cs", Head + @"public class McMaker : UdonSharpBehaviour
{
    public McCaller caller;
    private int A(int x) => x + 1;
    private int B(int x) => x * 2;
    void Start() { McChain c = A; c += B; caller.Run(c); }
}"),
                new TestSource("McCaller.cs", Head + @"public class McCaller : UdonSharpBehaviour
{
    public void Run(McChain c) { Debug.Log(c(3)); }
}"));
            // UdonSharp は、呼ばれない static メソッドをプログラムに入れない。McMaker はつなぐだけ（__combine）で、呼ぶのは McCaller
            Assert.That(r["McMaker"].Uasm, Does.Contain("__combine"));
            Assert.That(HasMulticastParts(r, "McCaller"), Is.True, "+= を書いていないが、つないだものを受け取って呼ぶ");
        }

        [Test]
        public void AddedForBinaryPlus()
        {
            CompileResult r = Compile(new TestSource("McPlus.cs", Head + @"public class McPlus : UdonSharpBehaviour
{
    private void A(int x) { Debug.Log(x); }
    void Start() { Action<int> a = A; Action<int> b = a + a; b(1); }
}"));
            Assert.That(HasMulticastParts(r, "McPlus"), Is.True);
        }

        [Test]
        public void AddedForCombineInsidePlainClass()
        {
            CompileResult r = Compile(
                new TestSource("McPlainBag.cs", Head + @"public class McPlainBag
{
    public Func<int, int> f;
    public void Add(Func<int, int> g) { f += g; }
}"),
                new TestSource("McPlainUser.cs", Head + @"public class McPlainUser : UdonSharpBehaviour
{
    void Start() { var bag = new McPlainBag(); bag.Add(x => x + 1); bag.Add(x => x * 3); Debug.Log(bag.f(2)); }
}"));
            Assert.That(HasMulticastParts(r, "McPlainUser"), Is.True);
        }

        [Test]
        public void OnlyTheCombinedTypeGetsParts()
        {
            CompileResult r = Compile(new TestSource("McTwo.cs", Head + @"public class McTwo : UdonSharpBehaviour
{
    private int A(int x) => x;
    private void B(string s) { Debug.Log(s); }
    void Start() { Func<int, int> f = A; Action<string> g = B; g += B; g(""x""); Debug.Log(f(1)); }
}"));
            string uasm = r["McTwo"].Uasm;
            // Action<string> の部品だけが入る（Func<int, int> の __loop は入らない）
            Assert.That(uasm, Does.Contain("System_Action_string_____loop"));
            Assert.That(uasm, Does.Not.Contain("System_Func_int__int_____loop"));
        }
    }
}
