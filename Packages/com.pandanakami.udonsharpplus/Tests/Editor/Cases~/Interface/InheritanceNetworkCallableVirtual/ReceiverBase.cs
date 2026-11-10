using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;

namespace UdonSharpPlusTests.InheritanceNetworkCallableVirtual
{
    // #92: [NetworkCallable] の virtual は U# のエラー（インタフェースの実装でも同じ）
    public abstract class ReceiverBase : UdonSharpBehaviour, IReceiver
    {
        [NetworkCallable]
        public virtual void Receive(int value) { }
    }
}
