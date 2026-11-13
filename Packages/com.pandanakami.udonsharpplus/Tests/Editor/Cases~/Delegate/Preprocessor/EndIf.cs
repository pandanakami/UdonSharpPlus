using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DelegatePreprocessor
{
    // #126 A11: 閉じ括弧の直前の #if〜#endif と、#region で囲んだ入れ子のデリゲートの宣言。
    // 宣言を消したところと、生成メンバー（中継メソッド・フィールド）を足すところで、プリプロセッサ指令が行頭からずれない
    public class EndIf : UdonSharpBehaviour
    {
        #region Delegates
        public delegate int Calc(int x);
        #endregion

        private Calc _calc;

        private static int Square(int x) => x * x;

        private void Start()
        {
            _calc = Square;
            Debug.Log(_calc(3));
        }
#if UNITY_EDITOR
        private void OnValidate() { }
#endif
    }
}
