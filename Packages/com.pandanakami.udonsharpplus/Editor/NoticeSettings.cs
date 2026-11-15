using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// コンパイル時の注意（Notices、#118）の設定。ProjectSettings/UdonSharpPlusSettings.json に置く（リポジトリに入れて、チームで揃えられる）。
    /// 既定から変えた ID だけを記録する（あとで注意の既定を変えたとき、変えていない ID は新しい既定に従うように）
    /// </summary>
    internal static class NoticeSettings
    {
        internal const string FilePath = "ProjectSettings/UdonSharpPlusSettings.json";

        [Serializable]
        private sealed class Data
        {
            public bool noticesEnabled = true;
            public List<string> enabledNotices = new List<string>();    // 既定では出さないが、出すもの
            public List<string> disabledNotices = new List<string>();   // 既定では出すが、出さないもの
        }

        private static readonly object Gate = new object();
        private static Data _data;
        private static DateTime _loadedWrite;

        /// <summary>テストで設定を差し替える（null なら、ファイルの設定）</summary>
        internal static Func<Notices.Rule, bool> Override;

        /// <summary>この注意を出すか。U# のコンパイルの並列処理（ワーカースレッド）からも呼ばれるので、Unity の API は使わない</summary>
        internal static bool IsEnabled(Notices.Rule rule)
        {
            if (Override != null)
                return Override(rule);
            Data data = Load();
            if (!data.noticesEnabled)
                return false;
            return rule.DefaultOn ? !data.disabledNotices.Contains(rule.Id) : data.enabledNotices.Contains(rule.Id);
        }

        private static Data Load()
        {
            lock (Gate)
            {
                try
                {
                    DateTime write = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                    if (_data == null || write != _loadedWrite)
                    {
                        _data = write == DateTime.MinValue ? new Data() : JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
                        _data.enabledNotices = _data.enabledNotices ?? new List<string>();
                        _data.disabledNotices = _data.disabledNotices ?? new List<string>();
                        _loadedWrite = write;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UdonSharpPlus] {FilePath} を読めなかったため、注意の設定を既定にします：{e.Message}");
                    _data = new Data();
                }
                return _data;
            }
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
                keywords = new HashSet<string>(new[] { "UdonSharp", "Notice", "注意" }.Concat(Notices.Rules.Select(r => r.Id))),
            };

        private static Vector2 _scroll;

        private static void OnGui()
        {
            Data data = Load();
            EditorGUILayout.LabelField("コンパイル時の注意", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "コンパイルエラーにはならないが、C# と動きが違うもの・気付きにくいものを使っているとき、U# のコンパイルのたびに、コンソールに Info のログを出します。"
                + "同じ注意は、コンパイルごとに 1 回だけ、最初に使った位置に出します。",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            bool all = EditorGUILayout.ToggleLeft("注意を出す", data.noticesEnabled);
            var enabled = new HashSet<string>(data.enabledNotices);
            var disabled = new HashSet<string>(data.disabledNotices);

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(!all))
            {
                foreach (Notices.Rule rule in Notices.Rules)
                {
                    bool current = rule.DefaultOn ? !disabled.Contains(rule.Id) : enabled.Contains(rule.Id);
                    string label = $"{rule.Id}  {rule.Title}{(rule.DefaultOn ? "（既定で出す）" : "")}";
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
            if (GUILayout.Button("既定に戻す", GUILayout.Width(120)))
                reset = true;

            if (EditorGUI.EndChangeCheck() || reset)
            {
                Save(reset
                    ? new Data()
                    : new Data
                    {
                        noticesEnabled = all,
                        enabledNotices = enabled.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                        disabledNotices = disabled.OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    });
            }
        }
    }
}
