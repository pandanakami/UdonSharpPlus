using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// U# の並列処理（BindAllPrograms・EmitAllPrograms・AssembleProgram はプログラムごとに並列）から呼ばれるパッチが、
    /// 同時に呼ばれても結果が変わらないこと（Redmine #113。#112 と同じ観点）
    /// </summary>
    public class ConcurrencyTests
    {
        private const int Programs = 20;

        // どの UdonSharpBehaviour からも使う普通のクラス。Bad の中のエラーは、コピーごとに出るが 1 つにまとめる
        private const string Shared = @"
namespace UdonSharpPlusTests.Concurrency
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

    public class Bad
    {
        public string path = ""a"";
        public bool Exists() => System.IO.File.Exists(path);
    }
}";

        private static string User(int i, bool withError) => $@"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Concurrency
{{
    public class User{i} : UdonSharpBehaviour
    {{
        void Start()
        {{
            var c = new Counter();
            Debug.Log(c.Get({i % 2}));{(withError ? @"
            Debug.Log(new Bad().Exists());" : "")}
        }}
    }}
}}";

        private static TestSource[] Sources(bool withError) =>
            new[] { new TestSource("Shared.cs", Shared) }
                .Concat(Enumerable.Range(0, Programs).Select(i => new TestSource($"User{i}.cs", User(i, withError))))
                .ToArray();

        private static CompileResult Compile(bool parallel, bool withError)
        {
            bool saved = UdonSharpTestCompiler.ParallelEmit;
            UdonSharpTestCompiler.ParallelEmit = parallel;
            try
            {
                return UdonSharpTestCompiler.Compile(Sources(withError));
            }
            finally
            {
                UdonSharpTestCompiler.ParallelEmit = saved;
            }
        }

        private static string DebugInfo(CompileResult result) =>
            string.Join("\n", result.Programs.OrderBy(p => p.TypeName, StringComparer.Ordinal).Select(p => p.TypeName + ": " + string.Join(" / ", p.DebugMethods.Select(m => m.ToString()).OrderBy(x => x, StringComparer.Ordinal))));

        /// <summary>
        /// 普通のクラスのコピーのデバッグ情報（DebugInfoPatch）は、プログラムを並列で生成しても、順番に生成したときと同じ
        /// （どのプログラムでも、コピーは元のクラスのソースを指す）
        /// </summary>
        [Test]
        public void ParallelEmitKeepsDebugInfo()
        {
            CompileResult sequential = Compile(parallel: false, withError: false);
            Assert.That(sequential.Diagnostics, Is.Empty);
            Assert.That(sequential.Programs.Count, Is.EqualTo(Programs));
            string expected = DebugInfo(sequential);
            Assert.That(expected, Does.Contain("Shared.cs"), "コピーが元のクラスを指していません");

            for (int run = 0; run < 3; run++)
            {
                CompileResult parallel = Compile(parallel: true, withError: false);
                Assert.That(parallel.Diagnostics, Is.Empty);
                Assert.That(DebugInfo(parallel), Is.EqualTo(expected), $"{run + 1} 回目");
            }
        }

        /// <summary>
        /// どのプログラムのコピーの中でも同じエラーは、元のクラスの位置の 1 つにまとまる（DiagnosticLocationPatch）。
        /// U# の Bind は並列なので、何回コンパイルしても数と位置が同じこと
        /// </summary>
        [Test]
        public void ParallelBindMergesSameDiagnostics()
        {
            string first = null;
            for (int run = 0; run < 3; run++)
            {
                CompileResult result = Compile(parallel: true, withError: true);
                Assert.That(result.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", result.Diagnostics));
                Assert.That(result.Diagnostics[0], Does.Contain("Shared.cs"));
                first = first ?? result.Diagnostics[0];
                Assert.That(result.Diagnostics[0], Is.EqualTo(first), $"{run + 1} 回目");
            }
        }

        public class Item { }
        public interface IItem { }

        /// <summary>シリアライザーのパッチ（LoweredFieldSerializerPatch）を、たくさんのスレッドから同時に呼んでも、同じ答えになる</summary>
        [Test]
        public void SerializerPatchFromManyThreads()
        {
            Type[] types = { typeof(Item), typeof(IItem), typeof(List<int>), typeof(Item[]), typeof(int), typeof(string) };
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, 200, i =>
            {
                Type type = types[i % types.Length];
                try
                {
                    object serializer = LoweredFieldSerializerTests.CreatePooled(type);
                    bool lowered = serializer.GetType().Name.StartsWith("LoweredFieldSerializer", StringComparison.Ordinal);
                    bool expected = type != typeof(int) && type != typeof(string);
                    if (lowered != expected)
                        errors.Add($"{type}: {serializer.GetType().Name}");
                }
                catch (TargetInvocationException e)
                {
                    errors.Add($"{type}: {e.InnerException}");
                }
            });
            Assert.That(errors, Is.Empty);
        }
    }
}
