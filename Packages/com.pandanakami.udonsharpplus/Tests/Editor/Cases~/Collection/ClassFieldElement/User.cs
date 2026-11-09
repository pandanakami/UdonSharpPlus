using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionClassFieldElement
{
    public class Unit
    {
        public List<int> hits = new List<int> { 0 };
        public Dictionary<string, int> counts = new Dictionary<string, int> { { "a", 0 } };
        public Unit inner;

        // 普通のクラスのメソッドの中（自分のフィールド）
        public void Hit() { hits[0] += 1; counts["a"]++; }
    }

    // #74: 普通のクラスのフィールドにある List / Dictionary の要素への複合代入・インクリメント
    public class User : UdonSharpBehaviour
    {
        private List<Unit> _units = new List<Unit>();

        private void Start()
        {
            var u = new Unit();
            u.inner = new Unit();
            u.hits[0] += 1;
            u.hits[0]++;
            --u.hits[0];
            u.counts["a"] += 2;
            u.counts["a"]++;
            u.inner.hits[0] += 3;                       // 入れ子のフィールド
            u.Hit();
            _units.Add(u);
            _units.ForEach(x => x.hits[0] += 1);        // 式形式のラムダ（#73）
            _units.ForEach(x => x.counts["a"]++);
            Debug.Log(u.hits[0] + u.counts["a"] + u.inner.hits[0]);
        }
    }
}
