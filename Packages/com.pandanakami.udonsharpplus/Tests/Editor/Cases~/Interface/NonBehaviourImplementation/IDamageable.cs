namespace UdonSharpPlusTests.NonBehaviourImplementation
{
    public interface IDamageable
    {
        void Damage(int amount);
    }

    // UdonSharpBehaviour と汎用クラス以外（MonoBehaviour など）でのインタフェース実装は未対応（エラーになる）
    public class Plain : UnityEngine.MonoBehaviour, IDamageable
    {
        public void Damage(int amount)
        {
        }
    }
}
