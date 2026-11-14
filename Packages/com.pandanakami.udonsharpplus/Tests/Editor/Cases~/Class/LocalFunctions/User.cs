using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.LocalFunctions
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Counter().AddTwice());
            Debug.Log(Util.Sum(1, 2));
            Debug.Log(new Box<int>().Get());
            Debug.Log(new Box<string>().Get());
        }
    }
}
