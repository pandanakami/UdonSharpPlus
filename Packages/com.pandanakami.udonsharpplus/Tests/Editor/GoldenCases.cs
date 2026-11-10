using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 期待値ファイル（ゴールデンファイル）方式のテストケース（Redmine #43）。
    ///
    /// Tests/Editor/Cases~/&lt;ケース名&gt;/ に IN の .cs と期待値 expected.txt を置く。
    /// "~" で終わるフォルダは Unity がインポートしないので、.cs はプロジェクトのコンパイル対象にならない。
    ///
    /// - 期待値がない：実際の出力を expected.txt に書き出してテストを失敗させる（中身を確認してもらうため）
    /// - 一致しない：実際の出力を ActualDir に書き出し、最初に違う行をメッセージに出す
    /// - 一括更新：環境変数 UDONSHARPPLUS_UPDATE_EXPECTED=1 でテストを実行するか、メニューから
    ///   （差分は git で確認してからコミットする）
    /// </summary>
    internal static class GoldenCases
    {
        public const string UpdateEnvironmentVariable = "UDONSHARPPLUS_UPDATE_EXPECTED";
        public const string ActualDirEnvironmentVariable = "UDONSHARPPLUS_ACTUAL_DIR";
        private const string ExpectedFileName = "expected.txt";

        /// <summary>
        /// ケースのフォルダにこの名前のファイルがあると、期待値はプログラム名・Diagnostics・Logs だけにする（Uasm などは比べない）。
        /// エラーなくコンパイルできることだけを確かめたい大きなケース用（#63。実行結果は FeatureTests のシナリオで確かめる）
        /// </summary>
        private const string DiagnosticsOnlyFileName = "diagnostics-only";

        /// <summary>
        /// ケースのフォルダにこの名前のファイルがあると、1 行に 1 つ書いたシンボルを定義して解析する（#91）。
        /// ないときは UdonSharpTestCompiler.EditorDefines（エディタでのコンパイルと同じ）。
        /// アップロード用のビルドを確かめるときは、UNITY_EDITOR を書かずに COMPILER_UDONSHARP だけにする
        /// </summary>
        private const string DefinesFileName = "defines";

        public static string CasesRoot
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GoldenCases).Assembly);
                string root = package != null
                    ? package.resolvedPath
                    : Path.GetFullPath("Packages/com.pandanakami.udonsharpplus");
                return Path.Combine(root, "Tests", "Editor", "Cases~");
            }
        }

        public static string ActualDir =>
            Environment.GetEnvironmentVariable(ActualDirEnvironmentVariable) is string dir && dir.Length > 0
                ? dir
                : Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "actual"));

        private static bool UpdateRequested =>
            Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1";

        /// <summary>ケース名（Cases~ 直下のフォルダ。"/" 区切りで入れ子にしてもよい）</summary>
        public static IEnumerable<string> All()
        {
            string root = CasesRoot;
            if (!Directory.Exists(root))
                return Enumerable.Empty<string>();

            return Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Where(d => Directory.GetFiles(d, "*.cs").Length > 0)
                .Select(d => d.Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(n => n, StringComparer.Ordinal);
        }

        public static string Run(string caseName)
        {
            string dir = Path.Combine(CasesRoot, caseName);
            TestSource[] sources = Directory.GetFiles(dir, "*.cs")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new TestSource(Path.GetFileName(p), File.ReadAllText(p)))
                .ToArray();

            bool diagnosticsOnly = File.Exists(Path.Combine(dir, DiagnosticsOnlyFileName));
            string definesPath = Path.Combine(dir, DefinesFileName);
            string[] defines = File.Exists(definesPath)
                ? File.ReadAllLines(definesPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray()
                : UdonSharpTestCompiler.EditorDefines;
            return Render(UdonSharpTestCompiler.Compile(defines, sources), diagnosticsOnly);
        }

        public static void Verify(string caseName)
        {
            string actual = Run(caseName);
            string expectedPath = Path.Combine(CasesRoot, caseName, ExpectedFileName);

            if (UpdateRequested)
            {
                WriteText(expectedPath, actual);
                Assert.Pass($"expected を更新しました: {expectedPath}");
            }

            if (!File.Exists(expectedPath))
            {
                WriteText(expectedPath, actual);
                Assert.Fail($"expected がなかったので書き出しました。内容を確認してからコミットしてください: {expectedPath}");
            }

            string expected = NormalizeNewlines(File.ReadAllText(expectedPath));
            if (expected == actual)
                return;

            string actualPath = Path.Combine(ActualDir, caseName.Replace('/', Path.DirectorySeparatorChar) + ".txt");
            WriteText(actualPath, actual);
            Assert.Fail($"期待値と一致しません: {caseName}\n{FirstDifference(expected, actual)}\nexpected: {expectedPath}\nactual:   {actualPath}");
        }

        [MenuItem("Tools/UdonSharpPlus/Tests/Update Expected Files")]
        private static void UpdateAllFromMenu()
        {
            var updated = new List<string>();
            foreach (string name in All())
            {
                string path = Path.Combine(CasesRoot, name, ExpectedFileName);
                string actual = Run(name);
                if (!File.Exists(path) || NormalizeNewlines(File.ReadAllText(path)) != actual)
                {
                    WriteText(path, actual);
                    updated.Add(name);
                }
            }

            UnityEngine.Debug.Log(updated.Count == 0
                ? "[UdonSharpPlus] expected は全件最新です"
                : $"[UdonSharpPlus] expected を {updated.Count} 件更新しました（git で差分を確認してください）:\n" + string.Join("\n", updated));
        }

        // ------------------------------------------------------------------

        /// <summary>期待値ファイルの中身を作る</summary>
        public static string Render(CompileResult result, bool diagnosticsOnly = false)
        {
            var sb = new StringBuilder();

            foreach (ProgramResult program in result.Programs)
            {
                if (diagnosticsOnly)
                {
                    sb.Append("## Program: ").Append(program.TypeName).Append('\n');
                    continue;
                }

                UasmNormalizer.Result normalized = UasmNormalizer.Normalize(program.Uasm, program.HeapDefaults);

                sb.Append("## Program: ").Append(program.TypeName).Append('\n');
                sb.Append("### Uasm\n").Append(normalized.Uasm).Append('\n');

                sb.Append("### HeapDefaults\n");
                foreach (var pair in program.HeapDefaults)
                {
                    string value = pair.Value;
                    if (UasmNormalizer.IsReturnAddressSymbol(pair.Key)
                        && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint address))
                        value = "-> " + (normalized.DescribeAddress(address) ?? value);
                    sb.Append(pair.Key).Append(" = ").Append(value).Append('\n');
                }

                sb.Append("### NetworkEvents\n");
                foreach (NetworkEventInfo e in program.NetworkEvents)
                    sb.Append(e).Append('\n');

                sb.Append('\n');
            }

            sb.Append("## Diagnostics\n");
            foreach (string d in result.Diagnostics)
                sb.Append(d).Append('\n');

            sb.Append("## Logs\n");
            foreach (string l in result.Logs)
                sb.Append(l).Append('\n');

            return NormalizeNewlines(sb.ToString());
        }

        private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n");

        private static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string FirstDifference(string expected, string actual)
        {
            string[] e = expected.Split('\n');
            string[] a = actual.Split('\n');
            for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
            {
                string el = i < e.Length ? e[i] : "<EOF>";
                string al = i < a.Length ? a[i] : "<EOF>";
                if (el != al)
                    return $"最初に違う行: {i + 1}\n  expected: {el}\n  actual:   {al}";
            }
            return "（違いが見つかりません）";
        }
    }
}
