using UdonSharp;
using UnityEngine;

namespace InterfacePoC
{
    public class Enemy : UdonSharpBehaviour, IDamageable
    {
        public int hp = 100;

        // インタフェースと違う引数名で実装（位置ベースの命名で揃うことの確認）
        public int Damage(int dmg, string source)
        {
            hp -= dmg;
            Debug.Log($"[PoC] Enemy '{name}' took {dmg} damage from '{source}', hp={hp}");
            return hp;
        }

        public void ResetState()
        {
            hp = 100;
            Debug.Log($"[PoC] Enemy '{name}' ResetState -> hp={hp}");
        }
    }
}
