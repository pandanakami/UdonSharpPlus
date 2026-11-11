using System.IO;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 書き換え後のソースの出力（本物は Library/UdonSharpPlus/。テストでは Temp の下の一時フォルダ）（Redmine #100）
    /// </summary>
    public class DumpTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            // ClearDump は名前に UdonSharpPlus を含むフォルダだけを消す
            _dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDump"));
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private static string UserSource(string ns) => $@"using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace {ns}
{{
    public class User : UdonSharpBehaviour
    {{
        private List<int> _values = new List<int>();
        private void Start() {{ _values.Add(1); Debug.Log(_values.Count); }}
    }}
}}";

        private CompileResult Compile(params TestSource[] sources) =>
            UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir, sources);

        private string Dumped(string relative) => Path.Combine(_dir, "Assets", "UdonSharpPlusTests", relative);

        /// <summary>書き換えたスクリプトは、プロジェクトからの相対パスのまま出る。書き換えのないスクリプトは出ない</summary>
        [Test]
        public void WritesRewrittenScriptUnderRelativePath()
        {
            const string plain = "using UdonSharp; namespace DumpPlain { public class Plain : UdonSharpBehaviour { } }";
            CompileResult result = Compile(new TestSource("User.cs", UserSource("DumpOne")), new TestSource("Plain.cs", plain));

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(File.Exists(Dumped("User.cs")), Is.True);
            Assert.That(File.ReadAllText(Dumped("User.cs")), Does.Contain("DataList"));
            Assert.That(File.Exists(Dumped("Plain.cs")), Is.False, "書き換えていないスクリプトが出ています");
        }

        /// <summary>前回の出力は消える（消したスクリプトなどの古い出力を残さない）</summary>
        [Test]
        public void RemovesStaleFiles()
        {
            string stale = Path.Combine(_dir, "Assets", "Old", "Removed.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(stale));
            File.WriteAllText(stale, "// old");

            Compile(new TestSource("User.cs", UserSource("DumpStale")));

            Assert.That(File.Exists(stale), Is.False, "古い出力が残っています");
            Assert.That(File.Exists(Dumped("User.cs")), Is.True);
        }

        /// <summary>別のフォルダの同じ名前のスクリプトは、上書きし合わない</summary>
        [Test]
        public void KeepsSameNameInDifferentFolders()
        {
            Compile(new TestSource("A/User.cs", UserSource("DumpA")), new TestSource("B/User.cs", UserSource("DumpB")));

            Assert.That(File.ReadAllText(Dumped("A/User.cs")), Does.Contain("namespace DumpA"));
            Assert.That(File.ReadAllText(Dumped("B/User.cs")), Does.Contain("namespace DumpB"));
        }

        [TestCase("Assets/A/User.cs", "Assets/A/User.cs")]
        [TestCase("Assets\\A\\User.cs", "Assets/A/User.cs")]
        [TestCase("Packages/com.example/Runtime/X.cs", "Packages/com.example/Runtime/X.cs")]
        [TestCase("Assets/../../Outside.cs", "Assets/Outside.cs")]
        public void DumpPathKeepsRelativePath(string filePath, string expected)
        {
            string dir = InterfaceLowering.DumpDir;
            try
            {
                InterfaceLowering.DumpDir = "Out";
                Assert.That(InterfaceLowering.DumpPath(filePath).Replace('\\', '/'), Is.EqualTo("Out/" + expected));
            }
            finally
            {
                InterfaceLowering.DumpDir = dir;
            }
        }
    }
}
