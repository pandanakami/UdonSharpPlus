using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.GetComponentBehaviour
{
    // #76: UdonSharpBehaviour と、インタフェースを実装した UdonSharpBehaviour は GetComponent 系で取得できる。
    // インタフェースとして使うときは、受けてから is / キャストする。
    // #78: GetComponent<ユーザーの UdonSharpBehaviour>() は UdonSharp の GetComponentShim を使うので、それを書き換えないこと
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Target target = GetComponent<Target>();
            IHittable hittable = target;
            hittable.Hit(1);

            Target[] targets = GetComponents<Target>();
            Target child = GetComponentInChildren<Target>(true);
            Target parent = GetComponentInParent<Target>();
            Target viaGameObject = gameObject.GetComponent<Target>();

            UdonSharpBehaviour any = GetComponent<UdonSharpBehaviour>();
            if (any is IHittable)
                ((IHittable)any).Hit(2);

            foreach (UdonSharpBehaviour b in GetComponentsInChildren<UdonSharpBehaviour>())
            {
                IHittable h = b as IHittable;
                if (h != null)
                    h.Hit(3);
            }

            Transform[] transforms = GetComponentsInChildren<Transform>();
            Debug.Log(targets.Length + " " + child + parent + viaGameObject + transforms.Length);
        }
    }
}
