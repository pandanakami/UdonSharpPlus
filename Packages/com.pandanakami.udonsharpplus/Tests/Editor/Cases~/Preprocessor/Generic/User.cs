using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PreprocessorGeneric
{
    // #91: ジェネリッククラスのメソッドの中の #if。コピーを作るときも U# と同じシンボルで解析し、有効な側だけを使う
    // （エディタでは +100 の側。定数 100 があり、200 はない）
    public class Box<T>
    {
        public int bonus;

        public int Bonus()
        {
#if UNITY_EDITOR
            return bonus + 100;
#else
            return bonus + 200;
#endif
        }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var box = new Box<int>();
            Debug.Log(box.Bonus());
        }
    }
}
