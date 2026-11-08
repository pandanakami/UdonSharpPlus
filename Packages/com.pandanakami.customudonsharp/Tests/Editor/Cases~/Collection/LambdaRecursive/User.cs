using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.CollectionLambdaRecursive
{
    public class User : UdonSharpBehaviour
    {
        private List<int> _list = new List<int>();

        [RecursiveMethod]
        private int Recursive(int depth)
        {
            int sum = 0;
            _list.ForEach(x => sum += x);                               // U# の制約：[RecursiveMethod] の中では ref 引数のメソッドを呼べない
            return depth > 0 ? Recursive(depth - 1) + sum : sum;
        }

        [RecursiveMethod]
        private int ReadOnly(int depth)
        {
            int limit = 3;
            return _list.FindIndex(x => x > limit + depth);           // 読むだけなら使える
        }
    }
}
