using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLambdaExpressionAssignmentErrors
{
    public class Unit
    {
        public int hp;
        public Unit next;
    }

    // #73: 戻り値のあるラムダの本体での代入は、代入の値を式として使うことになるので、今までどおりエラー
    public class User : UdonSharpBehaviour
    {
        private List<Unit> _units = new List<Unit>();
        private List<int> _ints = new List<int> { 1, 2 };
        private Dictionary<int, int> _dict = new Dictionary<int, int>();

        private void Start()
        {
            List<int> set = _units.Select(u => u.hp = 1).ToList();
            List<int> set2 = _ints.Select(x => _dict[x] = 1).ToList();
            List<int> set3 = _ints.Select(x => _dict[x]++).ToList();
            Debug.Log(set.Count + set2.Count + set3.Count);

            // ラムダ・LINQ の中の、普通のクラスのほかの未対応の書き方（#75：巻き添えのエラーを出さない）
            bool any = _units.Any(u => u.next?.hp > 0);                 // ?.
            Unit found = _units.Find(u => u is Unit x && x.hp > 0);     // 型パターン
            _units.ForEach(u => Get().hp += 1);                         // 複合代入の受け側がメソッド呼び出し
            Debug.Log(any + " " + found);
        }

        private Unit Get() { return _units[0]; }
    }
}
