using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.FormErrors
{
    // #94: List / Dictionary / LINQ の、未対応の書き方のエラー
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();
        private Dictionary<string, int> _dict = new Dictionary<string, int>();

        private void Start()
        {
            // List・配列以外から List を作る（Keys は foreach と .Count でだけ使える、のエラーも出る）
            var keys = new List<string>(_dict.Keys);
            // 比較子を渡して作る
            var ordinal = new Dictionary<string, int>(StringComparer.Ordinal);
            // 要素が 3 つの初期化子
            var three = new Dictionary<string, int> { { "a", 1, 2 } };
            // List・配列以外からの追加
            _list.AddRange(_dict.Values);
            // LINQ 以外の拡張メソッド（.NET の CollectionExtensions）
            Debug.Log(_dict.GetValueOrDefault("a"));
            // List・配列・Dictionary 以外への LINQ
            Debug.Log("abc".Any(c => c == 'a'));
            // 受け側のない LINQ
            Debug.Log(Enumerable.Empty<int>().Count());
        }
    }
}
