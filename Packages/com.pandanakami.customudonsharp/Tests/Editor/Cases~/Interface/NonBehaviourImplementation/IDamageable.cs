namespace CustomUdonSharpTests.NonBehaviourImplementation
{
    public interface IDamageable
    {
        void Damage(int amount);
    }

    // UdonSharpBehaviour 以外でのインタフェース実装は未対応（エラーになる）
    public class Plain : IDamageable
    {
        public void Damage(int amount)
        {
        }
    }
}
