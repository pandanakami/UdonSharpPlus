using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FeatureTests.Anon
{
    // #70：匿名型を型引数にする（ジェネリックメソッドの引数・ジェネリッククラスの中で型引数をメンバーにする）
    public static class AnGen
    {
        public static T Id<T>(T x) => x;
        public static List<T> Wrap<T>(T x) => One(x);

        // 引数とは別の匿名型を、ジェネリックメソッドの中で作る
        public static string Describe<T>(T x)
        {
            var inner = new { Tag = "t", Count = 1 };
            return inner.Tag + inner.Count;
        }

        public static List<T> One<T>(T x)
        {
            var l = new List<T>();
            l.Add(x);
            return l;
        }
    }

    // クラスの型引数とメソッドの型引数を、同時に匿名型のメンバーにする
    public class AnPair<T>
    {
        public string Mix<U>(T a, U b) => new { a, b }.ToString();
    }

    public interface IAnShape { int Area(); }
    public class AnSquare : IAnShape { public int side = 3; public int Area() => side * side; }
    public class AnCircle : IAnShape { public int Area() => 31; }

    public class AnGenBox<T>
    {
        public T v;
        public int n;
    }

    public class AnHolder
    {
        public string name = "holder";
        public int hp = 4;

        public string Describe() => name + hp;

        // this をメンバーにする
        public string SelfPair()
        {
            var p = new { Self = this, hp };
            return p.Self.name + p.hp;
        }
    }

    public class AnBox<T>
    {
        public T value;
        public string Show() => new { value }.ToString();
    }

    // #70：匿名型（new { ... }）。普通のクラスの中で作る・使う
    public class AnItem
    {
        public string Name;
        public int Hp;

        public AnItem(string name, int hp) { Name = name; Hp = hp; }

        public string Label { get { return Name + "!"; } }

        public string Describe()
        {
            var p = new { Name, Hp };
            return p.ToString();
        }

        // 普通のクラスの中の LINQ で、匿名型に射影する
        public int Total(List<AnItem> list) => list.Select(i => new { i.Hp, Bonus = i.Hp / 2 }).Sum(a => a.Hp + a.Bonus);
    }
}
