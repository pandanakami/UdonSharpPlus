using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.ErrorLocationPlainMultiLine
{
    // #79: 普通のクラスのコピーの中の、複数行にまたがる式の 2 行目のエラーが、元のソースの位置に出る
    public class Saver
    {
        public string path = "a";

        public bool Both(string other)
        {
            return path.Length > 0
                && System.IO.File.Exists(other);
        }
    }
}
