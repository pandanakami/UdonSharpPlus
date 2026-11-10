using System;
using UnityEngine;

namespace UdonSharpPlusTests.TupleErrors
{
    // UdonSharpBehaviour から使う：エラー
    public class Used
    {
        public (int, string) pair;

        public (int min, int max) Range(int a, int b) { return (Math.Min(a, b), Math.Max(a, b)); }
    }

    // ジェネリッククラスのコピーが 2 つあっても、同じ位置のエラーは 1 つ
    public class Pool<T>
    {
        public T Get(int i) { var (x, _) = (i, 0); return default(T); }
    }

    // 使わない：エラーにしない
    public class Unused
    {
        public (int, int) Pair() { return (1, 2); }
    }

    // UdonSharp と関係ないクラス：エラーにしない
    public class PlainBehaviour : MonoBehaviour
    {
        private (int, int) _pair = (1, 2);
    }
}
