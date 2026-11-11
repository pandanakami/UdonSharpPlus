using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorClosingBrace
{
    // #96: 閉じ括弧の直前の #if〜#endif。インタフェース・普通のクラス・List の生成メンバーが、#endif の行にならない
    public class EndIf : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            IUnit u = new Unit();
            _values.Add(u.Hp());
            Debug.Log(_values[0]);
        }
#if UNITY_EDITOR
        private void OnValidate() { }
#endif
    }
}
