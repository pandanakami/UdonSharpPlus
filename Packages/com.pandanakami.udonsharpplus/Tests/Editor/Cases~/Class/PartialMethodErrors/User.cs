using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.PartialMethodErrors
{
    // #117: 普通のクラスの partial メソッドはエラー（UdonSharp も対応していない）。partial クラスそのものは使える（#93）
    public partial class Both
    {
        public int v;
        partial void OnHit();
        public void Hit() { OnHit(); }
    }

    public partial class Both
    {
        partial void OnHit() { v++; }
    }

    public partial class DeclarationOnly
    {
        public int v;
        partial void OnHit();
        public void Hit() { OnHit(); v++; }
    }

    public partial class Plain
    {
        public int a = 1;
    }

    public partial class Plain
    {
        public int Get() => a;
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var both = new Both();
            both.Hit();
            var only = new DeclarationOnly();
            only.Hit();
            Debug.Log(both.v + only.v + new Plain().Get());
        }
    }
}
