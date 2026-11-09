using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Generic
{
    // #59: ユーザー定義のジェネリクス（ジェネリッククラス・ジェネリックメソッド）のシナリオ。
    // 普通の C# でも Udon でも同じ結果になることを確かめる
    public class Bag<T>
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

        public T Last(bool empty) { return empty ? default(T) : _last; }

        public bool Has(T item) => _items.Contains(item);

        // クラスの中のジェネリックメソッド
        public Bag<U> Fill<U>(U value)
        {
            var result = new Bag<U>();
            for (int i = 0; i < _items.Count; i++)
                result.Add(value);
            return result;
        }
    }

    public class Pair<A, B>
    {
        public A first;
        public B second;

        public Pair(A first, B second)
        {
            this.first = first;
            this.second = second;
        }

        public string Show() => first + "/" + second;
    }

    public class Hero
    {
        public string name;
        public int hp;

        public Hero(string name, int hp)
        {
            this.name = name;
            this.hp = hp;
        }
    }

    public class Slot
    {
        public int n = 5;
    }

    // new() 制約付きのジェネリッククラス
    public class Factory<T> where T : new()
    {
        public T Make() => new T();
    }

    public static class GenericScenario
    {
        public static T Create<T>() where T : new() => new T();

        public static T Max<T>(List<T> list) where T : IComparable<T>
        {
            T best = list[0];
            foreach (T x in list)
                if (x.CompareTo(best) > 0)
                    best = x;
            return best;
        }

        // ジェネリックが別のジェネリックを呼ぶ
        public static T First<T>(List<T> list) { return Pick(list, 0); }

        private static T Pick<T>(List<T> list, int i) { return list[i]; }

        public static T Second<T>(this List<T> list) => list[1];

        public static int CountOf<T>(T[] array, T value)
        {
            int c = 0;
            foreach (T x in array)
                if (x.Equals(value))
                    c++;
            return c;
        }

        public static T Default<T>() => default(T);

        public static List<T> Repeat<T>(T value, int n)
        {
            var list = new List<T>();
            for (int i = 0; i < n; i++)
                list.Add(value);
            return list;
        }

        public static string Run()
        {
            string r = "";

            // a: ジェネリッククラス（int）
            var ints = new Bag<int>();
            ints.Add(3);
            ints.Add(5);
            ints.Add(4);
            r += "a:" + ints.Count + "," + ints.Get(1) + "," + ints.Last(false) + "," + ints.Last(true) + "," + ints.Has(5) + "," + ints.Has(9) + ";";

            // b: string・汎用クラスを型引数に
            var names = new Bag<string>();
            names.Add("x");
            names.Add("y");
            var units = new Bag<Hero>();
            units.Add(new Hero("ann", 30));
            units.Add(new Hero("bob", 50));
            r += "b:" + names.Get(0) + names.Last(false) + (names.Last(true) == null) + "," + units.Get(1).name + units.Count + (units.Last(true) == null) + ";";

            // c: クラスの中のジェネリックメソッド
            Bag<string> filled = ints.Fill("z");
            r += "c:" + filled.Count + filled.Get(2) + ";";

            // d: static のジェネリックメソッド（型の推論・連鎖・拡張メソッド・制約付きの CompareTo）
            var scores = new List<int> { 4, 9, 2 };
            var words = new List<string> { "pear", "apple", "plum" };
            r += "d:" + Max(scores) + "," + Max(words) + "," + Max(new List<float> { 1.5f, 0.5f }) + "," + First(words) + "," + scores.Second() + ";";

            // e: 型引数 2 つのクラスと、その List・LINQ
            var pairs = new List<Pair<string, int>>();
            pairs.Add(new Pair<string, int>("a", 1));
            pairs.Add(new Pair<string, int>("b", 2));
            int sum = 0;
            foreach (Pair<string, int> p in pairs)
                sum += p.second;
            r += "e:" + pairs[1].Show() + "," + sum + "," + pairs.Count(p => p.second > 1) + ";";

            // f: 配列・Equals・default(T)・型を書いた呼び出し
            r += "f:" + CountOf(new[] { 1, 2, 1 }, 1) + "," + CountOf(new[] { "a", "b" }, "b") + "," + Default<int>() + "," + (Default<string>() == null) + "," + Repeat("k", 3).Count + ";";

            // g: List を型引数に
            var nested = new Bag<List<int>>();
            nested.Add(new List<int> { 1, 2 });
            nested.Get(0).Add(3);
            r += "g:" + nested.Get(0).Count + "," + nested.Get(0)[2] + ";";

            // h: new T()（汎用クラス・List・値型）
            List<int> made = Create<List<int>>();
            made.Add(1);
            r += "h:" + Create<Slot>().n + "," + made.Count + "," + Create<int>() + "," + new Factory<Slot>().Make().n + ";";

            return r;
        }
    }
}
