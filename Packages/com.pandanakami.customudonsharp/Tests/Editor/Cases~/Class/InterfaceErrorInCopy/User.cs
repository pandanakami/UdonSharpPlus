using UdonSharp;

namespace CustomUdonSharpTests.InterfaceErrorInCopy
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            new Notifier().Send(1);
        }
    }
}
