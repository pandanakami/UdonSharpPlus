using UdonSharp;

namespace UdonSharpPlusTests.ClassErrors
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Derived d = new Derived();
            Counter c = new Counter();
            int x = c.value = 3;          // 代入の値は使えない（エラー）
            GetCounter().value += 1;      // 受け側が呼び出し（エラー）
        }

        private Counter GetCounter()
        {
            return new Counter();
        }
    }
}
