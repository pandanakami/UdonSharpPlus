using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLambdaErrors
{
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();
        private Dictionary<string, int> _dict = new Dictionary<string, int>();
        private bool _initial = new List<int>().Exists(x => x > 0);    // フィールドの初期化子の中

        private void Start()
        {
            bool a = System.Array.Exists(new int[0], x => x > 0);       // List のメソッドの引数以外のラムダ（Func などの変数には #125 から入れられる）
            bool b = _list.Exists(delegate (int x) { return x > 0; });  // delegate { }
            System.Converter<int, int> p = null;                        // 対応しない .NET のデリゲート（Func などの変数は #125 から渡せる）
            var c = _list.ConvertAll(p);                                // 対応しないデリゲートの変数
            foreach (var kv in _dict)
                _list.RemoveAll(x => x == kv.Value);                    // kv をラムダの中で使う（#61 から使える）
        }
    }
}
