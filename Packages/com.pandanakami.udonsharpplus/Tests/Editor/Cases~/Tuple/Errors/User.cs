using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.TupleErrors
{
    public class User : UdonSharpBehaviour
    {
        private (int, string) _field;
        private List<(int, string)> _list;
        private Dictionary<string, int> _dict;
        private List<int> _ints;

        private void Start()
        {
            var t = (1, "a");                                // タプルの式（使える）
            (int x, string y) named = (2, "b");              // 名前付きのタプル（使える）
            t.Item1 = 5;                                     // 要素への代入：エラー
            object o = t;                                    // object にする：エラー
            int c = 1, d = 2;
            (c, d) = (d, c);                                 // 入れ替え（使える）
            foreach (var (k, v) in _dict) { }               // KeyValuePair の分解：エラー
            var keyed = new Dictionary<(int, int), int>();   // タプルをキーにする Dictionary：エラー
            Debug.Log(new Used().Range(1, 2));
            Debug.Log(new Pool<int>().Get(0));
            Debug.Log(new Pool<string>().Get(0));
        }

        private (int, int) Pair() { return (1, 2); }        // 戻り値

        private void Take((int, int) p) { }                  // 引数
    }
}
