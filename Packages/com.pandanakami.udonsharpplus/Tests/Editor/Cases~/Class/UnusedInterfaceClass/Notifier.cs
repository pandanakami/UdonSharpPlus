namespace UdonSharpPlusTests.UnusedInterfaceClass
{
    public interface IListener
    {
        void Notify(int value);
    }

    // UdonSharpBehaviour から使われないクラス：インタフェースの未対応の書き方（?.）があってもエラーにしない（検査しない）
    public class Notifier : IListener
    {
        public IListener listener;

        public void Notify(int value) { }

        public void Send(int value)
        {
            listener?.Notify(value);
        }
    }
}
