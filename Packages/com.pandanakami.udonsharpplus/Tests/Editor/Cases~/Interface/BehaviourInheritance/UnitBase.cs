using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace UdonSharpPlusTests.BehaviourInheritance
{
    // #92: abstract な基底の UdonSharpBehaviour がインタフェースを実装する。
    // Hp は virtual（派生クラスで override する）、Kind は基底だけで実装、Receive は基底だけで実装した [NetworkCallable]（U# は [NetworkCallable] の virtual を許さない）
    public abstract class UnitBase : UdonSharpBehaviour, IUnit
    {
        public int hp = 10;

        public virtual int Hp() { return hp; }

        public string Kind() { return "base"; }

        [NetworkCallable]
        public void Receive(int value) { hp += value; }
    }
}
