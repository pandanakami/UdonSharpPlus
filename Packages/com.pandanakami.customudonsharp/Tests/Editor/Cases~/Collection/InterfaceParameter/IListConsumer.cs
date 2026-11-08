using System.Collections.Generic;

namespace CustomUdonSharpTests.CollectionInterface
{
    // インタフェースのメソッドの引数・戻り値が List / Dictionary（Udon 上の名前が実装側と呼び出し側で揃うこと）
    public interface IListConsumer
    {
        int Consume(List<int> values, Dictionary<string, int> weights);
        List<string> Names();
    }
}
