using UdonSharp;

namespace UdonSharpPlusTests.InterfaceErrorInCopy
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            new Notifier().Send(1);
        }
    }
}
