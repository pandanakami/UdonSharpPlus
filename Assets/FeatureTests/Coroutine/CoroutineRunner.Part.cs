using System.Collections;

namespace FeatureTests.Coroutines
{
    // #107：CoroutineRunner の partial の別のファイルに書いたコルーチン（Y の段）
    public partial class CoroutineRunner
    {
        private IEnumerator YPart(int n)
        {
            yield return null;
            Y("pt" + n);
        }
    }
}
