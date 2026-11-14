namespace FeatureTests.Anon
{
    // #70：partial の別のファイルで、同じ並びの匿名型を作る（同じクラスになる）
    public partial class AnonRunner
    {
        private string PartAnon()
        {
            var z = new { Name = "a", Score = 3 };
            return z.ToString() + "," + z.Equals(new { Name = "a", Score = 3 }) + "," + z.Score;
        }
    }
}
