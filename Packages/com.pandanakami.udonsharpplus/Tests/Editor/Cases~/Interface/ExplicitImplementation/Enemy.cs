using UdonSharp;

namespace UdonSharpPlusTests.ExplicitImplementation
{
    public class Enemy : UdonSharpBehaviour, IDamageable
    {
        // 明示的インタフェース実装は未対応（エラーになる）
        int IDamageable.Damage(int amount, string attacker)
        {
            return amount;
        }
    }
}
