using System.Collections.Generic;
using UdonSharp;

namespace CustomUdonSharpTests.CollectionInterface
{
    public class Consumer : UdonSharpBehaviour, IListConsumer
    {
        public int Consume(List<int> values, Dictionary<string, int> weights)
        {
            int s = 0;
            foreach (int v in values)
                s += v * weights["w"];
            return s;
        }

        public List<string> Names() { return new List<string> { "a" }; }
    }
}
