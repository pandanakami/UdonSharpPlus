using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorLambdaExpression
{
    // #91: 式のラムダの、式の途中の #if（以前は「ラムダの中のプリプロセッサ指令は未対応」）。有効な側だけを使う
    // （エディタでは > 100 の側）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            _values.Add(150);
            int n = _values.Count(x =>
#if UNITY_EDITOR
                x > 100
#else
                x > 200
#endif
            );
            Debug.Log(n);
        }
    }
}
