using UdonSharp;
using UnityEngine;
using VRC.Udon.Common.Interfaces;

namespace UdonSharpPlusTests.BehaviourInheritance
{
    public class User : UdonSharpBehaviour
    {
        public UdonSharpBehaviour[] targets;
        public UnitBase baseTyped;

        private void Start()
        {
            int total = 0;
            foreach (UdonSharpBehaviour t in targets)
            {
                // 派生クラスのインスタンスを is / as で判定する
                if (t is IUnit)
                {
                    IUnit u = (IUnit)t;
                    total += u.Hp();          // Knight は override の方、Archer は基底の方
                    Debug.Log(u.Kind());      // 基底だけで実装
                    u.Receive(1);             // 基底だけで実装した [NetworkCallable]
                }
                IUnit a = t as IUnit;
                if (a != null)
                    total += a.Hp();
            }

            // 基底の型で受けたものを、インタフェースにして呼ぶ
            IUnit fromBase = baseTyped;
            total += fromBase.Hp();

            // ネットワーク越し（インタフェースの nameof）
            ((UdonSharpBehaviour)fromBase).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(IUnit.Receive), 5);

            Debug.Log(total);
        }
    }
}
