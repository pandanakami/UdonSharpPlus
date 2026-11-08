using UdonSharp;
using UnityEngine;

namespace InterfacePoC
{
    public class Barrel : UdonSharpBehaviour, IDamageable
    {
        public int durability = 30;

        // 同名引数 amount を持つメソッドを先に宣言し、U# の通常の命名なら
        // __1_amount__param になってしまう状況を作る（引数名は位置で決める固定名で揃う）
        public void Paint(Color color, int amount)
        {
            Debug.Log($"[PoC] Barrel Paint {color} {amount}");
        }

        // インタフェース実装メソッド（Damage）と同じ名前のメソッドは置けない（#51）ので別名にする
        public void Chip(float amount)
        {
            Debug.Log($"[PoC] Barrel Chip(float) {amount}");
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
