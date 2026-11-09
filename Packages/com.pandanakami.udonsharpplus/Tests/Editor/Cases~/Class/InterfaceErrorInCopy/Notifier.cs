namespace UdonSharpPlusTests.InterfaceErrorInCopy
{
    public interface IListener
    {
        void Notify(int value);
    }

    public class Notifier
    {
        public IListener listener;

        public void Send(int value)
        {
            // インタフェースへの ?. は未対応（本パッケージのエラー。コピーの中で出る）
            listener?.Notify(value);
        }
    }
}
