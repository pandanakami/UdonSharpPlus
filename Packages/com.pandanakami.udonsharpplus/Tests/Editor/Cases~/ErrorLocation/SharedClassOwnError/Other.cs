using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.ErrorLocationSharedClassOwnError
{
    // 同じクラスを別の UdonSharpBehaviour でも使う
    public class Other : UdonSharpBehaviour
    {
        private void Start()
        {
            Debug.Log(new Node().NextValue());
            Debug.Log(new Box<int>().Value());
        }
    }
}
