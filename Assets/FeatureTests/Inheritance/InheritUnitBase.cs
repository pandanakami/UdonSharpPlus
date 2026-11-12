using System.Collections.Generic;
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

        // #111: 基底と派生（InheritArcher.DerivedHelpers）の両方で、インタフェースの呼び出し・普通のクラス・ラムダを使う
        // （生成するメソッドの名前が重なると、U# の「hiding base methods」のエラーになっていた）
        public int BaseHelpers()
        {
            IInheritUnit self = this;
            var counter = new InheritCounter();
            counter.Add(hp);
            var list = new List<int> { 1, 2, 3 };
            return self.Hp() + counter.total + list.FindIndex(x => x > 1);
        }
    }

    public class InheritCounter
    {
        public int total;
        public void Add(int v) { total += v; }
    }
}
