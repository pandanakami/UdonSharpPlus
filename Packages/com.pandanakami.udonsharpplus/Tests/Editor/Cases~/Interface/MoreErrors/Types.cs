using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.InterfaceMoreErrors
{
    // #63：ドキュメントの未対応の書き方で、テストがなかったもの
    public interface IHittable
    {
        void Hit(int damage);
    }

    // イベントは警告を出して無視する
    public interface INotifier
    {
        event System.Action Changed;
        void Notify();
    }

    // in / params の引数
    public interface IReader
    {
        void ReadIn(in int value);
        void ReadParams(params int[] values);
    }
}
