using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 書き換え後のコードの UASM を静的に調べて、Udon で確保（GC）になる命令がないことを確かめる（#217）。
    /// Udon は、COPY の元が「null で初期化された、型つきの定数」（default(object[]) など。object 型の null は別）のとき、
    /// UdonHeap.CopyHeapVariable の中で、リフレクションで確保する（1 回で 2 回・約 64 B）。EXTERN の命令には出てこないので、COPY を見る
    /// </summary>
    public class UasmGcScanTests
    {
        private static readonly string[] ValueTypes =
            { "SystemInt32", "SystemBoolean", "SystemUInt32", "SystemSingle", "SystemString", "SystemInt64", "SystemDouble", "SystemObject" };

        /// <summary>COPY の元が、型つきの null 定数のものを、「メソッド: 元 -> 先」の形で返す</summary>
        private static List<string> TypedNullCopies(string uasm)
        {
            var nulls = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(uasm, @"^\s*(__const_\S+): %(\S+), null\s*$", RegexOptions.Multiline))
                if (!ValueTypes.Contains(m.Groups[2].Value))
                    nulls[m.Groups[1].Value] = m.Groups[2].Value;

            var found = new List<string>();
            string owner = null;
            var stack = new List<string>();
            foreach (string raw in uasm.Split('\n'))
            {
                string line = raw.Trim();
                Match om = Regex.Match(line, @"^# (\S+\(.*\))$");
                if (om.Success) { owner = om.Groups[1].Value; continue; }
                if (line.StartsWith("PUSH,")) { stack.Add(line.Substring(5).Trim()); continue; }
                if (line == "COPY")
                {
                    if (stack.Count >= 2 && nulls.TryGetValue(stack[stack.Count - 2], out string type))
                        found.Add($"{owner}: {stack[stack.Count - 2]}(%{type}) -> {stack[stack.Count - 1]}");
                    stack.Clear();
                    continue;
                }
                if (line.StartsWith("EXTERN") || line.StartsWith("JUMP"))
                    stack.Clear();
            }
            return found;
        }

        [Test]
        public void TheScannerSeesDefaultOfAReferenceType()
        {
            // 確かめる側の確認：default(object[]) の初期化は見つかり、null の初期化は見つからない
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("GcScan0.cs",
                "using UdonSharp;\npublic class GcScan0 : UdonSharpBehaviour\n{\n  void A(object[] s) { object[] a = default(object[]); a = (object[])s[1]; }\n  void B(object[] s) { object[] a = null; a = (object[])s[1]; }\n}"));
            List<string> found = TypedNullCopies(r.Programs.Single().Uasm);
            Assert.That(found.Count(f => f.Contains("GcScan0.A(")), Is.EqualTo(1), string.Join("\n", found));
            Assert.That(found.Count(f => f.Contains("GcScan0.B(")), Is.EqualTo(0), string.Join("\n", found));
        }

        [Test]
        public void CoroutineStepsDoNotCopyTypedNullConstants()
        {
            // 参照型のローカル（配列・クラス・string・UniTask<T> の戻り値の置き場）を持つコルーチン・async
            CompileResult r = UdonSharpTestCompiler.Compile(
                new TestSource("GcScan1.cs",
                    "using System.Collections;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\n" +
                    "public class GcScanBox { public int n; }\n"),
                new TestSource("GcScan2.cs",
                    "using System.Collections;\nusing Cysharp.Threading.Tasks;\nusing UdonSharp;\nusing UnityEngine;\n" +
                    "public class GcScan2 : UdonSharpBehaviour\n{\n  int _sink; bool _never;\n" +
                    "  void Start() { Run(); StartCoroutine(Co(3)); }\n" +
                    "  IEnumerator Co(int n) { int[] arr = new int[2]; string s = \"x\"; GcScanBox b = new GcScanBox(); object[] o = new object[1]; yield return null; _sink += arr.Length + s.Length + b.n + o.Length + n; foreach (int v in arr) { yield return null; _sink += v; } }\n" +
                    "  async UniTask<int> Zero() { if (_never) await UniTask.Yield(); return 7; }\n" +
                    "  async UniTask Run() { int v = await Zero(); await UniTask.WhenAll(Nop(), Nop()); int w = await UniTask.WhenAny(Nop(), Nop()); _sink += v + w; }\n" +
                    "  async UniTask Nop() { if (_never) await UniTask.Yield(); }\n}"));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            List<string> found = TypedNullCopies(r["GcScan2"].Uasm);
            Assert.That(found, Is.Empty, "型つきの null 定数からの COPY があります（実行のたびに確保になります）\n" + string.Join("\n", found));
        }
    }
}
