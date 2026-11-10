using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorBehaviour
{
    public interface IUnit { int Hp(); }

    public class Unit : IUnit
    {
        public int hp = 3;
        public int Hp() { return hp; }
    }

    // #91: UdonSharpBehaviour の中の #if。U# と同じく COMPILER_UDONSHARP・UNITY_EDITOR を定義して解析するので、
    // 有効な側だけが書き換えの対象になる（!COMPILER_UDONSHARP の中は U# からは見えない）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();
#if UNITY_EDITOR
        private int _editorOnly = 10;
#endif

#if !COMPILER_UDONSHARP
        // U# には見えないので、UdonSharp で使えない型（HashSet）を書いてもよい。
        // クラスの最後（閉じ括弧の直前）に置くと、今は CS1040 になる（不具合のチケットを参照）
        private void OnValidate()
        {
            var set = new HashSet<int>();
            set.Add(1);
        }
#endif

        private void Start()
        {
            IUnit u = new Unit();
#if UNITY_EDITOR
            _values.Add(u.Hp() + _editorOnly);
#else
            _values.Add(u.Hp());
#endif
            Debug.Log(_values[0]);
        }
    }
}
