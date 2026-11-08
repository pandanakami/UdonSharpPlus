using UdonSharp;

namespace InterfacePoC
{
    // #46: このクライアントの表示だけを消す
    public class NetworkResetButton : UdonSharpBehaviour
    {
        public NetworkProbe probe;

        public override void Interact()
        {
            probe.ResetResults();
        }
    }
}
