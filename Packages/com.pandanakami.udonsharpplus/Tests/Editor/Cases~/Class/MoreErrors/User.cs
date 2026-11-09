using System.Collections.Generic;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ClassMoreErrors
{
    public class User : UdonSharpBehaviour
    {
        [SerializeField] private Bullet _serialized;
        [UdonSynced] private Bullet _synced;
        private List<int> _armors = new List<int> { 1, 2 };

        private void Start()
        {
            Shape s = null;
            var inner = new Outer.Inner();
            Vec v = new Vec();
            Grid g = new Grid();
            Button button = new Button();
            Named n = new Named();
            Debug.Log(s.size + inner.v + v.x + g[0] + n.name + button);

            var b = new Bullet { damage = 1 };                // オブジェクト初期化子
            int found = _armors.Find(b.IsHit);                // インスタンスのメソッドを、メソッド名だけで渡す
            object o = b;
            if (o is Bullet typed)                            // 型パターン
                Debug.Log(typed.damage);
            Debug.Log(typeof(Bullet));                        // typeof
            Debug.Log(found);
        }
    }
}
