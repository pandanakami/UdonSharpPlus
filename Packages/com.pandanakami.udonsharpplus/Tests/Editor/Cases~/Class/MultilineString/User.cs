using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.MultilineString
{
    // #93: 改行を含む逐語的文字列（@"..."）。普通のクラス・ラムダ・ジェネリクスのコピーは 1 行に直すので、
    // エスケープした表記（"\r\n"）に変えて値を保つ。このファイルは CRLF
    public class Note
    {
        public string Text()
        {
            return @"plain
line2";
        }
    }

    public class Box<T>
    {
        public T value;

        public string Text()
        {
            return @"generic
line2";
        }
    }

    public class User : UdonSharpBehaviour
    {
        private List<string> _lines = new List<string>();

        private void Start()
        {
            _lines.Add("x");
            int n = _lines.Count(s => s != @"lambda
line2");
            Debug.Log(new Note().Text() + new Box<int>().Text() + n);
        }
    }
}
