using UdonSharp;

namespace CustomUdonSharpTests.ClassGenericErrors
{
    public class Other : UdonSharpBehaviour
    {
        public T Echo<T>(T value) => value;
    }

    // ジェネリックな UdonSharpBehaviour・インタフェースはエラー
    public class Holder<T> : UdonSharpBehaviour
    {
    }

    public interface IBox<T>
    {
        T Get();
    }
}
