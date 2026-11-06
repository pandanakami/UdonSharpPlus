using UdonSharp;
using UnityEngine;

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

            Debug.Log("[PoC] DONE");
        }

        private int Hit(IDamageable damageable, int amount)
        {
            return damageable.Damage(amount, name);
        }
    }
}
