using UdonSharp;

namespace CustomUdonSharpTests.RefOutParameter
{
    public class Caller : UdonSharpBehaviour
    {
        public IReader reader;

        private void Start()
        {
            // ref/out 引数のインタフェース呼び出しは未対応（エラーになる）
            reader.Read(out int value);
        }
    }
}
