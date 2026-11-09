using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionInterface
{
    public class Caller : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;

        private void Start()
        {
            var consumer = (IListConsumer)target;
            Debug.Log(consumer.Consume(new List<int> { 1, 2 }, new Dictionary<string, int> { { "w", 3 } }));
            Debug.Log(consumer.Names()[0]);
        }
    }
}
