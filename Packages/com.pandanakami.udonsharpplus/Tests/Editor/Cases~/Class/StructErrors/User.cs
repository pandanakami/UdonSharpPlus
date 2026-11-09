using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.StructErrors
{
    public class User : UdonSharpBehaviour
    {
        public Point field;
        private Point[] _points;

        private void Start()
        {
            Point p = new Point(1, 2);
            Debug.Log(p.Sum());
            Debug.Log(new Box());
        }
    }
}
