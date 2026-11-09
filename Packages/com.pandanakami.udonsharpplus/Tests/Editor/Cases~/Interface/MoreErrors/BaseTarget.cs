using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.InterfaceMoreErrors
{
    // 基底の UdonSharpBehaviour に、インタフェース実装メソッドと同じ名前のメソッドがある
    public class BaseTarget : UdonSharpBehaviour
    {
        public void Hit() { }
    }
}
