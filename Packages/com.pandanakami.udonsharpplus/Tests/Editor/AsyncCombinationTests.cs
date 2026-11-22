using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// UniTask の async（#207）と、ほかの機能・継承・partial の組み合わせ（#207 の追加テスト案の 4・7）。
    /// ほかの段（匿名型・タプル・演算子・LINQ・デリゲート）が、await をまたぐ変数・await の前後の式でも、コンパイルできること
    /// </summary>
    public class AsyncCombinationTests
    {
        private const string Head = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\n";

        private static CompileResult Compile(params string[] sources) =>
            UdonSharpTestCompiler.Compile(sources.Select((s, i) => new TestSource($"AsyCombo{i}.cs", Head + s)).ToArray());

        private static void AssertOk(CompileResult r, string what) =>
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, what + "\n" + string.Join("\n", r.Diagnostics));

        [Test]
        public void AnonymousTypesTuplesOperatorsLinqAndDelegatesAcrossAwait()
        {
            CompileResult r = Compile(@"
public class Num
{
    public int v;
    public Num(int v) { this.v = v; }
    public static Num operator +(Num a, Num b) => new Num(a.v + b.v);
}",
@"
public class AsyMix : UdonSharpBehaviour
{
    public int n;
    void Start() { Run(); }
    async UniTask Run()
    {
        var anon = new { A = 1, B = ""x"" };
        var tup = (1, 2);
        Num m = new Num(1) + new Num(2);
        var list = new List<int> { 3, 1, 2 };
        var dict = new Dictionary<string, int> { { ""a"", 1 } };
        await UniTask.Yield();
        n = anon.A + tup.Item2 + m.v + list.Where(x => x > 1).Sum() + dict[""a""];
        var groups = list.GroupBy(x => x % 2).ToList();
        int r = await Add(m.v, tup.Item1);
        Func<int, int> f = x => x + r;
        await UniTask.Delay(1);
        n += f(1) + groups.Count + anon.B.Length;
        int one = await Add(1, 1);
        Num next = m + new Num(one);
        n += next.v;
    }
    async UniTask<int> Add(int a, int b) { await UniTask.Yield(); return a + b; }
}");
            AssertOk(r, "他の機能 × await");
        }

        [Test]
        public void PartialClassAcrossFiles()
        {
            CompileResult r = Compile(@"
public partial class AsyPart : UdonSharpBehaviour
{
    public int n;
    void Start() { Go(); }
}",
@"
public partial class AsyPart
{
    async UniTask Go() { await UniTask.Yield(); n = await Add(1, 2); }
    async UniTask<int> Add(int a, int b) { await UniTask.Yield(); return a + b; }
}");
            AssertOk(r, "partial");
        }

        [Test]
        public void VirtualAndOverrideAsyncMethods()
        {
            CompileResult r = Compile(@"
public class AsyBase : UdonSharpBehaviour
{
    public int n;
    void Start() { Step(1); }
    public virtual async UniTask Step(int k) { await UniTask.Yield(); n += k; }
}",
@"
public class AsyDerived : AsyBase
{
    public override async UniTask Step(int k) { await base.Step(k); n *= 2; }
}");
            AssertOk(r, "virtual / override");
        }

        [Test]
        public void VirtualAndOverrideAsyncMethodsWithValues()
        {
            CompileResult r = Compile(@"
public class AsyBaseV : UdonSharpBehaviour
{
    public int n;
    void Start() { Run(); }
    async UniTask Run() { n = await Calc(2); }
    public virtual async UniTask<int> Calc(int k) { await UniTask.Yield(); return k; }
}",
@"
public class AsyDerivedV : AsyBaseV
{
    public override async UniTask<int> Calc(int k) { int b = await base.Calc(k); return b * 2; }
}",
@"
public class AsyEvents : UdonSharpBehaviour
{
    public int n;
    public virtual void OnHit(int k) { n += k; }
    public override async void Interact() { await UniTask.Yield(); n++; }
}",
@"
public class AsyEventsDerived : AsyEvents
{
    public override async void OnHit(int k) { await UniTask.Yield(); n += k * 2; }
    public override async void Interact() { await UniTask.Yield(); n += 10; }
}");
            AssertOk(r, "値を返す virtual / override・イベントの override の override");
        }

        [Test]
        public void AbstractAsyncDeclarationIsAClearError()
        {
            CompileResult r = Compile(@"
public abstract class AsyAbs : UdonSharpBehaviour
{
    public abstract UniTask Do();
}",
@"
public class AsyImpl : AsyAbs
{
    public override async UniTask Do() { await UniTask.Yield(); }
}");
            Assert.That(r.Diagnostics.Any(d => d.Contains("async でないメソッドは未対応") || d.Contains("without async")), string.Join("\n", r.Diagnostics));
        }
    }
}
