using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ExplicitAndPartialErrors
{
    // #98: ジェネリッククラスの partial は「partial は未対応」だけ（「ジェネリッククラスは未対応」を重ねない）
    public partial class Box<T>
    {
        public T value;
    }

    public partial class Box<T>
    {
        public T Get() { return value; }
    }

    // #98: 明示的インタフェース実装の名前は「クラス名.メンバー名」（名前空間を入れない）
    public interface IScore
    {
        int Score { get; }
        int Calc();
    }

    public class Item : IScore
    {
        public int v;
        int IScore.Score => v;
        int IScore.Calc() { return v; }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var box = new Box<int>();
            var item = new Item();
            Debug.Log(box.Get() + item.v);
        }
    }
}
