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
            var t = (1, "a");                                // タプルの式
            (int x, string y) named = (2, "b");              // 名前付きのタプル（型と式で 1 つずつ）
            ValueTuple<int, string> vt = default;            // ValueTuple
            var (a, b) = Pair();                             // 分解
            int c = 1, d = 2;
            (c, d) = (d, c);                                 // 入れ替え
            foreach (var (k, v) in _dict) { }               // foreach での分解
            bool e = _ints.Exists(i => (i, i).Item1 == 0);  // ラムダの中
            Debug.Log(new Used().Range(1, 2));
            Debug.Log(new Pool<int>().Get(0));
            Debug.Log(new Pool<string>().Get(0));
        }

        private (int, int) Pair() { return (1, 2); }        // 戻り値

        private void Take((int, int) p) { }                  // 引数
    }
}
