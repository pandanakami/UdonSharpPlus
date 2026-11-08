using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.ClassGeneric
{
    // 基底の UdonSharpBehaviour のジェネリックメソッドを、継承したクラスから呼ぶ
    public abstract class GenericBase : UdonSharpBehaviour
    {
        protected T Pick<T>(T[] array, int i) => array[i];
    }

    public class Derived : GenericBase
    {
        private void Start()
        {
            int[] numbers = { 1, 2 };
            Debug.Log(Pick(numbers, 1) + this.Pick(new[] { "a" }, 0));
        }
    }
}
