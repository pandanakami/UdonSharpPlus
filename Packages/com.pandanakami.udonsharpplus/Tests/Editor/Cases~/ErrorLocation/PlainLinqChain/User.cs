using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationPlainLinqChain
{
    public class User : UdonSharpBehaviour
    {
        private List<string> _names = new List<string>();

        private void Start()
        {
            Debug.Log(new Finder().Count());
        }
    }
}
