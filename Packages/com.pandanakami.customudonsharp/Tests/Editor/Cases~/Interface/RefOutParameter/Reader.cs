using UdonSharp;

namespace CustomUdonSharpTests.RefOutParameter
{
    public class Reader : UdonSharpBehaviour, IReader
    {
        public void Read(out int value)
        {
            value = 1;
        }
    }
}
