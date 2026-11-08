using System;
using System.Collections.Generic;

namespace CustomUdonSharpTests.ClassGeneric
{
    public static class Util
    {
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
    }

    public class Item
    {
        public string name;
        public Item(string name) { this.name = name; }
    }
}
