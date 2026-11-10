using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 汎用クラスのコピーのデバッグ情報（実行時例外の位置を引くもの）が、元のクラスのソースを指すこと（Redmine #53）
    /// </summary>
    public class DebugInfoTests
    {
        // 行番号は 1 始まり（@" の直後の改行が 1 行目）
        private const string Counter = @"
namespace UdonSharpPlusTests.DebugInfo
{
    public class Counter
    {
        public int[] values = new int[2];

        public int Get(int i)
        {
            int v = values[i];
            return v * 2;
        }
    }
}";

        private const string User = @"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DebugInfo
{
    public class User : UdonSharpBehaviour
    {
        void Start()
        {
            var c = new Counter();
            Debug.Log(c.Get(5));
        }
    }
}";

        private static ProgramResult Compile()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("Counter.cs", Counter), new TestSource("User.cs", User));
            Assert.That(result.Diagnostics, Is.Empty);
            return result["User"];
        }

        [Test]
        public void CopiedMethodsPointToOriginalClass()
        {
            ProgramResult program = Compile();
            string dump = string.Join(" / ", program.DebugMethods);
            var copies = program.DebugMethods.Where(m => m.FilePath.EndsWith("Counter.cs")).ToList();

            // Get のコピー：元のメソッドの行（8〜12 行目）だけを指し、2 つの文（10・11 行目）を両方含む
            // （最後の文も、1 つ前の文の位置にならない）
            DebugMethodInfo get = copies.FirstOrDefault(m => m.MethodName.Contains("Get"));
            Assert.That(get, Is.Not.Null, dump);
            Assert.That(get.Positions, Is.Not.Empty, get.ToString());
            Assert.That(get.Positions.Select(p => p.Line + 1), Is.All.InRange(8, 12), get.ToString());
            Assert.That(get.Positions.Select(p => p.Line + 1), Does.Contain(10), get.ToString());
            Assert.That(get.Positions.Select(p => p.Line + 1), Does.Contain(11), get.ToString());

            // フィールドの初期化子（6 行目）
            DebugMethodInfo init = copies.FirstOrDefault(m => m.MethodName.Contains("init"));
            Assert.That(init, Is.Not.Null, dump);
            Assert.That(init.Positions.Select(p => p.Line + 1), Is.All.EqualTo(6), init.ToString());
        }

        [Test]
        public void OwnMethodsAreUnchanged()
        {
            ProgramResult program = Compile();
            DebugMethodInfo start = program.DebugMethods.FirstOrDefault(m => m.MethodName == "Start");
            Assert.That(start, Is.Not.Null, string.Join(" / ", program.DebugMethods));
            Assert.That(start.FilePath, Does.EndWith("User.cs"), start.ToString());
            Assert.That(start.Positions.Select(p => p.Line + 1), Is.All.InRange(10, 13), start.ToString());
        }

        // ラムダ（#57）：生成メソッドが、元のラムダの行を指す
        private const string LambdaUser = @"
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DebugInfoLambda
{
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();

        void Start()
        {
            int[] table = new int[2];
            int hit = _list.Find(x => table[x] > 0);
            _list.ForEach(x =>
            {
                int v = table[x];
                Debug.Log(v);
            });
        }
    }
}";

        [Test]
        public void LambdaMethodsPointToLambda()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("User.cs", LambdaUser));
            Assert.That(result.Diagnostics, Is.Empty);
            ProgramResult program = result["User"];
            string dump = string.Join(" / ", program.DebugMethods);

            // Find のラムダ（15 行目）
            DebugMethodInfo find = program.DebugMethods.FirstOrDefault(m => m.MethodName.Contains("__lambda_Find"));
            Assert.That(find, Is.Not.Null, dump);
            Assert.That(find.FilePath, Does.EndWith("User.cs"), find.ToString());
            Assert.That(find.Positions.Select(p => p.Line + 1), Is.All.EqualTo(15), find.ToString());

            // ForEach のブロックのラムダ（16〜20 行目）。中の 2 つの文（18・19 行目）を両方含む
            DebugMethodInfo forEach = program.DebugMethods.FirstOrDefault(m => m.MethodName.Contains("__lambda_ForEach"));
            Assert.That(forEach, Is.Not.Null, dump);
            Assert.That(forEach.Positions.Select(p => p.Line + 1), Is.All.InRange(16, 20), forEach.ToString());
            Assert.That(forEach.Positions.Select(p => p.Line + 1), Does.Contain(18), forEach.ToString());
            Assert.That(forEach.Positions.Select(p => p.Line + 1), Does.Contain(19), forEach.ToString());
        }

        // ジェネリッククラスのコピー（#95）：Box<int> は Box__int になり、さらに普通のクラスとして使う側にコピーされる。
        // 2 段の書き換えのあとも、元のジェネリッククラスの行を指す
        private const string Box = @"
namespace UdonSharpPlusTests.DebugInfoGeneric
{
    public class Box<T>
    {
        public T[] values = new T[2];

        public T Get(int i)
        {
            T v = values[i];
            return v;
        }
    }
}";

        private const string GenericUser = @"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DebugInfoGeneric
{
    public class User : UdonSharpBehaviour
    {
        void Start()
        {
            var b = new Box<int>();
            Debug.Log(b.Get(5));
        }
    }
}";

        [Test]
        public void GenericCopiesPointToGenericClass()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("Box.cs", Box), new TestSource("User.cs", GenericUser));
            Assert.That(result.Diagnostics, Is.Empty);
            ProgramResult program = result["User"];
            string dump = string.Join(" / ", program.DebugMethods);
            var copies = program.DebugMethods.Where(m => m.FilePath.EndsWith("Box.cs")).ToList();

            // Get のコピー：元のメソッドの行（8〜12 行目）だけを指し、2 つの文（10・11 行目）を両方含む
            DebugMethodInfo get = copies.FirstOrDefault(m => m.MethodName.Contains("Get"));
            Assert.That(get, Is.Not.Null, dump);
            Assert.That(get.Positions.Select(p => p.Line + 1), Is.All.InRange(8, 12), get.ToString());
            Assert.That(get.Positions.Select(p => p.Line + 1), Does.Contain(10), get.ToString());
            Assert.That(get.Positions.Select(p => p.Line + 1), Does.Contain(11), get.ToString());

            // フィールドの初期化子（6 行目）
            DebugMethodInfo init = copies.FirstOrDefault(m => m.MethodName.Contains("init"));
            Assert.That(init, Is.Not.Null, dump);
            Assert.That(init.Positions.Select(p => p.Line + 1), Is.All.EqualTo(6), init.ToString());
        }
    }
}
