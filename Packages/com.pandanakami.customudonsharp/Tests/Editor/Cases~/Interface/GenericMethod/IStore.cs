namespace CustomUdonSharpTests.GenericMethod
{
    public interface IStore
    {
        void Put<T>(T value);
    }
}
