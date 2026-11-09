using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace UdonSharpPlusTests.NonConstantEventName
{
    public class NetworkTarget : UdonSharpBehaviour, IReceiver
    {
        [NetworkCallable]
        public void Receive(int v, string sender)
        {
            Debug.Log(v);
        }
    }
}
