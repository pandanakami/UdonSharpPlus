using UdonSharp;
using UnityEngine;
using VRC.Udon.Common.Interfaces;

namespace FeatureTests.Interface
{
    public class Attacker : UdonSharpBehaviour
    {
        // インスペクターで割り当てる (Enemy, Barrel, Rock)
        public UdonSharpBehaviour[] targets;

        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読む。
        // その場で決まる値だけを比べる（ネットワークイベントの受信は後から非同期に来るので、Enemy のログで手で確かめる）
        private const string Expected = "0:90;1:-10;2:NOT;as:True;loop:-50;nameof:Receive;";
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        private void Start()
        {
            string r = "";
            for (int i = 0; i < targets.Length; i++)
            {
                UdonSharpBehaviour target = targets[i];
                if (target is IDamageable)
                {
                    IDamageable damageable = (IDamageable)target;
                    int remain = Hit(damageable, 10 * (i + 1));
                    Debug.Log($"[FeatureTests] targets[{i}] '{target.name}' is IDamageable, remain={remain}");
                    r += i + ":" + remain + ";";
                }
                else
                {
                    Debug.Log($"[FeatureTests] targets[{i}] '{target.name}' is NOT IDamageable");
                    r += i + ":NOT;";
                }
            }

            IDamageable first = targets[0] as IDamageable;
            if (first != null)
                first.ResetState();

            IDamageable notDamageable = targets[2] as IDamageable;
            Debug.Log($"[FeatureTests] 'as' on Rock -> isNull={notDamageable == null}");
            r += "as:" + (notDamageable == null) + ";";

            IDamageable[] list = new IDamageable[] { (IDamageable)targets[1], (IDamageable)targets[1] };
            int total = 0;
            foreach (IDamageable d in list)
                total += d.Damage(5, "loop");
            Debug.Log($"[FeatureTests] array loop total={total}");
            r += "loop:" + total + ";";

            // #32: [NetworkCallable] な実装メソッド
            INetworkReceiver receiver = (INetworkReceiver)targets[0];
            receiver.Receive(1, "local-interface");
            ((UdonSharpBehaviour)receiver).SendCustomNetworkEvent(NetworkEventTarget.All, nameof(INetworkReceiver.Receive), 2, "network-interface-nameof");
            Enemy enemy = (Enemy)targets[0];
            enemy.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Enemy.Receive), 3, "network-concrete-nameof");
            enemy.SendCustomNetworkEvent(NetworkEventTarget.All, "Receive", 4, "network-concrete-literal");
            Debug.Log($"[FeatureTests] nameof(Enemy.Receive)={nameof(Enemy.Receive)}");
            r += "nameof:" + nameof(Enemy.Receive) + ";";
            resultName = "#31 interface (Attacker)"; resultOk = r == Expected; resultText = r; resultReady = true;

            Debug.Log("[FeatureTests] DONE");
        }

        private int Hit(IDamageable damageable, int amount)
        {
            return damageable.Damage(amount, name);
        }
    }
}
