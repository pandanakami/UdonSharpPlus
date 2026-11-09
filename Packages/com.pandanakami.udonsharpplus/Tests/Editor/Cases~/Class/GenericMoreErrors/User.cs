using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassGenericMoreErrors
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Sorter<int>().Compare(1, 2));
            Debug.Log(new Registry<int>().value);
        }
    }
}
