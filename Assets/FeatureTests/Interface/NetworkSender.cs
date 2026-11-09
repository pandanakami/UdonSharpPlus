using UdonSharp;
using VRC.Udon.Common.Interfaces;

namespace FeatureTests.Interface
{
    // #46, #51: 押したクライアントから、5 通りの呼び方で SendCustomNetworkEvent を送る
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

            // 5. 受け手の型が IUdonEventReceiver（相手のクラスが不明）でも、元の名前の文字列で届く（#51）
            ((IUdonEventReceiver)probe).SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 5, "case5");
        }
    }
}
