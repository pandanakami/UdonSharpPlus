using UdonSharp;
using UnityEngine;
using VRC.Udon.Common.Interfaces;

namespace InterfacePoC
{
    public class Attacker : UdonSharpBehaviour
    {
        // インスペクターで割り当てる (Enemy, Barrel, Rock)
        public UdonSharpBehaviour[] targets;

        private void Start()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                UdonSharpBehaviour target = targets[i];
                if (target is IDamageable)
                {
                    IDamageable damageable = (IDamageable)target;
                    int remain = Hit(damageable, 10 * (i + 1));
                    Debug.Log($"[PoC] targets[{i}] '{target.name}' is IDamageable, remain={remain}");
                }
                else
                {
                    Debug.Log($"[PoC] targets[{i}] '{target.name}' is NOT IDamageable");
                }
            }

            IDamageable first = targets[0] as IDamageable;
            if (first != null)
                first.ResetState();

            IDamageable notDamageable = targets[2] as IDamageable;
            Debug.Log($"[PoC] 'as' on Rock -> isNull={notDamageable == null}");

            IDamageable[] list = new IDamageable[] { (IDamageable)targets[1], (IDamageable)targets[1] };
            int total = 0;
            foreach (IDamageable d in list)
                total += d.Damage(5, "loop");
            Debug.Log($"[PoC] array loop total={total}");

            // #32: [NetworkCallable] な実装メソッド
            INetworkReceiver receiver = (INetworkReceiver)targets[0];
            receiver.Receive(1, "local-interface");
            ((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(INetworkReceiver.Receive), 2, "network-interface-nameof");
            Enemy enemy = (Enemy)targets[0];
            enemy.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Enemy.Receive), 3, "network-concrete-nameof");
            enemy.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 4, "network-concrete-literal");
            Debug.Log($"[PoC] nameof(Enemy.Receive)={nameof(Enemy.Receive)}");

            Debug.Log("[PoC] DONE");
        }

        private int Hit(IDamageable damageable, int amount)
        {
            return damageable.Damage(amount, name);
        }
    }
}
