using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorPlainMembers
{
    // #91: 普通のクラス・インタフェースのメンバーやクラス全体を #if で囲むのは使える（メソッドの中は未対応）
    public interface IUnit
    {
        int Hp();
#if UNITY_EDITOR
        string DebugName();
#endif
    }

    public class Unit : IUnit
    {
        #region data
        public int hp = 3;
#if UNITY_EDITOR
        public int editorBonus = 10;
#endif
        #endregion

        public int Hp() { return hp; }

#if UNITY_EDITOR
        public string DebugName() { return "unit" + editorBonus; }
#endif
    }

#if !COMPILER_UDONSHARP
    // U# には見えないクラス
    public class EditorOnlyHelper
    {
        public System.Collections.Generic.HashSet<int> set;
    }
#endif

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            IUnit u = new Unit();
            Debug.Log(u.Hp());
#if UNITY_EDITOR
            Debug.Log(u.DebugName());
#endif
        }
    }
}
