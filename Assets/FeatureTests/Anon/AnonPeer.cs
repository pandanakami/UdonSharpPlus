using UdonSharp;

namespace FeatureTests.Anon
{
    // #70：別の UdonSharpBehaviour でも、同じ並び・並びの違う匿名型を作る
    public class AnonPeer : UdonSharpBehaviour
    {
        public string Describe()
        {
            var p = new { Name = "a", Score = 3 };
            var q = new { Score = 3, Name = "a" };
            return p.ToString() + p.Equals(new { Name = "a", Score = 3 }) + q;
        }
    }
}
