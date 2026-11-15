using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// UdonSharp Plus の設定。ProjectSettings/UdonSharpPlusSettings.json に置く（リポジトリに入れて、チームで揃えられる）。
    /// - 文言の言語（#67）：English（既定）／日本語
    /// - コンパイル時の注意（Notices、#118）：既定から変えた ID だけを記録する（あとで注意の既定を変えたとき、変えていない ID は新しい既定に従うように）
    /// U# のコンパイルの並列処理（ワーカースレッド）からも読まれるので、読むところでは Unity の API（JsonUtility のほか）は使わない
    /// </summary>
    internal static class UdonSharpPlusSettings
    {
        internal const string FilePath = "ProjectSettings/UdonSharpPlusSettings.json";

        [Serializable]
        private sealed class Data
        {
            public string language = "en";                              // "en" / "ja"
            public bool noticesEnabled = true;
            public List<string> enabledNotices = new List<string>();    // 既定では出さないが、出すもの
            public List<string> disabledNotices = new List<string>();   // 既定では出すが、出さないもの
        }

        private static readonly object Gate = new object();
        private static Data _data;
        private static DateTime _loadedWrite;

        /// <summary>テストで注意の設定を差し替える（null なら、ファイルの設定）</summary>
        internal static Func<Notices.Rule, bool> NoticeOverride;

        /// <summary>テストで言語を差し替える（null なら、ファイルの設定）</summary>
        internal static bool? JapaneseOverride;

        /// <summary>文言を日本語にするか</summary>
        internal static bool Japanese => JapaneseOverride ?? Load().language == "ja";

        /// <summary>この注意を出すか</summary>
        internal static bool IsEnabled(Notices.Rule rule)
        {
            if (NoticeOverride != null)
                return NoticeOverride(rule);
            Data data = Load();
            if (!data.noticesEnabled)
                return false;
            return rule.DefaultOn ? !data.disabledNotices.Contains(rule.Id) : data.enabledNotices.Contains(rule.Id);
        }

        private static Data Load()
        {
            string error = null;
            Data data;
            lock (Gate)
            {
                DateTime write = DateTime.MinValue;
                try
                {
                    write = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                    if (_data == null || write != _loadedWrite)
                    {
                        Data loaded = write == DateTime.MinValue ? new Data() : JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
                        loaded.language = loaded.language == "ja" ? "ja" : "en";
                        loaded.enabledNotices = loaded.enabledNotices ?? new List<string>();
                        loaded.disabledNotices = loaded.disabledNotices ?? new List<string>();
                        _data = loaded;
                        _loadedWrite = write;
                    }
                }
                catch (Exception e)
                {
                    // 先に既定を入れてから知らせる（文言の言語を決めるのに、ここをまた読むため）
                    _data = new Data();
                    _loadedWrite = write;
                    error = e.Message;
                }
                data = _data;
            }
            if (error != null)
                Debug.LogWarning(Loc.T($"[UdonSharpPlus] {FilePath} を読めなかったため、設定を既定にします：{error}", $"[UdonSharpPlus] Could not read {FilePath}, so the default settings are used: {error}"));
            return data;
        }

        private static void Save(Data data)
        {
            lock (Gate)
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
                _data = data;
                _loadedWrite = File.GetLastWriteTimeUtc(FilePath);
            }
        }

        // ---- Project Settings の画面 -------------------------------------------------------

        [SettingsProvider]
        private static SettingsProvider CreateProvider() =>
            new SettingsProvider("Project/Udon Sharp Plus", SettingsScope.Project)
            {
                label = "Udon Sharp Plus",   // Project Settings の一覧の「Udon Sharp」に表記を合わせる
                guiHandler = _ => OnGui(),
                keywords = new HashSet<string>(new[] { "UdonSharp", "Language", "言語", "Notice", "注意" }.Concat(Notices.Rules.Select(r => r.Id))),
            };

        private static readonly string[] LanguageNames = { "English", "日本語" };
        private static Vector2 _scroll;

        private static void OnGui()
        {
            Data data = Load();
            EditorGUI.BeginChangeCheck();

            // 言語の名前は、どちらの言語でも読めるように両方を書く
            int language = EditorGUILayout.Popup("Language / 言語", data.language == "ja" ? 1 : 0, LanguageNames);
            EditorGUILayout.LabelField(Loc.T("エラー・警告・注意の文言と、実行時のログの言語です。変えたあとのコンパイルから使います。",
                "The language of errors, warnings, notices and runtime logs. Used from the next compile."), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Loc.T("コンパイル時の注意", "Compile-time notices"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                Loc.T("コンパイルエラーにはならないが、C# と動きが違うもの・気付きにくいものを使っているとき、U# のコンパイルのたびに、コンソールに Info のログを出します。", "When your code uses something that compiles but behaves differently from C# or is easy to miss, an Info log is written to the Console on every U# compile. ")
                + Loc.T("同じ注意は、コンパイルごとに 1 回だけ、最初に使った位置に出します。", "Each notice appears only once per compile, at the first place it is used."),
                MessageType.None);

            bool all = EditorGUILayout.ToggleLeft(Loc.T("注意を出す", "Show notices"), data.noticesEnabled);
            var enabled = new HashSet<string>(data.enabledNotices);
            var disabled = new HashSet<string>(data.disabledNotices);

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(!all))
            {
                foreach (Notices.Rule rule in Notices.Rules)
                {
                    bool current = rule.DefaultOn ? !disabled.Contains(rule.Id) : enabled.Contains(rule.Id);
                    string label = Loc.T($"{rule.Id}  {rule.Title}{(rule.DefaultOn ? "（既定で出す）" : "")}", $"{rule.Id}  {rule.Title}{(rule.DefaultOn ? " (on by default)" : "")}");
                    bool next = EditorGUILayout.ToggleLeft(new GUIContent(label, rule.Message), current);
                    if (next == current)
                        continue;
                    enabled.Remove(rule.Id);
                    disabled.Remove(rule.Id);
                    if (next != rule.DefaultOn)
                        (next ? enabled : disabled).Add(rule.Id);
                }
            }
            EditorGUILayout.EndScrollView();

            bool reset = false;
            EditorGUILayout.Space();
            if (GUILayout.Button(Loc.T("注意を既定に戻す", "Reset notices to defaults"), GUILayout.Width(200)))
                reset = true;

            if (EditorGUI.EndChangeCheck() || reset)
            {
                Save(new Data
                {
                    language = language == 1 ? "ja" : "en",
                    noticesEnabled = reset || all,
                    enabledNotices = reset ? new List<string>() : enabled.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    disabledNotices = reset ? new List<string>() : disabled.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                });
            }
        }
    }

    /// <summary>文言の言語（#67）。Loc.T("日本語", "English") で、設定の言語の方を返す</summary>
    internal static class Loc
    {
        internal static string T(string japanese, string english) => UdonSharpPlusSettings.Japanese ? japanese : english;
    }
}
