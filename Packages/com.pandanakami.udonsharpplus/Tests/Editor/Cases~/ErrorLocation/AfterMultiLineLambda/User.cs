using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationAfterMultiLineLambda
{
    // #103: 複数行のブロックのラムダのうしろの行のエラーが、元の行・列に出る（期待：19 行 35 列。UdonSharp はこのエラーで引数の位置を指す）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            int found = _values.Find(x =>
            {
                return x > 1;
            });
            Debug.Log(found);
            System.IO.File.Exists("a");
        }
    }
}
