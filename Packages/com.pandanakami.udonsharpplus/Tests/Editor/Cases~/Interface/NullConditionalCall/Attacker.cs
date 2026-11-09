using UdonSharp;

namespace UdonSharpPlusTests.NullConditionalCall
{
    public class Attacker : UdonSharpBehaviour
    {
        public IDamageable target;

        private void Start()
        {
            // ?. でのインタフェース呼び出しは未対応（エラーになる）
            target?.Damage(1);
        }
    }
}
