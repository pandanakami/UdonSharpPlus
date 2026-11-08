using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace CustomUdonSharpTests.SdkInterface
{
    public class Caller : UdonSharpBehaviour
    {
        public Target target;

        private void Start()
        {
            // SDK のインタフェース型の変数（本パッケージの書き換え対象ではない）
            IUdonEventReceiver receiver = (IUdonEventReceiver)target;
            receiver.SendCustomEvent("Ping");                                                         // 1
            receiver.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Target.Receive), 2, "b");  // 2 nameof → 固定名
            receiver.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 3, "c");               // 3 受け手の型が不明

            // 受け手を引数で渡す SDK の API
            NetworkCalling.SendCustomNetworkEvent(receiver, NetworkEventTarget.All, nameof(Target.Receive), 4, "d");          // 4
            NetworkCalling.SendCustomNetworkEvent((IUdonEventReceiver)target, NetworkEventTarget.All, "Receive", 5, "e");    // 5
        }
    }
}
