namespace UdonSharpPlusTests.GenericMethod
{
    public interface IStore
    {
        void Put<T>(T value);
    }
}
