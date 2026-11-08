using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace CustomUdonSharpTests.SdkInterface
{
    public class Target : UdonSharpBehaviour, IReceiver
    {
        [NetworkCallable]
        public void Receive(int value, string from)
        {
            Debug.Log(value);
        }

        public void Ping()
        {
            Debug.Log("ping");
        }
    }
}
