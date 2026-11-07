using UdonSharp;
using VRC.Udon.Common.Interfaces;

namespace CustomUdonSharpTests.NonConstantEventName
{
    public class NetworkCaller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public string eventName = "Receive";

        private void Start()
        {
            // 受け手の型に改名対象のメソッドがある：書き換えられないので警告が出る（#47）
            ((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, eventName, 1, "warned");

            // 受け手が UdonSharpBehaviour 型：対象が分からないので警告しない
            target.SendCustomNetworkEvent(NetworkEventTarget.All, eventName, 2, "not-warned");
        }
    }
}
