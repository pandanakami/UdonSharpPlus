using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.Partial
{
    // #93: ジェネリックでない partial クラスは使える（宣言を分けたフィールド・メソッドが 1 つのクラスになる）
    public partial class Item
    {
        public int count = 2;
    }

    public partial class Item
    {
        public int price = 5;

        public int Total() { return count * price; }
    }

    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            var item = new Item();
            Debug.Log(item.Total());
        }
    }
}
