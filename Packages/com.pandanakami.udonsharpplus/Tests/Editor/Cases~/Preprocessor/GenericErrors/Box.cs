using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorGenericErrors
{
    // #91: ジェネリッククラスのメソッドの中のプリプロセッサ指令はエラー
    public class Box<T>
    {
        public T value;

        public T Get()
        {
#if UNITY_EDITOR
            return value;
#else
            return default;
#endif
        }
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
