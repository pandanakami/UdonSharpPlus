using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.ClassGeneric
{
    public class User : UdonSharpBehaviour
    {
        private Pool<int> _ints = new Pool<int>();
        private List<Pool<Item>> _pools = new List<Pool<Item>>();

        private void Start()
        {
            _ints.Add(3);
            _ints.Add(5);
            var names = new Pool<string>();
            names.Add("a");
            var items = new Pool<Item>();
            items.Add(new Item("x"));
            _pools.Add(items);

            var scores = new List<int> { 4, 9, 2 };
            int max = Util.Max(scores);
            string first = Util.First(new List<string> { "p", "q" });
            int second = scores.Second();
            Pool<string> texts = _ints.Fill("z");

            int[] arr = { 1, 2 };
            Swap(arr, 0, 1);
            Debug.Log(Echo<string>(first) + max + second + texts.Get(0) + names.Has("a") + _ints.LastOrDefault(false) + items.Get(0).name + arr[0]);
        }

        // UdonSharpBehaviour のジェネリックメソッド
        private void Swap<T>(T[] a, int i, int j)
        {
            T t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        private T Echo<T>(T value) => value;
    }
}
