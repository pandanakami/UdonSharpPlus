namespace FeatureTests.Anon
{
    // #70：partial の別のファイルで、並びの違う匿名型を作る（生成する番号・置く場所・再利用が安定する）
    public partial class AnonMoreRunner
    {
        private string PartMore()
        {
            var p = new { Score = 3, Name = "a" };
            var q = new { Name = "a", Score = 3 };
            return p + "," + q + "," + p.Equals(q) + "," + q.Equals(new { Name = "a", Score = 3 });
        }
    }
}
