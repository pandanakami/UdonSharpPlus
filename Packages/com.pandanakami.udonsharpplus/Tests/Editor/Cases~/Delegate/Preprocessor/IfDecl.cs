using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DelegatePreprocessor
{
    // #126 A11: #if の中のデリゲートの宣言（有効な側だけが使われる）と、ファイルの最後の #endif
#if COMPILER_UDONSHARP
    public delegate string Label(int x);
#else
    public delegate string Label(string x);
#endif

    public class IfDecl : UdonSharpBehaviour
    {
        private static string Name(int x) => "n" + x;

        private void Start()
        {
            Label l = Name;
            Debug.Log(l(1));
        }

        // 入れ子の宣言の直後が #if
        public delegate void Done();
#if UNITY_EDITOR
        private void Reset() { }
#endif
        private void Finish() { }

        private void Update()
        {
            Done d = Finish;
            d();
        }
    }
}
#if UNITY_EDITOR
// エディタだけ
#endif
