using UdonSharp;
using UnityEngine;

namespace ClassPoC
{
    // #53: 汎用クラスのコピーの中で起きた実行時例外の位置を確かめる。
    // Interact（または throwOnStart なら Start）で Faulty.Get(5) を呼び、範囲外で例外にする。
    // 期待：コンソールの "Udon runtime exception detected!" の位置が Faulty.cs の 11 行目になる
    // （直す前は、この RuntimeErrorProbe.cs の閉じ括弧の行になっていた）
    public class RuntimeErrorProbe : UdonSharpBehaviour
    {
        public bool throwOnStart;

        private void Start()
        {
            if (throwOnStart)
                Throw();
        }

        public override void Interact()
        {
            Throw();
        }

        private void Throw()
        {
            var f = new Faulty();
            Debug.Log("[ClassPoC] #53 calling Faulty.Get(5)");
            Debug.Log(f.Get(5));
        }
    }
}
