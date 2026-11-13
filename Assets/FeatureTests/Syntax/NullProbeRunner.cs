using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #79 i・f: NullProbe（ケースごとに 1 つ）の結果を、数フレーム後に読んで OK/NG を表示する。
    // 期待は C# と同じく、どのケースも実行時例外で止まること（finished が立たない）。
    // 止まる前にほかの処理が走っていないこと（observed が空のまま）も見る（#104）
    public class NullProbeRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        public NullProbe[] probes;
        public Text display;

        private void Start() { SendCustomEventDelayedFrames(nameof(Check), 10); }

        public override void Interact() { Check(); }

        public void Check()
        {
            bool allOk = true;
            string text = "";
            for (int i = 0; i < probes.Length; i++)
            {
                NullProbe p = probes[i];
                string state = !p.started ? "not started" : p.finished ? "finished " + p.observed
                    : p.observed.Length > 0 ? "halted after " + p.observed : "halted";
                bool ok = p.started && !p.finished && p.observed.Length == 0;
                allOk &= ok;
                text += (ok ? "OK  " : "NG  ") + NullProbe.NameOf(p.caseId) + ": " + state + (ok ? "" : " (expected halted)") + "\n";
            }
            bool countOk = probes.Length == NullProbe.Count;
            allOk &= countOk;

            resultName = "#79 null access"; resultOk = allOk; resultText = text.Replace("\n", " | "); resultReady = true;
            Debug.Log("[FeatureTests] #79 null " + (allOk ? "OK" : "NG") + " | " + text.Replace("\n", " | "));
            if (display != null)
                display.text = "#79 null access\n" + (allOk ? "RESULT: OK" : "RESULT: NG") + (countOk ? "" : " (probe count)") + "\n\n" + text;
        }
    }
}
