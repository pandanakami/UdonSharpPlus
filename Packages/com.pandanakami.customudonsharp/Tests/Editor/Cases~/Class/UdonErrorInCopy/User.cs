using UdonSharp;

namespace CustomUdonSharpTests.UdonErrorInCopy
{
    public class User : UdonSharpBehaviour
    {
        private void Start()
        {
            Saver s = new Saver();
            bool e = s.Exists();
        }
    }
}
