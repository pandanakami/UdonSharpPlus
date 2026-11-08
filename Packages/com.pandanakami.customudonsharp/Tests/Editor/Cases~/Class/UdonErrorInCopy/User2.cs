using UdonSharp;

namespace CustomUdonSharpTests.UdonErrorInCopy
{
    // 同じクラスを別の UdonSharpBehaviour でも使う：コピーが 2 つになっても、エラーは 1 つにまとめる
    public class User2 : UdonSharpBehaviour
    {
        private void Start()
        {
            Saver s = new Saver();
            if (s.Exists())
                return;
        }
    }
}
