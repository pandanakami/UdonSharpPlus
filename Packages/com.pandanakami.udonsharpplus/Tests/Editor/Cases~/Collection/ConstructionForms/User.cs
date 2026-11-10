using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ConstructionForms
{
    // #94: 作り方。容量の指定は無視し、Dictionary からのコピーは ShallowClone にする
    public class User : UdonSharpBehaviour
    {
        private Dictionary<string, int> _source = new Dictionary<string, int> { { "a", 1 } };

        private void Start()
        {
            var list = new List<int>(10);
            list.Add(1);
            var copy = new Dictionary<string, int>(_source);
            copy["b"] = 2;
            Debug.Log(list.Count + copy.Count + _source.Count);
        }
    }
}
