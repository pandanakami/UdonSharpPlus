using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationAfterMultiLineLinq
{
    // #103: 複数行の LINQ のうしろの行のエラーが、元の行・列に出る（期待：19 行 35 列。UdonSharp はこのエラーで引数の位置を指す）
    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            int n = _values
                .Where(x => x > 0)
                .Select(x => x * 2)
                .Count();
            System.IO.File.Exists("a");
        }
    }
}
