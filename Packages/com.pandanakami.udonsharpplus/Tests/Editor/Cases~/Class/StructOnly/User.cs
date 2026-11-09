using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.StructOnly
{
    // 汎用クラスがなく、構造体だけを使う場合もエラーにする
    public struct Pair
    {
        public int a, b;
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Pair? p = null;
            Debug.Log(p.HasValue);
        }
    }
}
