using NUnit.Framework;

namespace UdonSharpPlus.Tests
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
            Assert.That(UdonSharpInterfacePatch.Enabled, Is.True, "パッチが当たっていません（コンソールの [UdonSharpPlus] のエラーを見てください）");
        }

        /// <summary>
        /// UdonSharp の自動アップグレードに入ると印が立ち、読み込む前に抜けても印が残らない（#84）。
        /// 残ると、次のコンパイルで本パッケージの書き換えが行われなくなる
        /// </summary>
        [Test]
        public void UpgradeGuardClearsWhenUpgradeReturnsEarly()
        {
            int before = UpgradeGuardPatch.EnteredCount;
            var empty = System.Array.CreateInstance(UdonSharpInternals.UpgradeScripts.GetParameters()[0].ParameterType.GetElementType(), 0);
            UdonSharpInternals.UpgradeScripts.Invoke(null, new object[] { empty });   // 空なら読み込む前に false で抜ける

            Assert.That(UpgradeGuardPatch.EnteredCount, Is.EqualTo(before + 1), "UdonSharpUpgrader.UpgradeScripts にパッチが当たっていません");
            Assert.That(UpgradeGuardPatch.ConsumeSkip(), Is.False, "アップグレードを抜けたあとも、書き換えを止める印が残っています");
        }
    }
}
