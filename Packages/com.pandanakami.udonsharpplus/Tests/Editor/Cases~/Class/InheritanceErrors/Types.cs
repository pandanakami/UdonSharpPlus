using UnityEngine;

namespace UdonSharpPlusTests.InheritanceErrors
{
    // 外部のクラスを継承したクラス：UdonSharpBehaviour から使うとエラー
    public class Settings : ScriptableObject
    {
        public int level;
    }

    public class Helper : MonoBehaviour
    {
        public int v;
    }

    public class GameError : System.Exception
    {
        public int code;
    }

    // ソース上の普通のクラスを継承したクラス：エラー（#49 の検査）
    public class Shape
    {
        public int size;
    }

    public class Box : Shape
    {
        public int depth;
    }

    // 基底だけを使う：エラーにしない
    public class Animal
    {
        public int legs;
    }

    public class Dog : Animal
    {
    }

    // 属性：エラーにしない
    public class NoteAttribute : System.Attribute
    {
    }

    // 使わない：エラーにしない
    public class UnusedSettings : ScriptableObject
    {
    }
}
