using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorRegionPragma
{
    public class Unit
    {
        #region data
        public int hp = 3;
        #endregion

        public int Get() { return hp; }
    }

    // #91: #region・#pragma。UdonSharpBehaviour の途中とメソッドの中、普通のクラスのメンバーを囲むもの、ラムダの中は使える。
    // クラスの閉じ括弧の直前に置くと、今は CS1040 になる（#96）
    public class User : UdonSharpBehaviour
    {
        #region fields
        private List<int> _values = new List<int>();
#pragma warning disable 0414
        private int _unused = 1;
#pragma warning restore 0414
        #endregion

        private void Start()
        {
            #region setup
            var u = new Unit();
            _values.Add(u.Get());
            #endregion
#pragma warning disable 0168
            int notUsed;
#pragma warning restore 0168
            int n = _values.Count(x =>
            {
                #region body
                return x > 0;
                #endregion
            });
            Debug.Log(n);
        }
    }
}
