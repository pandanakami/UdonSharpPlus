using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Anon
{
    // #71：partial の別のファイルで、同じ並び・別の並びの匿名型のリストを作る
    public partial class AnonListRunner
    {
        private string PartList(List<int> src)
        {
            var a = src.Select(x => new { A = x }).ToList();
            var b = src.Select(x => new { B = x, A = "s" }).ToList();
            return a.Count + "" + a.Where(r => r.A > 4).Count() + b.FindIndex(r => r.B == 3) + b[0].A;
        }
    }
}
