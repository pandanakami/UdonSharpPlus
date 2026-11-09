using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>テスト基盤（UdonSharpTestCompiler）そのものが動くことの確認</summary>
    public class TestCompilerTests
    {
        [Test]
        public void CompilesPlainBehaviour()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("PlainBehaviour.cs", @"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Plain
{
    public class PlainBehaviour : UdonSharpBehaviour
    {
        public int hp = 3;
        public int Add(int a, int b) { return a + b; }
        void Start() { Debug.Log(Add(hp, 2)); }
    }
}"));

            Assert.That(result.Diagnostics, Is.Empty);
            ProgramResult program = result["PlainBehaviour"];
            Assert.That(program.EntryPoints, Is.EquivalentTo(new[] { "__0_Add", "_start" }));
            Assert.That(program.ExportedSymbols, Does.Contain("hp"));
            Assert.That(program.HeapDefaults["__refl_typename"], Is.EqualTo("\"UdonSharpPlusTests.Plain.PlainBehaviour\""));
        }

        [Test]
        public void ReportsRoslynErrors()
        {
            var result = UdonSharpTestCompiler.Compile(new TestSource("Broken.cs", @"
using UdonSharp;
public class Broken : UdonSharpBehaviour { void Start() { int x = ""str""; } }"));

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Programs, Is.Empty);
            Assert.That(result.Diagnostics.Single(), Does.Contain("CS0029"));
        }

        [Test]
        public void ReportsUdonSharpErrors()
        {
            // U# 本体の診断（[NetworkCallable] は戻り値を持てない）
            var result = UdonSharpTestCompiler.Compile(new TestSource("BadNetwork.cs", @"
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
public class BadNetwork : UdonSharpBehaviour { [NetworkCallable] public int Get(int a) { return a; } }"));

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Diagnostics, Has.Some.Contains("cannot have a return type"));
        }
    }
}
