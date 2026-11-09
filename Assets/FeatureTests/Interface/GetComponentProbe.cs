using UdonSharp;
using UnityEngine;

namespace FeatureTests.Interface
{
    // #76: インタフェースを実装した UdonSharpBehaviour（Enemy）と UdonSharpBehaviour は、GetComponent 系で取得できることを確かめる。
    // （インタフェース型・普通のクラスを型引数に渡すのはコンパイルエラー。実行時に例外で止まるため）
    // Enemy・GreetTarget と同じ GameObject に置く。Enemy の状態は変えない（ほかのテストが Enemy を使うため）
    public class GetComponentProbe : UdonSharpBehaviour
    {
        private void Start()
        {
            string r = "";
            bool ok = true;

            // インタフェースを実装した UdonSharpBehaviour を、具体的な型で取得する
            Enemy enemy = GetComponent<Enemy>();
            IDamageable asInterface = enemy;
            ok &= enemy != null && asInterface != null;
            r += "enemy:" + (enemy != null) + ";";

            Enemy[] enemies = GetComponents<Enemy>();
            ok &= enemies.Length == 1;
            r += "enemies:" + enemies.Length + ";";

            Enemy viaGameObject = gameObject.GetComponent<Enemy>();
            ok &= viaGameObject == enemy;
            r += "gameObject:" + (viaGameObject == enemy) + ";";

            // UdonSharpBehaviour で受けて、is / as でインタフェースにする
            int damageable = 0;
            foreach (UdonSharpBehaviour b in GetComponents<UdonSharpBehaviour>())
            {
                IDamageable d = b as IDamageable;
                if (d != null)
                    damageable++;
            }
            ok &= damageable == 1;
            r += "damageable:" + damageable + ";";

            // GetComponent で取ったあと、インタフェース経由でメソッドを呼ぶ（GreetTarget は状態を変えない）
            GreetTarget greetTarget = GetComponent<GreetTarget>();
            IGreeter greeter = greetTarget;
            string greeted = greeter.Greet("a");
            ok &= greeted == "hi a";
            r += "greet:" + greeted + ";";

            // UdonSharpBehaviour で受けて as したものから呼ぶ
            string greetedAll = "";
            foreach (UdonSharpBehaviour b in GetComponents<UdonSharpBehaviour>())
            {
                IGreeter g = b as IGreeter;
                if (g != null)
                    greetedAll += g.Greet("b");
            }
            ok &= greetedAll == "hi b";
            r += "greetAs:" + greetedAll + ";";

            Debug.Log("[FeatureTests] #76 GetComponent " + (ok ? "OK" : "NG") + " | " + r);
        }
    }
}
