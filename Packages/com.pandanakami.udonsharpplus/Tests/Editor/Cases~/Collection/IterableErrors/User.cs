using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.IterableErrors
{
    // #99: 中を回せないものを LINQ・foreach に渡したときのエラーを、書き方に合わせた文言にする
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();

        private void Start()
        {
            // Enumerable.Range・Repeat・Empty（元を取らない生成のメソッド）
            Debug.Log(Enumerable.Range(0, 3).Count());
            var repeated = Enumerable.Repeat(1, 3);
            foreach (int x in repeated)            // 宣言の場所でエラーにしているので、ここでは出さない
                Debug.Log(x);
            Debug.Log(Enumerable.Empty<int>().Count());

            // クエリ構文
            var query = from x in _list where x > 0 select x;
            Debug.Log(query.Count());              // 宣言の場所でエラーにしているので、ここでは出さない
            Debug.Log((from x in _list select x * 2).Count());
            foreach (int x in from y in _list select y)
                Debug.Log(x);

            // 自分で IEnumerable 型にした変数
            int[] array = new int[0];
            IEnumerable<int> items = array;
            Debug.Log(items.Any(x => x > 0));
            foreach (int x in items)
                Debug.Log(x);
        }
    }
}
