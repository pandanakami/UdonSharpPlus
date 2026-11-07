using System.Linq;
using NUnit.Framework;

namespace CustomUdonSharp.Tests
{
    /// <summary>[NetworkCallable] なインタフェース実装メソッド（Redmine #32, #47）</summary>
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
        public void ImplementationIsExportedWithSignatureName()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target);

            Assert.That(result.Diagnostics, Is.Empty);
            ProgramResult target = result["NetworkTarget"];
            Assert.That(target.EntryPoints, Is.EquivalentTo(new[] { "Receive__int__string" }));

            NetworkEventInfo networkEvent = target.NetworkEvents.Single();
            Assert.That(networkEvent.Name, Is.EqualTo("Receive__int__string"));
            Assert.That(networkEvent.Parameters.Select(p => p.Name),
                Is.EqualTo(new[] { "Receive__int__string__p0", "Receive__int__string__p1" }));
        }

        [TestCase("((UdonSharpBehaviour)(IReceiver)target).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(IReceiver.Receive), 1, \"a\");", TestName = "Interface nameof")]
        [TestCase("((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetworkTarget.Receive), 1, \"a\");", TestName = "Concrete nameof")]
        [TestCase("((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, \"Receive\", 1, \"a\");", TestName = "Concrete literal")]
        public void EventNameIsRewrittenToSignatureName(string call)
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target, Caller(call));

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Logs, Is.Empty);
            var strings = result["NetworkCaller"].HeapDefaults.Values.ToList();
            Assert.That(strings, Does.Contain("\"Receive__int__string\""));
            Assert.That(strings, Does.Not.Contain("\"Receive\""));
        }

        [Test]
        public void LocalCallThroughInterfaceUsesSignatureName()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target, Caller("((IReceiver)target).Receive(1, \"a\");"));

            Assert.That(result.Diagnostics, Is.Empty);
            var strings = result["NetworkCaller"].HeapDefaults.Values.ToList();
            Assert.That(strings, Does.Contain("\"Receive__int__string\""));
            Assert.That(strings, Does.Contain("\"Receive__int__string__p0\""));
            Assert.That(strings, Does.Contain("\"Receive__int__string__p1\""));
        }

        // #47
        [Test]
        public void NonConstantEventNameIsWarned()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target,
                Caller("string ev = \"Receive\"; ((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, ev, 1, \"a\");"));

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Logs, Has.Exactly(1).Matches<string>(l =>
                l.StartsWith("Warning:") && l.Contains("NetworkCaller.cs") && l.Contains("イベント名が定数ではない")));
        }

        // #47：受け手の型に改名対象のメソッドがなければ警告しない
        [Test]
        public void NonConstantEventNameOnPlainBehaviourIsNotWarned()
        {
            var result = UdonSharpTestCompiler.Compile(Receiver, Target,
                Caller("string ev = \"Receive\"; target.SendCustomNetworkEvent(NetworkEventTarget.All, ev, 1, \"a\");"));

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Logs, Is.Empty);
        }
    }
}
