using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationBehaviourLambda
{
    // #79: UdonSharpBehaviour の中のラムダのエラーが、元のソースの位置に出る
    public class User : UdonSharpBehaviour
    {
        private List<string> _names = new List<string>();

        private void Start()
        {
            Debug.Log(_names.Find(n => System.IO.File.Exists(n)));
        }
    }
}
