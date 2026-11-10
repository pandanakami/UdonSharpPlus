using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Syntax
{
    public class User : UdonSharpBehaviour
    {
        public SyntaxTarget target;

        private void Start()
        {
            Debug.Log(SyntaxScenario.Run(target));
        }
    }
}
