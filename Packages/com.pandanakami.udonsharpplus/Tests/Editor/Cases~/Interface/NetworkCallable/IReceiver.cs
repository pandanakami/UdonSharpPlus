namespace UdonSharpPlusTests.NetworkCallable
{
    public interface IReceiver
    {
        void Receive(int value, string from);
    }
}
