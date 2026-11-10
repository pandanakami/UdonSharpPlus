using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.TupleUnrelated
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Used().value);
        }
    }
}
