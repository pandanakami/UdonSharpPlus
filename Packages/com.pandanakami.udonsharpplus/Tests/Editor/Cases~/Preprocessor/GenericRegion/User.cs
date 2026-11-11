using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorGenericRegion
{
    // #91: ジェネリッククラスのメンバーを囲む #region・#if、メソッドの中の #pragma
    public class Box<T>
    {
        #region data
        public T value;
        #endregion

#if UNITY_EDITOR
        public int EditorOnly() { return 100; }
#endif

        public T Get()
        {
#pragma warning disable 0168
            int unused;
#pragma warning restore 0168
            return value;
        }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var box = new Box<int>();
            Debug.Log(box.Get() + box.EditorOnly());
        }
    }
}
