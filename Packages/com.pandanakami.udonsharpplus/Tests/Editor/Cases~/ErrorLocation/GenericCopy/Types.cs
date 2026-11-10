using System.Collections.Generic;
using System.Linq;

namespace UdonSharpPlusTests.ErrorLocationGenericCopy
{
    // #79: ジェネリクスのコピーの中のエラーが、元のソースの位置に出る。コピーが 2 つ、使う UdonSharpBehaviour が 2 つあっても、エラーは 1 つ
    public class Store<T>
    {
        public T value;

        public bool Check(string p) { return System.IO.File.Exists(p); }
    }
}
