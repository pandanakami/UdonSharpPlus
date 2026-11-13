using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace FeatureTests.EditorTools
{
    /// <summary>
    /// プレイモード（ClientSim）の間に、FeatureTests の全パネルの結果をまとめて読む（Redmine #106）。
    ///
    /// 結果を出す UdonSharpBehaviour（ランナー）は、resultReady・resultOk・resultName・resultText の変数を持つ。
    /// それを UdonBehaviour.GetProgramVariable で読む（コンソールのログを探さずに済む）。
    ///
    /// 使い方：メニュー Tools/UdonSharpPlus/FeatureTests/Collect Results、
    /// または Unity MCP の execute_code で `return FeatureTests.EditorTools.FeatureTestResults.Collect();`。
    /// 遅れて結果を出すパネル（数フレーム後に判定するもの）は、まだなら「未完了」になるので、少し待って呼び直す。
    /// </summary>
    public static class FeatureTestResults
    {
        /// <summary>自動では判定しないパネル（プログラムの名前 → 確かめ方）</summary>
        private static readonly Dictionary<string, string> Manual = new Dictionary<string, string>
        {
            { "NetworkProbe", "#32 NetworkCallable：パネルの SEND を押し、表示の 1〜5 を見る（2 つのクライアントでも）" },
            { "RuntimeErrorProbe", "#53 実行時例外の位置：Interact で、コンソールの「Udon runtime exception detected!」が Faulty.cs の 11 行目になること" },
        };

        public sealed class Result
        {
            public string Name;
            public string Path;
            public bool Ready;
            public bool Ok;
            public string Text;
        }

        [MenuItem("Tools/UdonSharpPlus/FeatureTests/Collect Results")]
        private static void CollectFromMenu()
        {
            string text = Collect(out bool allOk);
            if (allOk)
                Debug.Log(text);
            else
                Debug.LogWarning(text);
        }

        /// <summary>全パネルの結果を 1 つの文字列にする（NG・未完了のものは本文も付ける）</summary>
        public static string Collect() => Collect(out _);

        public static string Collect(out bool allOk)
        {
            allOk = false;
            if (!EditorApplication.isPlaying)
                return "[FeatureTests] プレイモードではありません（ClientSim でプレイモードに入ってから呼んでください）";

            List<Result> results = Read();
            var manual = Object.FindObjectsOfType<UdonBehaviour>(true)
                .Select(u => u.programSource != null ? u.programSource.name : null)
                .Where(n => n != null && Manual.ContainsKey(n))
                .Distinct().OrderBy(n => n).ToList();

            int ok = results.Count(r => r.Ready && r.Ok);
            int ng = results.Count(r => r.Ready && !r.Ok);
            int pending = results.Count(r => !r.Ready);
            allOk = results.Count > 0 && ng == 0 && pending == 0;

            var sb = new StringBuilder();
            sb.Append("[FeatureTests] ").Append(allOk ? "ALL OK" : ng > 0 ? "NG あり" : "未完了あり")
              .Append($"（OK {ok} / NG {ng} / 未完了 {pending}。ほかに手で確認 {manual.Count}）\n");
            foreach (Result r in results)
            {
                string state = !r.Ready ? "未完了" : r.Ok ? "OK" : "NG";
                sb.Append($"{state,-4} {r.Name}");
                if (!r.Ready || !r.Ok)
                    sb.Append($"  [{r.Path}]  {r.Text}");
                sb.Append('\n');
            }
            foreach (string name in manual)
                sb.Append("手で確認 ").Append(Manual[name]).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>結果の変数（resultReady）を持つ UdonBehaviour の結果を、名前の順に読む</summary>
        public static List<Result> Read()
        {
            var results = new List<Result>();
            foreach (UdonBehaviour udon in Object.FindObjectsOfType<UdonBehaviour>(true))
            {
                if (udon.GetProgramVariableType("resultReady") != typeof(bool))
                    continue;
                string name = udon.GetProgramVariable("resultName") as string;
                results.Add(new Result
                {
                    Name = string.IsNullOrEmpty(name) ? udon.name : name,
                    Path = PathOf(udon.transform),
                    Ready = udon.GetProgramVariable("resultReady") is bool ready && ready,
                    Ok = udon.GetProgramVariable("resultOk") is bool ok && ok,
                    Text = udon.GetProgramVariable("resultText") as string ?? "",
                });
            }
            return results.OrderBy(r => r.Name, System.StringComparer.Ordinal).ThenBy(r => r.Path).ToList();
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
