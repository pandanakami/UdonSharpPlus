using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Basic
{
    public class Attacker : UdonSharpBehaviour
    {
        public UdonSharpBehaviour[] targets;

        private void Start()
        {
            int total = 0;

            // is とキャスト
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is IDamageable)
                    total += Hit((IDamageable)targets[i], 10);
            }

            // as
            IDamageable first = targets[0] as IDamageable;
            if (first != null)
                first.ResetState();

            // インタフェース型の配列と foreach
            IDamageable[] list = new IDamageable[] { (IDamageable)targets[0], (IDamageable)targets[1] };
            foreach (IDamageable d in list)
                total += d.Damage(5, "loop");

            Debug.Log(total);
        }

        // インタフェース型の引数
        private int Hit(IDamageable damageable, int amount)
        {
            return damageable.Damage(amount, name);
        }
    }
}
