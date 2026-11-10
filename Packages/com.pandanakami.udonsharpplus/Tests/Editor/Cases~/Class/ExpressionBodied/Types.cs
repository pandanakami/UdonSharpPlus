namespace UdonSharpPlusTests.ExpressionBodied
{
    // #86: 値を返さない式形式のメンバーの本体の代入・インクリメントは、文として扱う（エラーにしない）
    // （ローカル関数は UdonSharp が対応していないので対象外）
    public class Box
    {
        public int v;
        private int _w;

        public Box(int v) => this.v = v;                     // コンストラクタ

        public int W { get => _w; set => _w = value; }      // set

        public void Reset() => v = 0;                        // 戻り値なしのメソッド

        public void Bump() => v++;                           // 戻り値なしのメソッド（インクリメント）
    }
}
