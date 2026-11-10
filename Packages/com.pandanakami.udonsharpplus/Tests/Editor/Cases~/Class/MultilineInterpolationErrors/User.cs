using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.MultilineInterpolationErrors
{
    // #93: 改行を含む補間文字列は、ラムダ・ジェネリクスのコピーの中ではエラー
    // （普通のクラスのメソッドの中も同じだが、今は位置が出ない不具合がある：#97）
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
            Debug.Log(texts.Count + new Box<int>().Text());
        }
    }
}
