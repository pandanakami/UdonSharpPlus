using NUnit.Framework;

namespace CustomUdonSharp.Tests
{
    /// <summary>
    /// 本パッケージが使う UdonSharp の内部 API が全部見つかり、パッチが当たっていること（Redmine #39）。
    /// VRChat SDK を上げたときは、まずこのテストで内部 API の変化を確かめる（DEVELOPMENT.md）
    /// </summary>
    public class UdonSharpInternalsTests
    {
        [Test]
        public void AllInternalsFound()
        {
            bool found = UdonSharpInternals.Resolve();
            Assert.That(UdonSharpInternals.Missing, Is.Empty,
                $"VRChat SDK {UdonSharpInterfacePatch.SdkVersion()} で見つからない UdonSharp の内部 API があります");
            Assert.That(found, Is.True);
        }

        [Test]
        public void PatchesApplied()
        {
            Assert.That(UdonSharpInterfacePatch.Enabled, Is.True, "パッチが当たっていません（コンソールの [CustomUdonSharp] のエラーを見てください）");
        }
    }
}
