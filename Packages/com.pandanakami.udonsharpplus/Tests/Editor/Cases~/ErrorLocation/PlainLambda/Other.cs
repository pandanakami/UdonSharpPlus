using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationPlainLambda
{
    // 同じクラスを別の UdonSharpBehaviour でも使う
    public class Other : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Finder().Find());
        }
    }
}
