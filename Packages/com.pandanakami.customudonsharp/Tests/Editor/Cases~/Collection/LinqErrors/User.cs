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
        private IEnumerable<int> _field = new List<int>();               // List を IEnumerable として扱う（#62）
        private int _initial = new List<int>().Count(x => x > 0);       // フィールドの初期化子の中

        private void Start()
        {
            var groups = _list.GroupBy(x => x % 2).ToList();             // 未対応のメソッド
            var custom = _list.OrderBy(x => x, null).ToList();           // 比較子を渡す形
            var objects = _list.Select(x => new object()).OrderBy(o => o).ToList();   // 並べ替えられない型のキー
            var indexed = _list.Select((x, i) => x + i).ToList();        // 添字付きのラムダ
            bool d = _dict.Any(kv => kv.Value > 0);                      // Dictionary（#61 から使える）
            string joined = string.Join(",", _list.Select(x => x.ToString()));   // そのまま渡す
            var names = _list.Select(x => x.ToString()).ToList();
            string longest = names.Max();                                // 数値以外の Max
            IEnumerable<int> local = _list.Where(x => x > 0);            // LINQ の結果を IEnumerable の変数に入れる（#62）
            local = _list;                                               // 代入（#62）
            var e = _list.GetEnumerator();                               // GetEnumerator
            foreach (string key in VRC.SDK3.Persistence.PlayerData.GetKeys(VRC.SDKBase.Networking.LocalPlayer))   // 外部の IEnumerable を回す（#62）
                Debug.Log(key);
            int n = VRC.SDK3.Persistence.PlayerData.GetKeys(VRC.SDKBase.Networking.LocalPlayer).Count();         // 外部の IEnumerable に LINQ（#62）
        }

        private IEnumerable<int> Positive() { return _list.Where(x => x > 0); }   // 戻り値（#62）
        private IList<int> AsList() => _list;                                       // 式の本体（#62）
        private void Dummy()
        {
        }
    }
}
