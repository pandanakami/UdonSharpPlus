using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationSharedClassOwnError
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Node().NextValue());
            Debug.Log(new Box<int>().Value());
            Debug.Log(new Box<string>().Value());
        }
    }
}
