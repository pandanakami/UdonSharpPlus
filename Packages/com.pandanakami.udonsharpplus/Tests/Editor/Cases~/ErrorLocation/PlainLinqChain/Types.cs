using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.ErrorLocationPlainLinqChain
{
    // #79: 普通のクラスの中の、LINQ のメソッドチェーンの途中のエラーが、元のソースの位置に出る
    public class Finder
    {
        public List<string> names = new List<string> { "a" };

        public int Count()
        {
            return names
                .Where(n => n.Length > 0)
                .Where(n => System.IO.File.Exists(n))
                .Count();
        }
    }
}
