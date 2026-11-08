using UdonSharp;
using VRC.Udon.Common.Interfaces;

namespace InterfacePoC
{
    // #46: 押したクライアントから、4 通りの呼び方で SendCustomNetworkEvent を送る
    public class NetworkSender : UdonSharpBehaviour
    {
        public UdonSharpBehaviour probe;

        public override void Interact()
        {
            // 1. インタフェース型を UdonSharpBehaviour にキャストして送る + nameof(インタフェースのメソッド)
            INetworkReceiver receiver = (INetworkReceiver)probe;
            ((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(INetworkReceiver.Receive), 1, "case1");

            // 2. 具象型 + nameof(具象型のメソッド)
            NetworkProbe concrete = (NetworkProbe)probe;
            concrete.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetworkProbe.Receive), 2, "case2");

            // 3. 具象型 + 文字列リテラル
            concrete.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 3, "case3");

            // 4. 自分以外にだけ送る（押した側には届かないのが正しい）
            ((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(INetworkReceiver.Receive), 4, "case4");
        }
    }
}
