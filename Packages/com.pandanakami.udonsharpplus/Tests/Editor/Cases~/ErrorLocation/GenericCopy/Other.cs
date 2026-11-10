using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationGenericCopy
{
    // 同じクラスを別の UdonSharpBehaviour でも使う
    public class Other : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Store<int>().Check("e"));
        }
    }
}
