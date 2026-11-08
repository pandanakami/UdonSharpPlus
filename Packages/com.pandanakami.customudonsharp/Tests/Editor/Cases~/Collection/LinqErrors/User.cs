using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.CollectionLinqErrors
{
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();
        private Dictionary<string, int> _dict = new Dictionary<string, int>();
        private IEnumerable<int> _field;                                 // IEnumerable の宣言
        private int _initial = new List<int>().Count(x => x > 0);       // フィールドの初期化子の中

        private void Start()
        {
            var groups = _list.GroupBy(x => x % 2).ToList();             // 未対応のメソッド
            var custom = _list.OrderBy(x => x, null).ToList();           // 比較子を渡す形
            var objects = _list.Select(x => new object()).OrderBy(o => o).ToList();   // 並べ替えられない型のキー
            var indexed = _list.Select((x, i) => x + i).ToList();        // 添字付きのラムダ
            bool d = _dict.Any(kv => kv.Value > 0);                      // Dictionary（後で）
            string joined = string.Join(",", _list.Select(x => x.ToString()));   // そのまま渡す
            var names = _list.Select(x => x.ToString()).ToList();
            string longest = names.Max();                                // 数値以外の Max
        }
    }
}
