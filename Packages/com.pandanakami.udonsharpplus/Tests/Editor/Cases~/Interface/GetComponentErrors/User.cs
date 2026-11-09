using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.GetComponentErrors
{
    // #76: インタフェース・普通のクラスを GetComponent 系で取得するとエラー（実行時に例外で止まるため）
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            IHittable a = GetComponent<IHittable>();
            IHittable[] b = GetComponents<IHittable>();
            IHittable c = GetComponentInChildren<IHittable>();
            IHittable[] d = GetComponentsInChildren<IHittable>();
            IHittable e = GetComponentInParent<IHittable>();
            IHittable[] f = GetComponentsInParent<IHittable>();
            IHittable g = gameObject.GetComponent<IHittable>();
            Bullet h = GetComponent<Bullet>();

            // エラーにしない：UdonSharpBehaviour、インタフェースを実装した UdonSharpBehaviour
            // （本パッケージのエラーが出ないことだけを見る。実行は GetComponentBehaviour と FeatureTests/Interface/GetComponentProbe）
            UdonSharpBehaviour ok1 = GetComponent<UdonSharpBehaviour>();
            Target ok2 = GetComponent<Target>();
            Target[] ok3 = GetComponents<Target>();
            Target ok4 = gameObject.GetComponent<Target>();
            Debug.Log(a + " " + b + c + d + e + f + g + h + ok1 + ok2 + ok3 + ok4);
        }
    }
}
