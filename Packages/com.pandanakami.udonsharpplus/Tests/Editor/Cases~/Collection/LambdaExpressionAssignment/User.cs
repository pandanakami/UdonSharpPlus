using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLambdaExpressionAssignment
{
    public class Unit
    {
        public int hp;
        public string Name { get; set; }
        public int Power
        {
            get { return hp; }
            set { hp = value; }
        }

        public List<int> hits = new List<int> { 0 };

        // 普通のクラスのメソッドの中（コピーの中）でも同じ
        public void Clear(List<Unit> others) { others.ForEach(o => o.hp = hp); }
    }

    // #73: 戻り値が void のラムダ（ForEach）の式の本体は、文と同じ扱い
    public class User : UdonSharpBehaviour
    {
        private List<Unit> _units = new List<Unit>();
        private List<int> _ints = new List<int> { 1, 2 };
        private Dictionary<int, int> _dict = new Dictionary<int, int>();

        private void Start()
        {
            int damage = 2;
            _units.ForEach(u => u.hp -= damage);      // フィールドへの複合代入
            _units.ForEach(u => u.hp = 0);            // フィールドへの代入
            _units.ForEach(u => u.hp++);              // フィールドのインクリメント
            _units.ForEach(u => u.Name = "x");        // 自動実装のプロパティ
            _units.ForEach(u => u.Power += 1);        // 本体付きのプロパティ
            _units.ForEach(u => u.hits[0] = 1);       // 要素の List の要素への代入
            _ints.ForEach(x => _dict[x] = x);         // Dictionary の要素への代入
            _ints.ForEach(x => _dict[x]++);           // Dictionary の要素のインクリメント
            _ints.ForEach(x => _ints[0] += x);        // List の要素への複合代入
            new Unit().Clear(_units);
            Debug.Log(_units.Count + _dict.Count);
        }
    }
}
