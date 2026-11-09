using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Combination
{
    public class User : UdonSharpBehaviour
    {
        public ComboGolem golem;

        private void Start()
        {
            golem.ResetUnit();
            Debug.Log(ComboScenario.Run(golem));
        }
    }
}
