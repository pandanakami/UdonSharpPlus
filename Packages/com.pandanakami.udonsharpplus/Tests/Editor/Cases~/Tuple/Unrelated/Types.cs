using UnityEngine;

namespace UdonSharpPlusTests.TupleUnrelated
{
    // 使わない普通のクラスと、UdonSharp と関係ないクラスのタプルはエラーにしない（そのままコンパイルが通る）
    public class Unused
    {
        public (int, int) Pair() { return (1, 2); }
        public int Sum() { var (a, b) = Pair(); return a + b; }
    }

    public class PlainBehaviour : MonoBehaviour
    {
        private (int, int) _pair = (1, 2);
        private void Start() { var (a, b) = _pair; Debug.Log(a + b); }
    }

    public class Used
    {
        public int value = 1;
    }
}
