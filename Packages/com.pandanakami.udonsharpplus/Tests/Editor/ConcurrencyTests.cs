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

        // ---- デリゲート（#126 D4） ------------------------------------------------

        private const string DelegateShared = @"
namespace UdonSharpPlusTests.ConcurrencyDelegate
{
    public delegate int Calc(int x);

    public class Holder
    {
        public Calc calc;
        public int Run(int x) => calc(x);
    }
}";

        private static string DelegateUser(int i) => $@"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ConcurrencyDelegate
{{
    public class DUser{i} : UdonSharpBehaviour
    {{
        private static int Twice(int x) => x * 2;
        private int Add(int x) => x + {i};

        private Calc Adder(int k) => x => x + k + {i};

        void Start()
        {{
            Calc c = Twice;
            var h = new Holder();
            h.calc = Add;
            int k = {i};
            Calc closure = x => {{ k += x; return k; }};
            Calc[] loop = new Calc[2];
            for (int j = 0; j < 2; j++) {{ int jj = j; loop[j] = x => x * jj + k; }}
            Debug.Log(c({i}) + h.Run(1) + closure(1) + Adder(2)(3) + loop[1](2));
        }}
    }}
}}";

        private static string Uasm(CompileResult result) =>
            string.Join("\n", result.Programs.OrderBy(p => p.TypeName, StringComparer.Ordinal).Select(p => p.TypeName + "\n" + p.Uasm));

        /// <summary>デリゲートの書き換え（中継メソッド・呼び出しの static クラスのコピー・クロージャ。#129 D4）は、並列で生成しても順番に生成したときと同じ</summary>
        [Test]
        public void ParallelEmitKeepsDelegatePrograms()
        {
            TestSource[] sources = new[] { new TestSource("DelegateShared.cs", DelegateShared) }
                .Concat(Enumerable.Range(0, Programs).Select(i => new TestSource($"DUser{i}.cs", DelegateUser(i)))).ToArray();
            bool saved = UdonSharpTestCompiler.ParallelEmit;
            try
            {
                UdonSharpTestCompiler.ParallelEmit = false;
                CompileResult sequential = UdonSharpTestCompiler.Compile(sources);
                Assert.That(sequential.Diagnostics, Is.Empty);
                Assert.That(sequential.Programs.Count, Is.EqualTo(Programs));
                string expected = Uasm(sequential);
                for (int run = 0; run < 3; run++)
                {
                    UdonSharpTestCompiler.ParallelEmit = true;
                    CompileResult parallel = UdonSharpTestCompiler.Compile(sources);
                    Assert.That(parallel.Diagnostics, Is.Empty);
                    Assert.That(Uasm(parallel), Is.EqualTo(expected), $"{run + 1} 回目");
                }
            }
            finally
            {
                UdonSharpTestCompiler.ParallelEmit = saved;
            }
        }

        /// <summary>
        /// 別のスレッドで書き換え（U# のコンパイル。CI では、プロジェクトを読み込んだときに裏で走る）が動いていても、
        /// テストのコンパイルの書き換えた結果が変わらない（位置の印の番号などが混ざらない）。
        /// 書き換えは InterfaceLowering.ProcessLock で 1 つずつにし、テストのコンパイラーはコンパイル全体をその中で行う（#164 の CI で発覚）
        /// </summary>
        [Test]
        public void BackgroundLoweringDoesNotMixIntoCompile()
        {
            const string background = "using UdonSharp; public class BgP { public int v; public BgP(int v) { this.v = v; } public int Get() { int a = v; a += 1; return a; } }"
                                      + " public class Bg : UdonSharpBehaviour { void Start() { var p = new { A = 1, B = \"x\" }; var q = new BgP(2); int n = q.Get() + p.A; } }";
            const string main = "using UdonSharp; public class FgP { public int v; public FgP(int v) { this.v = v; } public int Twice() { int a = v; return a * 2; } }"
                                + " public class Fg : UdonSharpBehaviour { void Start() { var p = new { Name = \"a\", Score = 1 }; var q = new FgP(3); string s = p.ToString() + q.Twice(); } }";

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type bindingType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UdonSharp.Compiler.ModuleBinding")).First(t => t != null);
            FieldInfo treeField = bindingType.GetField("tree", all);
            FieldInfo pathField = bindingType.GetField("filePath", all);
            FieldInfo textField = bindingType.GetField("sourceText", all);

            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusBackground"));
            string Lowered()
            {
                if (System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, recursive: true);
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, new TestSource("Fg.cs", main));
                Assert.That(r.Diagnostics, Is.Empty);
                return System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "Assets", "UdonSharpPlusTests", "Fg.cs"));
            }

            string expected = Lowered();
            bool stop = false;
            int runs = 0;
            Exception error = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    while (!System.Threading.Volatile.Read(ref stop))
                    {
                        Array bindings = Array.CreateInstance(bindingType, 1);
                        object binding = Activator.CreateInstance(bindingType, true);
                        treeField.SetValue(binding, Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(background));
                        pathField.SetValue(binding, "Assets/Bg.cs");
                        textField.SetValue(binding, background);
                        bindings.SetValue(binding, 0);
                        InterfaceLowering.Process(bindings, treeField, textField, null);
                        System.Threading.Interlocked.Increment(ref runs);
                    }
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            bool dump = InterfaceLowering.DumpEnabled;
            InterfaceLowering.DumpEnabled = false;   // 裏の書き換えは Library に出さない（テストのコンパイルの間は、テストが出し先を決める）
            thread.Start();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(Lowered(), Is.EqualTo(expected), $"{i + 1} 回目");
                    System.Threading.Thread.Sleep(20);
                }
            }
            finally
            {
                System.Threading.Volatile.Write(ref stop, true);
                thread.Join();
                InterfaceLowering.DumpEnabled = dump;
                if (System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, recursive: true);
            }
            Assert.That(error, Is.Null, error?.ToString());
            Assert.That(runs, Is.GreaterThan(0), "裏の書き換えが動いていない");
        }
    }
}
