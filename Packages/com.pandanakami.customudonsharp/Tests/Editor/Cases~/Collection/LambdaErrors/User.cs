using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.CollectionLambdaErrors
{
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();
        private Dictionary<string, int> _dict = new Dictionary<string, int>();
        private bool _initial = new List<int>().Exists(x => x > 0);    // フィールドの初期化子の中

        private void Start()
        {
            System.Func<int, bool> f = x => x > 0;                      // List のメソッドの引数以外のラムダ
            bool w = _list.Any(x => x > 0);                             // LINQ（#58）
            bool b = _list.Exists(delegate (int x) { return x > 0; });  // delegate { }
            System.Predicate<int> p = null;
            bool c = _list.Exists(p);                                   // デリゲートの変数
            foreach (var kv in _dict)
                _list.RemoveAll(x => x == kv.Value);                    // kv をラムダの中で使う
        }
    }
}
