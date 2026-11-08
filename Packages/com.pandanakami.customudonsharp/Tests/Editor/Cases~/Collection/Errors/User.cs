using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.CollectionErrors
{
    public class User : UdonSharpBehaviour
    {
        public List<int> publicList;                       // シリアライズは未対応（エラー）
        [SerializeField] private Dictionary<string, int> serialized;   // 同上
        private List<int> _list = new List<int>();
        private Dictionary<string, int> _dict = new Dictionary<string, int>();

        private void Start()
        {
            _list.CopyTo(new int[1]);                      // 未対応のメソッド
            Debug.Log(string.Join(",", _list));            // IEnumerable として渡す
            while (_dict.TryGetValue("a", out int v))      // ループの条件の中の TryGetValue
                _dict.Remove("a");
            int c = _list?.Count ?? 0;                     // ?.
            foreach (var (k, value) in _dict)              // 分解
                Debug.Log(k);
            foreach (var kv in _dict)
                Debug.Log(kv);                             // kv をそのまま使う（#61 から使える。object[] になる）
            int x2 = _list[0] = 5;                         // 代入を式として使う
            GetList()[0] += 1;                             // 複合代入の受け側がメソッド呼び出し
            Debug.Log(_dict.Keys);                         // Keys を foreach / .Count 以外で使う
        }

        private List<int> GetList() { return _list; }
    }
}
