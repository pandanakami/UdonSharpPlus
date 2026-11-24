using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// Documentation~/unitask.md の書き方が、実際にコンパイルできること（#207 の追加テスト案）。
    /// ドキュメントの例・「使えるもの」の書き方が古くなったら、ここで気づく
    /// </summary>
    public class UniTaskDocsTests
    {
        private static string Doc()
        {
            string path = Path.Combine("Packages", "com.pandanakami.udonsharpplus", "Documentation~", "unitask.md");
            Assert.That(File.Exists(path), path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static void AssertCompiles(string source, string what)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("DocSample.cs", source));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, what + "\n" + string.Join("\n", r.Diagnostics));
        }

        /// <summary>リポジトリの README（日本語・英語）の ```csharp のブロック（Attacker の例。foreach を UniTask で 1 フレームずつに分ける）が、そのままコンパイルできる</summary>
        [TestCase("README.md")]
        [TestCase("README.ja.md")]
        public void TheExampleInTheReadmeCompiles(string readme)
        {
            Assert.That(File.Exists(readme), readme);
            string doc = File.ReadAllText(readme).Replace("\r\n", "\n");
            int start = doc.IndexOf("```csharp\n", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "コードのブロックがありません");
            start += "```csharp\n".Length;
            int end = doc.IndexOf("\n```", start, System.StringComparison.Ordinal);
            string example = doc.Substring(start, end - start);
            Assert.That(example, Does.Contain("await UniTask.Yield()").And.Contain("await Hit(e, total)").And.Contain("async UniTask Hit("));
            AssertCompiles(example, readme + " の例");
        }

        /// <summary>ドキュメントの最初の ```csharp のブロック（Door の例）が、そのままコンパイルできる</summary>
        [Test]
        public void TheExampleInTheDocCompiles()
        {
            string doc = Doc();
            int start = doc.IndexOf("```csharp\n", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "コードのブロックがありません");
            start += "```csharp\n".Length;
            int end = doc.IndexOf("\n```", start, System.StringComparison.Ordinal);
            string example = doc.Substring(start, end - start);
            Assert.That(example, Does.Contain("async void Interact"));
            AssertCompiles(example, "ドキュメントの例");
        }

        /// <summary>「使えるもの」に書いた書き方。左は、ドキュメントにその文字があること、右は、その書き方を含むソース</summary>
        private static readonly string[] DocumentedForms =
        {
            "await UniTask.Yield();",
            "await UniTask.NextFrame();",
            "await UniTask.Delay(ms);",
            "await UniTask.Delay(TimeSpan.FromSeconds(x));",
            "await UniTask.Delay(ms, ignoreTimeScale: true);",
            "await UniTask.DelayFrame(n);",
            "await Foo();",
            "Foo().Forget();",
            "_ = Foo();",
            "_ = await Add(1, 2);",
            "var x = await Add(1, 2);",
            "int x = await Add(1, 2);",
            "x = await Add(1, 2);",
            "return await Add(1, 2);",
            "await Add(1, 2);",
            "Add(1, 2).Forget();",
            "var (a, b) = await UniTask.WhenAll(A(), B());",
            "int i = await UniTask.WhenAny(A(), B());",
            "var (i, r1, r2) = await UniTask.WhenAny(A(), B());",
            "UniTask t = Foo();",
            "var t = Foo();",
            "_t = Foo();",
            "await t;",
            "t.Forget();",
        };

        /// <summary>「CancellationToken」の節の例が、そのままコンパイルできる（#225）</summary>
        [Test]
        public void TheCancellationTokenExampleCompiles()
        {
            string doc = Doc();
            int section = doc.IndexOf("## CancellationToken\n", System.StringComparison.Ordinal);
            Assert.That(section, Is.GreaterThanOrEqualTo(0), "CancellationToken の節がありません");
            int start = doc.IndexOf("```csharp\n", section, System.StringComparison.Ordinal) + "```csharp\n".Length;
            int end = doc.IndexOf("\n```", start, System.StringComparison.Ordinal);
            string example = doc.Substring(start, end - start);
            AssertCompiles("using System.Threading;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\npublic class CancelDoc : UdonSharpBehaviour\n{\n" + example + "\n}", "CancellationToken の例");
        }

        [Test]
        public void TheFormsListedInTheDocAreInTheDoc()
        {
            string doc = Doc();
            foreach (string form in DocumentedForms)
                Assert.That(doc, Does.Contain(form), "ドキュメントから消えました（このテストの一覧も直してください）");
        }

        [Test]
        public void TheFormsListedInTheDocCompile()
        {
            AssertCompiles(@"using System;
using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;
public class DocForms : UdonSharpBehaviour
{
    public int ms = 10;
    public int n = 2;
    public float x = 0.1f;
    public bool flag;
    private int _v;
    private UniTask _t;
    void Start() { Run(); }
    async UniTask Run()
    {
        await UniTask.Yield();
        await UniTask.NextFrame();
        await UniTask.Delay(ms);
        await UniTask.Delay(TimeSpan.FromSeconds(x));
        await UniTask.Delay(TimeSpan.FromMilliseconds(ms));
        await UniTask.Delay(ms, ignoreTimeScale: true);
        await UniTask.Delay(ms, DelayType.UnscaledDeltaTime);
        await UniTask.Delay(ms, DelayType.Realtime);
        await UniTask.DelayFrame(n);
        await UniTask.WaitUntil(() => flag);
        await UniTask.WaitWhile(() => !flag);
        await Foo();
        Foo();
        Foo().Forget();
        _ = Foo();
        _ = await Add(1, 2);
        var v1 = await Add(1, 2);
        int v2 = await Add(1, 2);
        _v = await Add(1, 2);
        await Add(1, 2);
        Add(1, 2);
        Add(1, 2).Forget();
        var (a, b) = await UniTask.WhenAll(Add(1, 2), Add(3, 4));
        await UniTask.WhenAll(A(), B());
        int i = await UniTask.WhenAny(A(), B());
        var (j, r1, r2) = await UniTask.WhenAny(Add(1, 2), Add(3, 4));
        UniTask t = Foo();
        var t2 = Foo();
        _t = Foo();
        await t;
        await _t;
        var t3 = Foo();
        t3.Forget();
        await UniTask.WhenAll(t2, Foo());
        _v = v1 + v2 + a + b + i + j + r1 + r2;
        if (_v > 0) return;
        await Twice();
    }
    async UniTask A() { await UniTask.Yield(); }
    async UniTask B() { await UniTask.Yield(); }
    async UniTask Foo() { await UniTask.Yield(); }
    async UniTask<int> Add(int p, int q) { await UniTask.Yield(); return p + q; }
    async UniTask<int> Twice() { return await Add(1, 2); }
}", "「使えるもの」の書き方");
        }
    }
}
