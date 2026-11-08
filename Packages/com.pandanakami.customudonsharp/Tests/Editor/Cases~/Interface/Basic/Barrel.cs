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

        // 同名の引数を持つ普通のメソッド（U# の通常の命名で __0_Chip のまま。#51 以降、Damage という名前は使えない）
        public void Chip(float amount)
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
