namespace CustomUdonSharpTests.OverloadErrors
{
    // インタフェース自体のオーバーロード（エラー）
    public interface IShooter
    {
        void Fire(int count);
        void Fire(int count, float speed);
    }

    public interface IHittable
    {
        void Hit(int damage);
    }

    public interface IHittableWithSource
    {
        void Hit(int damage, string source);
    }

    public interface IResettable
    {
        void Hit(int damage);   // IHittable と同じシグネチャ：1 つのメソッドで両方を実装できる（エラーにしない）
    }
}
