using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassInterfaceSwitch
{
    public class Commander : UdonSharpBehaviour
    {
        public UdonSharpBehaviour golem;

        private void Start()
        {
            IUnit[] units = new IUnit[] { new Archer(), new Knight(), new Mage(), new Thief(), (IUnit)golem };
            foreach (IUnit u in units)
                u.Boost(1);
            Debug.Log(new Army().Total(units));

            ILabel[] labels = new ILabel[] { new Archer(), new Mage(), new Banner() };
            foreach (ILabel l in labels)
                Debug.Log(l.Label());
        }

        // static メソッドの中からも呼べる
        private static int PowerOf(IUnit u) { return u.Power(); }
    }
}
