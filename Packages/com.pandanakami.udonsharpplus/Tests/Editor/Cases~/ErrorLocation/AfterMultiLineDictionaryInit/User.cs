using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationAfterMultiLineDictionaryInit
{
    // #103: 複数行の Dictionary の初期化子（メソッドの中）のうしろの行のエラーが、元の行・列に出る（期待：18 行 35 列。UdonSharp はこのエラーで引数の位置を指す）
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var scores = new Dictionary<string, int>
            {
                { "a", 1 },
                ["b"] = 2,
            };
            Debug.Log(scores.Count);
            System.IO.File.Exists("a");
        }
    }
}
