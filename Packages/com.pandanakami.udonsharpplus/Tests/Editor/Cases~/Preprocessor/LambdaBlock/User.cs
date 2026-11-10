using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorLambdaBlock
{
    // #91: ブロック形式のラムダの中で、文と文の間にある #if は、有効な側の文だけが使われる
    // （1 行に直すとき、文の前の #if・#else と無効な側の行がまとめて消える）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            _values.Add(1);
            _values.Add(5);
            int n = _values.Count(x =>
            {
#if UNITY_EDITOR
                return x > 2;
#else
                return x > 0;
#endif
            });
            Debug.Log(n);
        }
    }
}
