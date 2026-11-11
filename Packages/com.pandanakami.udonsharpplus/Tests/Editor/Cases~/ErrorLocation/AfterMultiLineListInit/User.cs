using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationAfterMultiLineListInit
{
    // #103: 複数行の List の初期化子のうしろの行のエラーが、元の行・列に出る（期待：18 行 35 列。UdonSharp はこのエラーで引数の位置を指す）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>
        {
            1,
            2,
        };

        private void Start()
        {
            System.IO.File.Exists("a");
        }
    }
}
