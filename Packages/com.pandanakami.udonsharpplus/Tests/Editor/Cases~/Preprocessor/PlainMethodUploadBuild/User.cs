using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorPlainMethodUploadBuild
{
    // #91: 普通のクラスのメソッドの中の #if・#region・#pragma。有効な側だけを使う
    // （エディタでは +100 の側。アップロード用のビルドの同じソースは PlainMethodUploadBuild で、+200 の側）
    public class Unit
    {
        public int hp = 3;

        public int Get()
        {
            #region body
#pragma warning disable 0168
            int unused;
#pragma warning restore 0168
#if UNITY_EDITOR
            return hp + 100;
#else
            return hp + 200;
#endif
            #endregion
        }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var u = new Unit();
            Debug.Log(u.Get());
        }
    }
}
