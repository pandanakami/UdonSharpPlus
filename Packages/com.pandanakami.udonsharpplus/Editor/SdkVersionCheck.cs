using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// VRChat SDK（com.vrchat.worlds）のバージョンが、動作を確かめた範囲（本パッケージの package.json の vpmDependencies）の外なら、
    /// 起動時に警告を 1 回出す（Redmine #95）。動きは変えない：内部が変わっていない更新なら、そのまま使えるようにするため。
    /// 内部が変わって使えないときは、UdonSharpInternals が見つからないものを見つけて、全体を無効にする（#39）。
    ///
    /// 範囲の書き方は VPM（npm の semver）のうち、よく使うものだけを解釈する：
    ///   ~1.2.3（1.2.3 以上 1.3.0 未満）、^1.2.3（1.2.3 以上 2.0.0 未満）、&gt;= &gt; &lt;= &lt; =、そのままの版（1.2.3）、
    ///   1.2.x / 1.2.*、空白区切りの組み合わせ（すべて満たす）、|| の区切り（どれかを満たす）。
    /// 版の -beta などのプレリリースと +build は無視して、数字だけで比べる
    /// </summary>
    internal static class SdkVersionCheck
    {
        internal const string SdkPackage = "com.vrchat.worlds";

        /// <summary>範囲外なら警告を出す。版や範囲が分からなければ何もしない</summary>
        public static void WarnIfOutOfRange()
        {
            try
            {
                string version = UdonSharpInterfacePatch.SdkVersion();
                string range = ExpectedRange();
                if (range == null || !TryParse(version, out _))
                    return;
                if (!InRange(version, range))
                    Debug.LogWarning($"[UdonSharpPlus] この VRChat SDK（{version}）は、動作を確かめた範囲（{range}）の外です。" +
                                     "うまく動かないときは、UdonSharp Plus を更新してください");
            }
            catch (Exception)
            {
                // 確認できなくても、動きには関係ない
            }
        }

        /// <summary>本パッケージの package.json の vpmDependencies にある com.vrchat.worlds の範囲。読めなければ null</summary>
        public static string ExpectedRange()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SdkVersionCheck).Assembly);
            if (package == null)
                return null;
            string path = Path.Combine(package.resolvedPath, "package.json");
            if (!File.Exists(path))
                return null;
            Match m = Regex.Match(File.ReadAllText(path), "\"" + Regex.Escape(SdkPackage) + "\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>version が range を満たすか</summary>
        public static bool InRange(string version, string range)
        {
            if (!TryParse(version, out int[] v))
                return false;
            foreach (string alternative in range.Split(new[] { "||" }, StringSplitOptions.None))
            {
                string[] parts = alternative.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;
                bool all = true;
                foreach (string part in parts)
                    all &= Satisfies(v, part);
                if (all)
                    return true;
            }
            return false;
        }

        private static bool Satisfies(int[] v, string condition)
        {
            if (condition == "*" || condition == "x")
                return true;
            if (condition.StartsWith("~", StringComparison.Ordinal))
            {
                int[] low = ParseOrNull(condition.Substring(1));
                return low != null && Compare(v, low) >= 0 && Compare(v, new[] { low[0], low[1] + 1, 0 }) < 0;
            }
            if (condition.StartsWith("^", StringComparison.Ordinal))
            {
                int[] low = ParseOrNull(condition.Substring(1));
                if (low == null)
                    return false;
                int[] high = low[0] > 0 ? new[] { low[0] + 1, 0, 0 } : low[1] > 0 ? new[] { 0, low[1] + 1, 0 } : new[] { 0, 0, low[2] + 1 };
                return Compare(v, low) >= 0 && Compare(v, high) < 0;
            }
            foreach (string op in new[] { ">=", "<=", ">", "<", "=" })
            {
                if (!condition.StartsWith(op, StringComparison.Ordinal))
                    continue;
                int[] target = ParseOrNull(condition.Substring(op.Length));
                if (target == null)
                    return false;
                int c = Compare(v, target);
                switch (op)
                {
                    case ">=": return c >= 0;
                    case "<=": return c <= 0;
                    case ">": return c > 0;
                    case "<": return c < 0;
                    default: return c == 0;
                }
            }
            // 1.2.x / 1.2.* / 1.2：書いてある桁だけ比べる
            string[] digits = condition.Split('.');
            for (int i = 0; i < digits.Length && i < 3; i++)
            {
                if (digits[i] == "x" || digits[i] == "X" || digits[i] == "*")
                    return true;
                if (!int.TryParse(StripSuffix(digits[i]), out int d) || d != v[i])
                    return false;
            }
            return true;
        }

        private static int[] ParseOrNull(string text) => TryParse(text, out int[] v) ? v : null;

        /// <summary>"3.10.6"・"3.10.6-beta.1"・"3.10" を [3, 10, 6] にする（足りない桁は 0）</summary>
        internal static bool TryParse(string text, out int[] version)
        {
            version = new int[3];
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string[] digits = StripSuffix(text.Trim()).Split('.');
            if (digits.Length == 0 || digits.Length > 3)
                return false;
            for (int i = 0; i < digits.Length; i++)
                if (!int.TryParse(digits[i], out version[i]) || version[i] < 0)
                    return false;
            return true;
        }

        private static string StripSuffix(string text)
        {
            int cut = text.IndexOfAny(new[] { '-', '+' });
            return cut < 0 ? text : text.Substring(0, cut);
        }

        private static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return 0;
        }
    }
}
