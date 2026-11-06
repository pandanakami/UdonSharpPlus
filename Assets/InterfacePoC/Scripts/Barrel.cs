using UdonSharp;
using UnityEngine;

namespace InterfacePoC
{
    public class Barrel : UdonSharpBehaviour, IDamageable
    {
        public int durability = 30;

        // 同名引数 amount とオーバーロード Damage を先に宣言し、従来の命名なら
        // __1_amount__param / __1_Damage になってしまう状況を作る
        public void Paint(Color color, int amount)
        {
            Debug.Log($"[PoC] Barrel Paint {color} {amount}");
        }

        public void Damage(float amount)
        {
            Debug.Log($"[PoC] Barrel Damage(float) overload {amount}");
        }

        public int Damage(int amount, string attacker)
        {
            durability -= amount * 2;
            Debug.Log($"[PoC] Barrel '{name}' took {amount}x2 damage from '{attacker}', durability={durability}");
            if (durability <= 0)
                Debug.Log($"[PoC] Barrel '{name}' exploded!");
            return durability;
        }

        public void ResetState()
        {
            durability = 30;
            Debug.Log($"[PoC] Barrel '{name}' ResetState");
        }
    }
}
