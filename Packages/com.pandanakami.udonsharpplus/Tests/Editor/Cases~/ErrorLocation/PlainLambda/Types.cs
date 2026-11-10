using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.ErrorLocationPlainLambda
{
    // #79: 普通のクラスの中のラムダのエラーが、元のソースの位置に出る。2 つの UdonSharpBehaviour で使っても、エラーは 1 つ
    public class Finder
    {
        public List<string> names = new List<string> { "a" };

        public string Find() { return names.Find(n => System.IO.File.Exists(n)); }
    }
}
