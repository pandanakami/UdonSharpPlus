using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.ErrorLocationPlainCopy
{
    // #79: 普通のクラスのコピーの中のエラーが、元のソースの位置に出る
    public class Saver
    {
        public string path = "a";

        public bool Exists() { return System.IO.File.Exists(path); }
    }
}
