using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.InterfaceFieldSerialization
{
    public interface ITarget
    {
        int Hit(int v);
    }

    // UdonSharp と関係ないクラス：エラーにしない
    public class PlainBehaviour : MonoBehaviour
    {
        [SerializeField] private ITarget target;
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class User : UdonSharpBehaviour
    {
        public ITarget publicTarget;                              // public はエラーにしない（コードで代入して使う）
        [SerializeField] private ITarget serializedTarget;        // エラー
        [UdonSynced] private ITarget syncedTarget;                // エラー
        [SerializeField] private ITarget[] serializedTargets;     // 配列もエラー
        [SerializeField] private ITarget first, second;           // 変数ごとにエラー
        public ITarget[] publicTargets;
        private ITarget privateTarget;
        [SerializeField] private UdonSharpBehaviour behaviour;    // UdonSharpBehaviour 型で受けるのはよい

        private void Start()
        {
            privateTarget = (ITarget)behaviour;
            Debug.Log(privateTarget != null);
        }
    }
}
