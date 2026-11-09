namespace FeatureTests.Interface
{
    // [NetworkCallable] な実装メソッドを持つインタフェース（#32 の確認用）
    public interface INetworkReceiver
    {
        void Receive(int value, string from);
    }
}
