using UdonSharp;
using UnityEngine;

namespace FeatureTests.Interface
{
    public class Barrel : UdonSharpBehaviour, IDamageable
    {
        public int durability = 30;

        // 同名引数 amount を持つメソッドを先に宣言し、U# の通常の命名なら
        // __1_amount__param になってしまう状況を作る（引数名は位置で決める固定名で揃う）
        public void Paint(Color color, int amount)
        {
            Debug.Log($"[FeatureTests] Barrel Paint {color} {amount}");
        }

        // インタフェース実装メソッド（Damage）と同じ名前のメソッドは置けない（#51）ので別名にする
        public void Chip(float amount)
        {
            Debug.Log($"[FeatureTests] Barrel Chip(float) {amount}");
        }

        public int Damage(int amount, string attacker)
        {
            durability -= amount * 2;
            Debug.Log($"[FeatureTests] Barrel '{name}' took {amount}x2 damage from '{attacker}', durability={durability}");
            if (durability <= 0)
                Debug.Log($"[FeatureTests] Barrel '{name}' exploded!");
            return durability;
        }

        public void ResetState()
        {
            durability = 30;
            Debug.Log($"[FeatureTests] Barrel '{name}' ResetState");
        }
    }
}
