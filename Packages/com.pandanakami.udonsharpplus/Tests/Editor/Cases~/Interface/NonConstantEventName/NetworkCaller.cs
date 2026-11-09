using UdonSharp;
using VRC.Udon.Common.Interfaces;

namespace UdonSharpPlusTests.NonConstantEventName
{
    public class NetworkCaller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public string eventName = "Receive";

        private void Start()
        {
            // 変数で渡したイベント名もそのまま届く（#51 でメソッド名が元の名前になり、書き換えも警告も不要になった）
            ((NetworkTarget)target).SendCustomNetworkEvent(NetworkEventTarget.All, eventName, 1, "typed");

            // 受け手が UdonSharpBehaviour 型でも同じ
            target.SendCustomNetworkEvent(NetworkEventTarget.All, eventName, 2, "plain");
        }
    }
}
