using UdonSharp;
using VRC.Udon.Common.Interfaces;

namespace UdonSharpPlusTests.NetworkCallable
{
    public class NetworkCaller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;

        private void Start()
        {
            // インタフェース経由のローカル呼び出し
            IReceiver receiver = (IReceiver)target;
            receiver.Receive(1, "local");

            // ネットワーク越し：インタフェースの nameof / 具象型の nameof / 文字列リテラル
            ((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(IReceiver.Receive), 2, "interface-nameof");
            NetworkTarget concrete = (NetworkTarget)target;
            concrete.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetworkTarget.Receive), 3, "concrete-nameof");
            concrete.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 4, "concrete-literal");
        }
    }
}
