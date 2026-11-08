using System.Collections.Generic;

namespace CustomUdonSharpTests.ClassGeneric
{
    // ジェネリッククラス（#59）
    public class Pool<T>
    {
        private List<T> _items = new List<T>();
        private T _last;

        public int Count => _items.Count;

        public void Add(T item)
        {
            _items.Add(item);
            _last = item;
        }

        public T Get(int i) { return _items[i]; }

        public T LastOrDefault(bool empty) { return empty ? default(T) : _last; }

        public bool Has(T item) => _items.Contains(item);

        // クラスの中のジェネリックメソッド
        public Pool<U> Fill<U>(U value)
        {
            var result = new Pool<U>();
            for (int i = 0; i < _items.Count; i++)
                result.Add(value);
            return result;
        }
    }
}
