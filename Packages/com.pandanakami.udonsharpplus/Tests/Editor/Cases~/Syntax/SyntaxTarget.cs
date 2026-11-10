using UdonSharp;

namespace UdonSharpPlusTests.Syntax
{
    // #79 n: 派生のインタフェースを実装する UdonSharpBehaviour。状態を変えない
    public class SyntaxTarget : UdonSharpBehaviour, IDerived
    {
        public int A(int x) { return x + 100; }

        public int B(int x) { return x + 200; }
    }
}
