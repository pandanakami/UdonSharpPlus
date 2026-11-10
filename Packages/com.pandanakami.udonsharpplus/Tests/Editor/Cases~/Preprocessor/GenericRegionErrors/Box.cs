using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorGenericRegionErrors
{
    // #91: ジェネリッククラスでは、メンバーを #region で囲むだけでもエラー（メンバーごとに 1 行に直してコピーするため）
    public class Box<T>
    {
        #region data
        public T value;
        #endregion

        public T Get() { return value; }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var box = new Box<int>();
            Debug.Log(box.Get());
        }
    }
}
