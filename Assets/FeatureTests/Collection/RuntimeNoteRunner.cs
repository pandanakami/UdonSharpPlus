using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Collection
{
    // #63: RuntimeNoteProbe（ケースごとに 1 つ）の結果を、数フレーム後に読んで OK/NG を表示する。
    // 期待は collections.md の「注意」のとおり：
    //   - 要素がないときの First／Last／Min／Max／Average、ToDictionary の同じキー、存在しないキーの読み出し：実行時例外で止まる
    //   - FindIndex の範囲外（開始位置が要素数より大きい）：止まらず -1（C# では ArgumentOutOfRangeException。ドキュメントの「見つからない結果」）
    //   - foreach の途中の Add：止まらず、回る回数は最初の要素数のまま。Remove / Clear：範囲外を読んで止まる
    //   - KeyValuePair の Equals：同じ要素から作った 2 つでも False（参照の比較。#95）
    // エラーのログ（First などが出すもの）は、コンソールで目で確かめる
    public class RuntimeNoteRunner : UdonSharpBehaviour
    {
        public RuntimeNoteProbe[] probes;
        public Text display;

        private void Start() { SendCustomEventDelayedFrames(nameof(Check), 10); }

        public override void Interact() { Check(); }

        // 止まるはずのケースなら null、最後まで進むはずのケースなら observed の期待値
        private string ExpectedObserved(int id)
        {
            if (id == 7)
                return "-1";
            if (id == 8)
                return "3,4";
            if (id == 11)
                return "False,True,True";
            return null;
        }

        public void Check()
        {
            bool allOk = true;
            string text = "";
            for (int i = 0; i < probes.Length; i++)
            {
                RuntimeNoteProbe p = probes[i];
                string expected = ExpectedObserved(p.caseId);
                string state = !p.started ? "not started" : p.finished ? "finished " + p.observed : "halted";
                bool ok = expected == null ? p.started && !p.finished : p.finished && p.observed == expected;
                allOk &= ok;
                text += (ok ? "OK  " : "NG  ") + RuntimeNoteProbe.NameOf(p.caseId) + ": " + state
                        + (ok ? "" : " (expected " + (expected == null ? "halted" : "finished " + expected) + ")") + "\n";
            }
            bool countOk = probes.Length == RuntimeNoteProbe.Count;
            allOk &= countOk;

            Debug.Log("[FeatureTests] #63 notes " + (allOk ? "OK" : "NG") + " | " + text.Replace("\n", " | "));
            if (display != null)
                display.text = "#63 runtime notes\n" + (allOk ? "RESULT: OK" : "RESULT: NG") + (countOk ? "" : " (probe count)") + "\n\n" + text;
        }
    }
}
