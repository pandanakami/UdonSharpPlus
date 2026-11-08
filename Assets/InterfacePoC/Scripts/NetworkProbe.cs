using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace InterfacePoC
{
    // #46: [NetworkCallable] なインタフェース実装メソッドを、クライアント間で受け取って結果を表示する
    public class NetworkProbe : UdonSharpBehaviour, INetworkReceiver
    {
        public Text display;

        private const int CaseCount = 4;
        private string[] _labels;
        private string[] _results;
        private int[] _counts;

        private void Start()
        {
            _labels = new string[]
            {
                "",
                "1 interface + nameof  (All)   ",
                "2 concrete  + nameof  (All)   ",
                "3 concrete  + literal (All)   ",
                "4 interface + nameof  (Others)",
            };
            _results = new string[CaseCount + 1];
            _counts = new int[CaseCount + 1];
            ResetResults();
        }

        [NetworkCallable]
        public void Receive(int value, string from)
        {
            if (_results == null)
                Start();   // Start より先に届いた場合

            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            string who = Utilities.IsValid(caller) ? caller.displayName : "?";
            Debug.Log($"[PoC] NetworkProbe Receive value={value} from='{from}' caller='{who}'");

            if (value < 1 || value > CaseCount)
            {
                _results[0] = $"NG unexpected value={value} from='{from}' by {who}";
                Refresh();
                return;
            }

            _counts[value]++;
            _results[value] = (from == "case" + value ? "OK" : $"NG (from='{from}')") + $"  by {who}";
            Refresh();
        }

        public void ResetResults()
        {
            for (int i = 0; i <= CaseCount; i++)
            {
                _results[i] = "";
                _counts[i] = 0;
            }
            Refresh();
        }

        private void Refresh()
        {
            if (display == null)
                return;

            VRCPlayerApi local = Networking.LocalPlayer;
            string text = "#32 NetworkCallable interface test\n"
                          + $"this client: {(Utilities.IsValid(local) ? local.displayName : "?")}\n"
                          + "client that pressed SEND: 1-3 OK, 4 = not received\n"
                          + "other client:             1-4 OK (by the sender)\n"
                          + "each count should be x1 per press\n\n";

            for (int i = 1; i <= CaseCount; i++)
            {
                string result = _results[i].Length == 0 ? "not received" : _results[i];
                text += $"{_labels[i]} : {result}  x{_counts[i]}\n";
            }

            if (_results[0].Length > 0)
                text += "\n" + _results[0] + "\n";

            display.text = text;
        }
    }
}
