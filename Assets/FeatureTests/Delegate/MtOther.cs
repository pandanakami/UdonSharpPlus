namespace FeatureTests.Delegate
{
    // #124 の追加テスト（組み合わせ 11）：MtBox と同じ名前のメソッドを持つ、別のファイルの普通のクラス
    public class MtOther
    {
        public int v;

        public MtOther(int v) { this.v = v; }

        public int Get(int x) => v * 10 + x;
    }
}
