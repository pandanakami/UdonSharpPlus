using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ExpressionBodied
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Box b = new Box(3);
            b.W = 4;
            b.Bump();
            Debug.Log(b.v);
            b.Reset();
            Debug.Log(b.v + b.W);
        }
    }
}
