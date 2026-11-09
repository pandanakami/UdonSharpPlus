using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;

namespace UdonSharpPlusTests.NetworkCallableReturnValue
{
    public class Enemy : UdonSharpBehaviour, IDamageable
    {
        // U# の制約：[NetworkCallable] は戻り値を持てない（インタフェース実装でも同じ）
        [NetworkCallable]
        public int Damage(int amount)
        {
            return amount;
        }
    }
}
