using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;

namespace CustomUdonSharpTests.NetworkCallableOverload
{
    public class Target : UdonSharpBehaviour, IReceiver
    {
        // U# は改名前の名前で重複を調べるので、改名後に名前が重ならなくてもエラーになる
        [NetworkCallable]
        public void Receive(int value, string from)
        {
        }

        public void Receive(int value)
        {
        }
    }
}
