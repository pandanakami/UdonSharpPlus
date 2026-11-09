using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.InterfaceMoreErrors
{
    public class User : UdonSharpBehaviour
    {
        public IReader reader;

        private void Start()
        {
            reader.ReadIn(1);
            reader.ReadParams(1, 2);
            IHittable h = GetComponent<IHittable>();
            Debug.Log(h);
        }
    }
}
