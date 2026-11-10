using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Syntax
{
    // #79 i・f: NullProbe（ケースごとに 1 つ）の結果を、数フレーム後に読んで OK/NG を表示する。
    // 期待は C# と同じく、どのケースも実行時例外で止まること（finished が立たない）
    public class NullProbeRunner : UdonSharpBehaviour
    {
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
                string state = !p.started ? "not started" : p.finished ? "finished " + p.observed : "halted";
                bool ok = p.started && !p.finished;
                allOk &= ok;
                text += (ok ? "OK  " : "NG  ") + NullProbe.NameOf(p.caseId) + ": " + state + (ok ? "" : " (expected halted)") + "\n";
            }
            bool countOk = probes.Length == NullProbe.Count;
            allOk &= countOk;

            Debug.Log("[FeatureTests] #79 null " + (allOk ? "OK" : "NG") + " | " + text.Replace("\n", " | "));
            if (display != null)
                display.text = "#79 null access\n" + (allOk ? "RESULT: OK" : "RESULT: NG") + (countOk ? "" : " (probe count)") + "\n\n" + text;
        }
    }
}
