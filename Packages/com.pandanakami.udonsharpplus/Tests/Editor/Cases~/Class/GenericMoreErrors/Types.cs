using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassGenericMoreErrors
{
    // #63：ドキュメントの未対応の書き方で、テストがなかったもの
    public class Sorter<T>
    {
        public int Compare(T a, T b) { return Comparer<T>.Default.Compare(a, b); }
    }

    public class Registry<T>
    {
        public static int count;   // ジェネリッククラスの static フィールド
        public T value;
    }
}
