using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationBehaviourLinqChain
{
    // #79: UdonSharpBehaviour の中の、LINQ のメソッドチェーンの途中のエラーが、元のソースの位置に出る
    public class User : UdonSharpBehaviour
    {
        private List<string> _names = new List<string>();

        private void Start()
        {
            Debug.Log(_names
                .Select(n => n + "x")
                .Where(n => System.IO.File.Exists(n))
                .Count());
        }
    }
}
