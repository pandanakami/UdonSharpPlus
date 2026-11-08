using UdonSharp;

namespace CustomUdonSharpTests.GenericMethod
{
    public class Caller : UdonSharpBehaviour
    {
        public IStore store;

        private void Start()
        {
            // ジェネリックメソッドのインタフェース呼び出しは未対応（エラーになる）
            store.Put(1);
        }
    }
}
