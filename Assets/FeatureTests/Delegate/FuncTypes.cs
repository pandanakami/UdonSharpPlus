using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Delegate
{
    // #125：.NET のデリゲート（Action・Func・Predicate・Comparison）を使う普通のクラス
    public class FnBox
    {
        public int v;
        public System.Func<int, int> map;   // 普通のクラスのフィールド（完全修飾名で書いても同じ型）

        public FnBox(int v) { this.v = v; }

        public int Get(int x) => v + x;
        private bool Over(int x) => x > v;

        public Predicate<int> MakeOver() => Over;
        public Func<int, int> MakeAdd() => x => x + v;

        // 普通のクラスの中で、デリゲートの変数を LINQ・List のメソッドに渡す
        public int SumMapped(List<int> list) => list.Select(map).Sum();

        public int CountOver(List<int> list)
        {
            Predicate<int> p = Over;
            return list.FindAll(p).Count;
        }

        // Func を返す Func（カリー化）
        public static Func<int, Func<int, int>> Curry() => a => b => a * 10 + b;
    }

    // #134：インタフェースのメソッドの Func 引数・戻り値（普通のクラスと UdonSharpBehaviour の実装）
    public interface IFnSink
    {
        int Feed(Func<int, int> f);
        Func<int, int> Make(int k);
    }

    public class FnSinkPlain : IFnSink
    {
        public int Feed(Func<int, int> f) => f(10) + 1;
        public Func<int, int> Make(int k) => x => x + k;
    }

    // #134：Func を受け取る・返すジェネリックメソッド（型引数ごとのコピーになる）
    public static class FnGen
    {
        public static T Apply<T>(Func<T, T> f, T x) => f(x);
        public static List<R> Map<T, R>(List<T> list, Func<T, R> f) => list.Select(f).ToList();
        public static int CountIf<T>(List<T> list, Func<T, bool> p) => list.Where(p).Count();
        public static Func<T, T> Twice<T>(Func<T, T> f) => x => f(f(x));
    }

    // ジェネリッククラスの中の Func<T, T>（型引数ごとのコピーで、Func<int, int>・Func<string, string> になる）
    public class FnHolder<T>
    {
        public Func<T, T> f;

        public T Apply(T x) => f(x);
        public List<T> Map(List<T> list) => list.Select(f).ToList();
    }
}
