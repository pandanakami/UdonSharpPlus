using UdonSharp;

namespace CustomUdonSharpTests.ClassGenericErrors
{
    public class Other : UdonSharpBehaviour
    {
        // 別のクラスから見えるもの（public・internal・protected internal）は警告（#64）
        public T Echo<T>(T value) => value;
        internal T EchoInternal<T>(T value) => value;
        protected internal T EchoProtectedInternal<T>(T value) => value;
        public static T EchoStatic<T>(T value) => value;

        // 同じクラスと継承したクラスの中からだけ呼べるものは警告なし
        private T EchoPrivate<T>(T value) => value;
        protected T EchoProtected<T>(T value) => value;
        private protected T EchoPrivateProtected<T>(T value) => value;
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
