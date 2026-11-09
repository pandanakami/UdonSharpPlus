using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassGenericErrors
{
    public class User : UdonSharpBehaviour
    {
        public Other other;

        private void Start()
        {
            int a = other.Echo(1);
            var box = new Box<int>();
            Debug.Log(box.Same(1, 2) + " " + box.Value() + Deep.Nest(1, 3));
            Debug.Log(typeof(Box<>));
        }
    }
}
