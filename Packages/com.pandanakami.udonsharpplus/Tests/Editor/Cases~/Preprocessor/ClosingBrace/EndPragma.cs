using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorClosingBrace
{
    // #96: 閉じ括弧の直前の #pragma・#nullable と、コメント
    public class EndPragma : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            IUnit u = new Unit();
            _values.Add(u.Hp());
            Debug.Log(_values[0]);
        }
#pragma warning disable 0414
        private int _unused = 1;
#pragma warning restore 0414
#nullable disable
        // 最後のコメント
    }
}
