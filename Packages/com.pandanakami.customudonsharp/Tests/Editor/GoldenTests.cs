using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CustomUdonSharp.Tests
{
    /// <summary>Cases~ の各ケースを、期待値ファイルと比べる（Redmine #43）</summary>
    public class GoldenTests
    {
        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (string name in GoldenCases.All())
                yield return new TestCaseData(name).SetName(name);
        }

        [TestCaseSource(nameof(Cases))]
        public void Case(string name)
        {
            // 本パッケージのエラー・警告ログは期待値ファイルの Logs で比べるので、ログによるテスト失敗は止める
            LogAssert.ignoreFailingMessages = true;
            GoldenCases.Verify(name);
        }
    }
}
