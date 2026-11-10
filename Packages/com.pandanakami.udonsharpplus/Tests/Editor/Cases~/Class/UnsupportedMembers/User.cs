using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.UnsupportedMembers
{
    // #93: 普通のクラスの、未対応のメンバー
    public class WithStaticConstructor
    {
        public int v;
        static WithStaticConstructor() { }
    }

    public class WithFinalizer
    {
        public int v;
        ~WithFinalizer() { }
    }

    public class WithStaticAutoProperty
    {
        public int v;
        public static int Count { get; set; }
    }

    // 継承は未対応。そのうえ override もエラー（ToString・Equals・GetHashCode 以外）
    public class BaseItem
    {
        public virtual int Value() { return 1; }
    }

    public class DerivedItem : BaseItem
    {
        public override int Value() { return 2; }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new WithStaticConstructor().v + new WithFinalizer().v + new WithStaticAutoProperty().v + new DerivedItem().Value());
        }
    }
}
