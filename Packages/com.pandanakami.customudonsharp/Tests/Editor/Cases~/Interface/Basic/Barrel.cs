using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.Basic
{
    public class Barrel : UdonSharpBehaviour, IDamageable
    {
        public int durability = 30;

        // 同名の引数とオーバーロードを先に宣言し、従来の命名なら連番がずれる状況を作る
        public void Paint(Color color, int amount)
        {
        }

        // インタフェースの実装ではないオーバーロード（従来どおり __0_Damage のまま）
        public void Damage(float amount)
        {
            durability -= (int)amount;
        }

        public int Damage(int amount, string attacker)
        {
            durability -= amount * 2;
            return durability;
        }

        public void ResetState()
        {
            durability = 30;
        }
    }
}
