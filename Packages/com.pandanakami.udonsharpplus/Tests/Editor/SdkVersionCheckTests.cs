using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// VRChat SDK のバージョンが、動作を確かめた範囲（package.json の vpmDependencies）の中かを比べる処理（Redmine #95）。
    /// 範囲外のときに警告が出る流れは、package.json の範囲を一時的に書き換えて手で確かめる（DEVELOPMENT.md）
    /// </summary>
    public class SdkVersionCheckTests
    {
        [TestCase("3.10.6", "~3.10.6", true)]
        [TestCase("3.10.9", "~3.10.6", true)]
        [TestCase("3.10.5", "~3.10.6", false)]
        [TestCase("3.11.0", "~3.10.6", false)]
        [TestCase("3.10.7-beta.1", "~3.10.6", true)]
        [TestCase("3.11.0", "^3.10.6", true)]
        [TestCase("4.0.0", "^3.10.6", false)]
        [TestCase("0.2.5", "^0.2.3", true)]
        [TestCase("0.3.0", "^0.2.3", false)]
        [TestCase("3.10.6", ">=3.10.0 <3.11.0", true)]
        [TestCase("3.11.0", ">=3.10.0 <3.11.0", false)]
        [TestCase("3.10.6", "3.10.6", true)]
        [TestCase("3.10.7", "3.10.6", false)]
        [TestCase("3.10.7", "3.10.x", true)]
        [TestCase("3.11.0", "3.10.x", false)]
        [TestCase("3.12.0", "~3.10.0 || ~3.12.0", true)]
        [TestCase("3.11.0", "~3.10.0 || ~3.12.0", false)]
        [TestCase("不明", "~3.10.6", false)]
        public void InRange(string version, string range, bool expected)
        {
            Assert.That(SdkVersionCheck.InRange(version, range), Is.EqualTo(expected), $"{version} / {range}");
        }

        /// <summary>期待値は本パッケージの package.json から読める</summary>
        [Test]
        public void ExpectedRangeIsReadFromPackageJson()
        {
            Assert.That(SdkVersionCheck.ExpectedRange(), Is.Not.Null.And.Not.Empty);
        }

        /// <summary>今のプロジェクトの SDK は、動作を確かめた範囲の中（範囲外なら、package.json の更新を忘れている）</summary>
        [Test]
        public void CurrentSdkIsInRange()
        {
            string version = UdonSharpInterfacePatch.SdkVersion();
            string range = SdkVersionCheck.ExpectedRange();
            Assert.That(SdkVersionCheck.InRange(version, range), Is.True, $"SDK {version} は {range} の外です");
        }
    }
}
