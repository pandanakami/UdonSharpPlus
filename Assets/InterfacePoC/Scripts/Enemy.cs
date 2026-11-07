using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace InterfacePoC
{
    public class Enemy : UdonSharpBehaviour, IDamageable, INetworkReceiver
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

        [NetworkCallable]
        public void Receive(int v, string sender)
        {
            Debug.Log($"[PoC] Enemy '{name}' Receive value={v} from='{sender}'");
        }
    }
}
