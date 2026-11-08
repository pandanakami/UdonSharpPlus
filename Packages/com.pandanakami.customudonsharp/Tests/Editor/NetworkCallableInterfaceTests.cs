using System.Linq;
using NUnit.Framework;

namespace CustomUdonSharp.Tests
{
    /// <summary>[NetworkCallable] なインタフェース実装メソッドと、Udon 上の名前（Redmine #32, #51）</summary>
    public class NetworkCallableInterfaceTests
    {
        private static readonly TestSource Receiver = new TestSource("IReceiver.cs", @"
namespace CustomUdonSharpTests.Network
{
    public interface IReceiver
    {
        void Receive(int value, string from);
    }
}");

        private static readonly TestSource Target = new TestSource("NetworkTarget.cs", @"
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace CustomUdonSharpTests.Network
{
    public class NetworkTarget : UdonSharpBehaviour, IReceiver
    {
        [NetworkCallable]
        public void Receive(int v, string sender) { Debug.Log(v); }
    }
}");

        private static TestSource Caller(string body) => new TestSource("NetworkCaller.cs", @"
using UdonSharp;
using UnityEngine;
using VRC.Udon.Common.Interfaces;

namespace CustomUdonSharpTests.Network
{
    public class NetworkCaller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;

        void Start()
        {
            " + body + @"
        }
    }
}");

        [Test]
        public void ImplementationIsExportedWithOriginalName()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target);

            Assert.That(result.Diagnostics, Is.Empty);
            ProgramResult target = result["NetworkTarget"];
            Assert.That(target.EntryPoints, Is.EquivalentTo(new[] { "Receive" }));

            // メソッド名は元の名前、引数は位置で決める固定名（#51）
            NetworkEventInfo networkEvent = target.NetworkEvents.Single();
            Assert.That(networkEvent.Name, Is.EqualTo("Receive"));
            Assert.That(networkEvent.Parameters.Select(p => p.Name),
                Is.EqualTo(new[] { "Receive__int__string__p0", "Receive__int__string__p1" }));
        }

        // イベント名はどの書き方でも元の名前のまま届く（書き換えない）
        [TestCase("((UdonSharpBehaviour)(IReceiver)target).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(IReceiver.Receive), 1, \"a\");", TestName = "Interface nameof")]
        [TestCase("((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetworkTarget.Receive), 1, \"a\");", TestName = "Concrete nameof")]
        [TestCase("((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, \"Receive\", 1, \"a\");", TestName = "Concrete literal")]
        [TestCase("target.SendCustomNetworkEvent(NetworkEventTarget.All, \"Receive\", 1, \"a\");", TestName = "UdonSharpBehaviour literal")]
        [TestCase("((IUdonEventReceiver)target).SendCustomNetworkEvent(NetworkEventTarget.All, \"Receive\", 1, \"a\");", TestName = "IUdonEventReceiver literal")]
        [TestCase("string ev = \"Receive\"; target.SendCustomNetworkEvent(NetworkEventTarget.All, ev, 1, \"a\");", TestName = "Variable")]
        public void EventNameIsOriginalName(string call)
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target, Caller(call));

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Logs, Is.Empty);
            var strings = result["NetworkCaller"].HeapDefaults.Values.ToList();
            Assert.That(strings, Does.Contain("\"Receive\""));
            Assert.That(strings, Does.Not.Contain("\"Receive__int__string\""));
        }

        [Test]
        public void LocalCallThroughInterfaceUsesOriginalName()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target, Caller("((IReceiver)target).Receive(1, \"a\");"));

            Assert.That(result.Diagnostics, Is.Empty);
            var strings = result["NetworkCaller"].HeapDefaults.Values.ToList();
            Assert.That(strings, Does.Contain("\"Receive\""));
            Assert.That(strings, Does.Contain("\"Receive__int__string__p0\""));
            Assert.That(strings, Does.Contain("\"Receive__int__string__p1\""));
        }

        // #51：インタフェース実装メソッドと同じ名前のメソッドは置けない（[NetworkCallable] と同じ制約）
        [TestCase("public void Receive() { }", TestName = "Overload without parameters")]
        [TestCase("public void Receive(int v) { }", TestName = "Overload with parameters")]
        public void OverloadOfImplementationIsError(string member)
        {
            var target = new TestSource("NetworkTarget.cs", @"
using UdonSharp;

namespace CustomUdonSharpTests.Network
{
    public class NetworkTarget : UdonSharpBehaviour, IReceiver
    {
        public void Receive(int v, string sender) { }
        " + member + @"
    }
}");
            var result = UdonSharpTestCompiler.Compile(Receiver, target);

            Assert.That(result.Diagnostics, Has.Exactly(1).Matches<string>(d =>
                d.StartsWith("Error:") && d.Contains("NetworkTarget.cs(9,") && d.Contains("同じ名前のメソッドは置けません")));
        }
    }
}
