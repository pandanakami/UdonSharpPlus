using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Comments
{
    public class Unit
    {
        public int hp = 3;

        /// <summary>ドキュメントコメント</summary>
        public int Get()
        {
            // 行コメント
            /* 複数行の
               ブロックコメント */
            return hp;
        }
    }

    // #91: コメントは、生成したメンバーと同じ行になっても壊れない（指令と違い、行頭でなくてよい）。
    // 普通のクラスのメソッドの中、UdonSharpBehaviour の閉じ括弧の直前、閉じ括弧と同じ行
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var u = new Unit();
            Debug.Log(u.Get());
        }

        /// <summary>最後のドキュメントコメント</summary>
        // 最後の行コメント
        /* 最後の
           ブロックコメント */
    } // 閉じ括弧の行のコメント
}
