using UdonSharp;

namespace CollectionPoC
{
    // #59: 基底の UdonSharpBehaviour のジェネリックメソッドを、継承したクラス（GenericScenarioRunner）から呼ぶ
    public abstract class GenericScenarioBase : UdonSharpBehaviour
    {
        protected T Pick<T>(T[] array, int i) => array[i];

        protected int CountOf<T>(T[] array, T value)
        {
            int c = 0;
            foreach (T x in array)
                if (x.Equals(value))
                    c++;
            return c;
        }

        // 基底クラスの中からも呼ぶ
        protected string PickBoth(int[] numbers, string[] texts) => Pick(numbers, 0) + Pick(texts, 0);
    }
}
