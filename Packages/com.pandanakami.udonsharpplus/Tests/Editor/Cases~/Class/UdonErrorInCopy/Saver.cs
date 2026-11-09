namespace UdonSharpPlusTests.UdonErrorInCopy
{
    public class Saver
    {
        public string path = "a.txt";

        public bool Exists()
        {
            // Udon で使えない API（U# のエラーになる）
            return System.IO.File.Exists(path);
        }
    }
}
