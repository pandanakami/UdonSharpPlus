using UdonSharp;

namespace CustomUdonSharpTests.Basic
{
    public class Enemy : UdonSharpBehaviour, IDamageable
    {
        public int hp = 100;

        // インタフェースと違う引数名で実装する（引数は位置で対応づく）
        public int Damage(int dmg, string source)
        {
            hp -= dmg;
            return hp;
        }

        public void ResetState()
        {
            hp = 100;
        }
    }
}
