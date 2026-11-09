using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassMoreErrors
{
    // #63：ドキュメントの未対応の書き方で、テストがなかったもの（UdonSharpBehaviour から使うとエラー）
    public abstract class Shape
    {
        public int size;
    }

    public class Outer
    {
        public class Inner
        {
            public int v;
        }
    }

    public class Vec
    {
        public int x;
        public static Vec operator +(Vec a, Vec b) { return new Vec(); }
    }

    public class Grid
    {
        private int[] _cells = new int[4];
        public int this[int i] { get { return _cells[i]; } set { _cells[i] = value; } }
    }

    public class Button
    {
        public event System.Action Clicked;
    }

    public class Named
    {
        public string name;
        public override string ToString() { return name; }
    }

    public class Bullet
    {
        public int damage;
        public bool IsHit(int armor) { return damage > armor; }
    }
}
