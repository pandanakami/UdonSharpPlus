using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.InterfaceMoreErrors
{
    public class Target : BaseTarget, IHittable
    {
        public void Hit(int damage) { }
    }
}
