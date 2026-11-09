namespace FeatureTests.Class
{
    // #53: 汎用クラスのメソッドの中で Udon の実行時例外を起こす（配列の範囲外）
    public class Faulty
    {
        public int[] values = new int[2];

        public int Get(int index)
        {
            int doubled = index * 2;
            return values[doubled];   // ← ここで例外（11 行目）
        }
    }
}
