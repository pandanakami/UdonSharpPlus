using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.BehaviourInheritance
{
    // override する派生クラス
    public class Knight : UnitBase
    {
        public override int Hp() { return hp * 2; }
    }
}
