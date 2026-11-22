using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// UniTask の async（#207）の中で実行時例外が起きたときの位置（デバッグ情報）が、生成したコルーチンの行ではなく、元のソースの行を指すこと
    /// （#207 の追加テスト案の 6）。async は、文を同じ行数のコルーチンの文に書き換える。複数行の式の 2 行目以降も、元の行を指すこと
    /// </summary>
    public class AsyncDebugInfoTests
    {
        // 行番号は 1 始まり（@" の直後の改行が 1 行目）
        private const string Source = @"
using System.Linq;
using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.AsyDebug
{
    public class Num
    {
        public int v;
        public Num(int v) { this.v = v; }
        public static Num operator +(Num a, Num b) => new Num(a.v + b.v);
        public int Twice() { return v * 2; }
    }

    public class AsyUser : UdonSharpBehaviour
    {
        public int[] a = new int[1];

        void Start() { Foo(3); }

        async UniTask Foo(int n)
        {
            await UniTask.Yield();
            int x = a[n];
            await UniTask.Delay(a[n] * 100);
            await UniTask.WaitUntil(() => a[n] > 0);
            await Child(n);
            int r = await Add(a[n], 2);
            var (p, q) = await UniTask.WhenAll(Add(a[n], 1), Add(a[n], 2));
            int w = await UniTask.WhenAny(Child(n), Child(n));
            var list = new System.Collections.Generic.List<int> { 1 };
            int c = list.Count(z => z > a[n]);
            Num m = new Num(x) + new Num(c);
            int t = m.Twice();
            int y = x
                + a[n];
            await UniTask.Delay(
                a[n]);
            int r2 = await Add(
                a[n], 2);
            Debug.Log(y + t + r + p + q + w + r2);
        }

        async UniTask Child(int n)
        {
            await UniTask.Yield();
            Debug.Log(a[n]);
        }

        async UniTask<int> Add(int u, int v)
        {
            await UniTask.Yield();
            return a[u] + v;
        }

        async UniTask Multi(int n)
        {
            await UniTask.WhenAll(
                Child(a[n]),
                Child(a[n + 1]));
            await UniTask.WaitUntil(
                () => a[n] > 0);
        }
    }
}";

        private static ProgramResult Compile()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("AsyUser.cs", Source));
            Assert.That(result.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", result.Diagnostics));
            return result["AsyUser"];
        }

        private static int[] Lines(DebugMethodInfo m) => m.Positions.Select(p => p.Line + 1).ToArray();

        [Test]
        public void SingleLineStatementsPointToTheirLines()
        {
            ProgramResult program = Compile();
            string dump = string.Join(" / ", program.DebugMethods);
            DebugMethodInfo foo = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Foo"));
            Assert.That(foo, Is.Not.Null, dump);
            Assert.That(foo.FilePath, Does.EndWith("AsyUser.cs"), foo.ToString());
            // Foo のコルーチン（23〜44 行目）の中だけを指す
            Assert.That(Lines(foo), Is.All.InRange(23, 44), foo.ToString());
            Assert.That(Lines(foo), Does.Contain(26), "await のあとの文：" + foo);
            Assert.That(Lines(foo), Does.Contain(27), "Delay の引数の式：" + foo);
            Assert.That(Lines(foo), Does.Contain(28), "WaitUntil：" + foo);
            Assert.That(Lines(foo), Does.Contain(29), "子の async：" + foo);
            Assert.That(Lines(foo), Does.Contain(30), "値を返す async の引数：" + foo);
            Assert.That(Lines(foo), Does.Contain(31), "WhenAll の子の引数：" + foo);
            Assert.That(Lines(foo), Does.Contain(32), "WhenAny：" + foo);
            Assert.That(Lines(foo), Does.Contain(35), "演算子の呼び出し：" + foo);
            Assert.That(Lines(foo), Does.Contain(43), "最後の文：" + foo);

            DebugMethodInfo child = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Child"));
            Assert.That(child, Is.Not.Null, dump);
            Assert.That(Lines(child), Is.All.InRange(46, 50), child.ToString());
            Assert.That(Lines(child), Does.Contain(49), "子の async：" + child);

            DebugMethodInfo add = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Add"));
            Assert.That(add, Is.Not.Null, dump);
            Assert.That(Lines(add), Does.Contain(55), "return の式（値を返す async）：" + add);
        }

        [Test]
        public void MultiLineExpressionsPointToTheirOwnLines()
        {
            ProgramResult program = Compile();
            DebugMethodInfo foo = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Foo"));
            Assert.That(foo, Is.Not.Null, string.Join(" / ", program.DebugMethods));
            Assert.That(Lines(foo), Does.Contain(38), "複数行の式の 2 行目：" + foo);
            Assert.That(Lines(foo), Does.Contain(40), "複数行の await の引数（2 行目）：" + foo);
            Assert.That(Lines(foo), Does.Contain(42), "複数行の値を返す await の引数（2 行目）：" + foo);

            DebugMethodInfo multi = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Multi"));
            Assert.That(multi, Is.Not.Null, string.Join(" / ", program.DebugMethods));
            Assert.That(Lines(multi), Does.Contain(61), "WhenAll の 1 つ目の子（複数行）：" + multi);
            Assert.That(Lines(multi), Does.Contain(62), "WhenAll の 2 つ目の子（複数行）：" + multi);
            // WaitUntil の条件が 2 行目にあるとき（64 行目）は、コルーチンの段が条件を 1 行にまとめるので、元の行（63 行目）を指す。コルーチンと同じ（#107）
            Assert.That(Lines(multi), Does.Contain(63), "WaitUntil：" + multi);
        }

        [Test]
        public void LambdaOperatorAndPlainClassInAsyncPointToTheirLines()
        {
            ProgramResult program = Compile();
            string dump = string.Join("\n", program.DebugMethods);
            // LINQ のラムダ（34 行目）・演算子の本体（13 行目）・普通のクラスのメソッド（14 行目）は、生成したメソッドでも元の行を指す
            // （WaitUntil のラムダ（28 行目）は、コルーチンのメソッドの中に入るので、SingleLineStatementsPointToTheirLines で見る）
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("AsyUser.cs") && Lines(m).Contains(34) && !m.MethodName.StartsWith("__co_Foo")), "LINQ のラムダ：\n" + dump);
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("AsyUser.cs") && Lines(m).Length > 0 && Lines(m).All(l => l == 13)), "演算子：\n" + dump);
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("AsyUser.cs") && Lines(m).Length > 0 && Lines(m).All(l => l == 14)), "普通のクラスのメソッド：\n" + dump);
        }
    }
}
