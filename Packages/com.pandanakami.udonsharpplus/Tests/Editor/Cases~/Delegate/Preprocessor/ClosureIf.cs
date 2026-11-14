using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.DelegatePreprocessor
{
    public delegate int Step(int x);

    // #129 D2: 環境を作る文を足すブロック・メソッドの { の直後が #if
    public class ClosureIf : UdonSharpBehaviour
    {
        private Step Make(int k)
        {
#if UNITY_EDITOR
            Debug.Log("editor");
#endif
            return x => x + k;
        }

        private void Start()
        {
#region locals
            int a = 1;
#endregion
            Step s = x => x * a;
            foreach (int w in new int[] { 2 })
#if UNITY_EDITOR
                s = x => x + w;
#else
                s = x => x - w;
#endif
            Debug.Log(s(1) + Make(2)(3));
        }
    }
}
