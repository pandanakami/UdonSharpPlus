using UdonSharp;
using UnityEngine;

namespace CustomUdonSharpTests.ClassInterface
{
    public class ShapeUser : UdonSharpBehaviour
    {
        public UdonSharpBehaviour triangle;

        private void Start()
        {
            IShape[] shapes = new IShape[3];
            shapes[0] = new Circle(1f);
            shapes[1] = new Square(2f);
            shapes[2] = (IShape)triangle;

            foreach (IShape s in shapes)
            {
                s.Grow(1f);
                Debug.Log(s.Describe("a ") + " " + s.Area());
            }

            Debug.Log(new Total().Sum(shapes));

            object o = shapes[0];
            if (o is IShape)
                Debug.Log((o as IShape).Describe("is "));
            if (o is Circle)
                Debug.Log(((Circle)o).r);
        }
    }
}
