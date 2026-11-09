using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Class
{
    // #50: 汎用クラスと UdonSharpBehaviour の両方が実装するインタフェースを Udon 上で使い、
    // 手計算の期待値と比べて OK/NG を表示する。Start で 1 回実行し、Interact でもう一度実行できる。
    public class ShapeScenarioRunner : UdonSharpBehaviour
    {
        private const string Expected =
            "a circle:12;a square:9;a triangle:7;sum:28;is:True,True,False,False,False;as:as circle;cross:9;init:12,7,27,9;";

        public ShapeTriangle triangle;
        public Text display;

        private void Start() { Run(); }

        public override void Interact() { Run(); }

        public void Run()
        {
            triangle.ResetShape();

            IShape[] shapes = new IShape[3];
            shapes[0] = new Circle(1);
            shapes[1] = new Square(2);
            shapes[2] = triangle;

            string actual = "";
            foreach (IShape s in shapes)
            {
                s.Grow(1);
                actual += s.Describe("a ") + ":" + s.Area() + ";";
            }

            actual += "sum:" + new ShapeTotal().Sum(shapes) + ";";

            object o0 = shapes[0];
            object o2 = triangle;
            object str = "x";
            object none = null;
            object sq = shapes[1];
            actual += "is:" + (o0 is IShape) + "," + (o2 is IShape) + "," + (str is IShape) + "," + (none is IShape) + "," + (sq is Circle) + ";";
            actual += "as:" + (o0 as IShape).Describe("as ") + ";";

            // 汎用クラスのインスタンスを別のプログラムに渡し、そちらのコピーで呼ぶ
            actual += "cross:" + triangle.MeasureOther(shapes[1]) + ";";

            // 配列の初期化子で汎用クラスと UdonSharpBehaviour を混ぜ、添字で呼ぶ
            IShape[] init = new IShape[] { new Circle(2), triangle };
            actual += "init:" + init[0].Area() + "," + init[1].Area() + ",";
            init[0].Grow(1);
            init[1].Grow(1);
            actual += init[0].Area() + "," + init[1].Area() + ";";

            bool ok = actual == Expected;
            Debug.Log("[FeatureTests] #50 " + (ok ? "OK " : "NG ") + actual);
            if (display != null)
                display.text = "#50 class implements interface\n" + (ok ? "RESULT: OK" : "RESULT: NG") + "\n\n"
                               + actual.Replace(";", "\n") + (ok ? "" : "\nexpected:\n" + Expected.Replace(";", "\n"));
        }
    }
}
