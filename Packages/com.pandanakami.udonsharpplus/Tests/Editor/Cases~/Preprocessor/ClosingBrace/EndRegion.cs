using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorClosingBrace
{
    // #96: クラスの中身全体を #region で囲む（閉じ括弧の直前が #endregion）
    public class EndRegion : UdonSharpBehaviour
    {
        #region all
        private List<int> _values = new List<int>();

        private void Start()
        {
            IUnit u = new Unit();
            _values.Add(u.Hp());
            Debug.Log(_values[0]);
        }
        #endregion
    }
}
