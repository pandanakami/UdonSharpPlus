using UdonSharp;
using UnityEngine;

#region Types
namespace UdonSharpPlusTests.DelegatePreprocessor
{
    // #126 A11: クラス全体を囲む #region と、普通のクラスの中の入れ子のデリゲート
    public class Counter
    {
        public delegate int Step(int x);
        public Step step;
        public int Next(int x) => step(x);
    }

    public class Region : UdonSharpBehaviour
    {
        private static int Inc(int x) => x + 1;

        private void Start()
        {
            var c = new Counter();
            c.step = Inc;
            Debug.Log(c.Next(1));
        }
#pragma warning disable 0414
    }
}
#endregion
