using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;

namespace FeatureTests.Inheritance
{
    // abstract な基底がインタフェースを実装する。Hp は virtual、Kind・Receive は基底だけで実装
    // （[NetworkCallable] は U# の制限で virtual にできない）
    public abstract class InheritUnitBase : UdonSharpBehaviour, IInheritUnit
    {
        public int hp = 10;

        public virtual int Hp() { return hp; }

        public string Kind() { return "base"; }

        [NetworkCallable]
        public void Receive(int value) { hp += value; }
    }
}
