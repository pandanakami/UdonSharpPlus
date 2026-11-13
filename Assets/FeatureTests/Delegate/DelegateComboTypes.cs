using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Delegate
{
    // #126: デリゲートと、ほかの機能との組み合わせ

    public interface IComboTarget
    {
        int Hit(int dmg);
        void SetCalc(Calc c);
        int UseCalc(int x);
    }

    public delegate int HitTarget(IComboTarget t);              // A1 インタフェースを引数に持つ
    public delegate ComboItem MakeItem(int v);                   // A3 普通のクラスを返す
    public delegate int SumItems(ComboItem[] items);             // A3 普通のクラスの配列
    public delegate int OnBox(ComboBox<int> box);                // A4 ジェネリッククラスを引数に持つ
    public delegate int SumList(List<int> list);                 // A5
    public delegate List<int> MakeList(int n);                   // A5
    public delegate int SumDict(Dictionary<string, int> dict);   // A5
    public delegate Vector3 MoveVec(Vector3 v);                  // B1
    public delegate int SumArray(int[] values);                  // B1
    public delegate string NameOfObject(GameObject go);          // B1
    public delegate int ReadBehaviour(UdonSharpBehaviour b);     // B1
    public delegate int NoArgs();                                // B2
    public delegate int FiveArgs(int a, int b, int c, int d, int e);   // B2
    public delegate int ApplyCalc(Calc c, int x);                // B3 デリゲートを引数に持つ
    public delegate string TextMap(string s);                    // B4
    public delegate void LogText(string s);                      // B5
    public delegate float FloatMap(float f);                     // B6
    public delegate void Act();                                  // B7

    // A1・A3：インタフェースを実装する普通のクラス
    public class ComboItem : IComboTarget
    {
        public int v;
        private Calc _calc;

        public ComboItem(int v) { this.v = v; }

        public int Hit(int dmg) { v -= dmg; return v; }
        public void SetCalc(Calc c) { _calc = c; }
        public int UseCalc(int x) => _calc(x) + 1000;
    }

    // A3：デリゲートを受け取る static メソッド
    public static class ComboUtil
    {
        public static int Apply(Calc c, int x) => c(x) + 1;

        public static ComboItem[] Pair(MakeItem make) => new ComboItem[] { make(1), make(2) };
    }

    // A4：デリゲート型のフィールドを持つジェネリッククラス・デリゲートを受け取るジェネリックメソッド
    public class ComboBox<T>
    {
        public T value;
        public Calc calc;

        public ComboBox(T value) { this.value = value; }

        public int Run(int x) => calc(x);
    }

    public static class ComboGeneric
    {
        public static int CountThen<T>(T[] items, Calc c) => c(items.Length);
    }
}
