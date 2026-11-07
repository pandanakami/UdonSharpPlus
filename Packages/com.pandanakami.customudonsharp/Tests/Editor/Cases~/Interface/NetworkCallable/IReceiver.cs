namespace CustomUdonSharpTests.NetworkCallable
{
    public interface IReceiver
    {
        void Receive(int value, string from);
    }
}
