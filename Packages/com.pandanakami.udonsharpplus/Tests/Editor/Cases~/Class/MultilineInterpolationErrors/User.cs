using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.MultilineInterpolationErrors
{
    // #93: 改行を含む補間文字列は、普通のクラス・ラムダ・ジェネリクスのコピーの中ではエラー（元の位置に出る。#97）
    public class Note
    {
        public int hp;

        public string Text()
        {
            return $@"hp={hp}
end";
        }
    }

    public class Box<T>
    {
        public T value;

        public string Text()
        {
            return $@"{value}
end";
        }
    }

    public class User : UdonSharpBehaviour
    {
        private List<int> _values = new List<int>();

        private void Start()
        {
            _values.Add(1);
            var texts = _values.Select(x => $@"{x}
").ToList();
            Debug.Log(texts.Count + new Box<int>().Text() + new Note().Text());
        }
    }
}
