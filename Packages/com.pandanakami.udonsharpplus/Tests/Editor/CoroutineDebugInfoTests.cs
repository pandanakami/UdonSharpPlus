using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// コルーチン（#107）の中で実行時例外が起きたときの位置（デバッグ情報）が、生成した __co_* の行ではなく、元のソースの行を指すこと
    /// （#107 の追加テスト案の 14）。状態機械は元の行のまま書き換え、ほかの段（ラムダ・演算子・普通のクラス）のコピーは #52 の印で戻す
    /// </summary>
    public class CoroutineDebugInfoTests
    {
        // 行番号は 1 始まり（@" の直後の改行が 1 行目）
        private const string Source = @"
using System.Collections;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CoDebug
{
    public class Num
    {
        public int v;
        public Num(int v) { this.v = v; }
        public static Num operator +(Num a, Num b) => new Num(a.v + b.v);
        public int Twice() { return v * 2; }
    }

    public class CoUser : UdonSharpBehaviour
    {
        public int[] a = new int[1];

        void Start() { StartCoroutine(Foo(3)); }

        IEnumerator Foo(int n)
        {
            yield return null;
            int x = a[n];
            for (int i = 0; i < 2; i++)
            {
                foreach (int v in a)
                {
                    yield return null;
                    x += a[v + n];
                }
            }
            yield return new WaitUntil(() => a[n] > 0);
            yield return StartCoroutine(Child(n));
            var list = new System.Collections.Generic.List<int> { 1 };
            int c = list.Count(z => z > a[n]);
            Num m = new Num(x) + new Num(c);
            int t = m.Twice();
            int y = x
                + a[n];
            Debug.Log(y + t);
        }

        IEnumerator Child(int n)
        {
            yield return null;
            Debug.Log(a[n]);
        }
    }
}";

        private static ProgramResult Compile()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("CoUser.cs", Source));
            Assert.That(result.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", result.Diagnostics));
            return result["CoUser"];
        }

        private static int[] Lines(DebugMethodInfo m) => m.Positions.Select(p => p.Line + 1).ToArray();

        [Test]
        public void StateMachineKeepsOriginalLines()
        {
            ProgramResult program = Compile();
            string dump = string.Join(" / ", program.DebugMethods);
            DebugMethodInfo foo = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Foo"));
            Assert.That(foo, Is.Not.Null, dump);
            Assert.That(foo.FilePath, Does.EndWith("CoUser.cs"), foo.ToString());
            // 状態機械のメソッドは、元のメソッド（23〜44 行目）の中だけを指す
            Assert.That(Lines(foo), Is.All.InRange(23, 44), foo.ToString());
            Assert.That(Lines(foo), Does.Contain(26), "yield の直後：" + foo);
            Assert.That(Lines(foo), Does.Contain(32), "入れ子のループの本体：" + foo);
            Assert.That(Lines(foo), Does.Contain(35), "WaitUntil の条件：" + foo);
            Assert.That(Lines(foo), Does.Contain(39), "演算子の呼び出し：" + foo);
            Assert.That(Lines(foo), Does.Contain(42), "複数行の式の 2 行目：" + foo);

            DebugMethodInfo child = program.DebugMethods.FirstOrDefault(m => m.MethodName.StartsWith("__co_Child"));
            Assert.That(child, Is.Not.Null, dump);
            Assert.That(Lines(child), Is.All.InRange(46, 50), child.ToString());
            Assert.That(Lines(child), Does.Contain(49), "子のコルーチン：" + child);
        }

        [Test]
        public void LambdaOperatorAndPlainClassInCoroutinePointToTheirLines()
        {
            ProgramResult program = Compile();
            string dump = string.Join("\n", program.DebugMethods);
            // ラムダ（38 行目）・演算子の本体（13 行目）・普通のクラスのメソッド（14 行目）は、生成したメソッドでも元の行を指す
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("CoUser.cs") && Lines(m).Contains(38) && !m.MethodName.StartsWith("__co_Foo")), "ラムダ：\n" + dump);
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("CoUser.cs") && Lines(m).Length > 0 && Lines(m).All(l => l == 13)), "演算子：\n" + dump);
            Assert.That(program.DebugMethods.Any(m => m.FilePath.EndsWith("CoUser.cs") && Lines(m).Length > 0 && Lines(m).All(l => l == 14)), "普通のクラスのメソッド：\n" + dump);
        }
    }
}
