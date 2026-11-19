using System.Linq;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// GroupBy のグループの入れ物の使い回し（Redmine #171）。グループがつなぎの外に出ないと言えるときだけ、
    /// 呼び出し箇所ごとのフィールド（…_gp0）に置いて使い回す。外に出る形を使い回すと、前に返したグループの中身が次の呼び出しで書き換わる
    /// </summary>
    public class GroupReuseTests
    {
        private const string Head = @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
public class GroupReuse : UdonSharpBehaviour
{
    private List<int> xs = new List<int> { 1, 2, 3, 4 };
    private IGrouping<int, int> _kept;
    private object _any;
    private static string Describe(IGrouping<int, int> g) => g.Key + "":"" + g.Count();
    private static bool Big(IGrouping<int, int> g) => g.Count() > 1;
    void Start() { BODY }
}
public class GroupReusePlain
{
    public int Run(List<int> xs) => xs.GroupBy(x => x % 2).Count(g => g.Count() > 1);
}
";

        private static bool Reuses(string body)
        {
            CompileResult r = UdonSharpTestCompiler.Compile(new TestSource("GroupReuse.cs", Head.Replace("BODY", body)));
            Assert.That(r.Diagnostics.Where(d => d.StartsWith("Error")), Is.Empty, string.Join("\n", r.Diagnostics));
            return r["GroupReuse"].Uasm.Contains("_gp0");
        }

        [TestCase("Debug.Log(string.Join(\",\", xs.GroupBy(x => x % 2).Select(g => g.Key + \":\" + string.Join(\"\", g))));", TestName = "Select with string.Join of the group")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Count());", TestName = "Count of groups")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Count(g => g.Count() > 1));", TestName = "Count with a lambda on the group")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 3).Where(g => g.Sum() > 2).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList().Count);", TestName = "Where OrderBy ThenBy then Select")]
        [TestCase("var r = xs.GroupBy(x => x % 2).Select(g => new { g.Key, N = g.Count(), L = g.ToList() }).ToList(); Debug.Log(r[0].N + r[0].L.Count);", TestName = "Select to an anonymous type with ToList of the group")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Sum(g => g.Max()));", TestName = "Sum over groups")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2, x => x * 2).Select(g => (g.Key, g.First())).ToList().Count);", TestName = "element selector and Select to a tuple")]
        public void ReusesWhenGroupsStayInside(string body)
        {
            Assert.That(Reuses(body), Is.True);
        }

        [TestCase("var gs = xs.GroupBy(x => x % 2).ToList(); Debug.Log(gs.Count);", TestName = "ToList of groups")]
        [TestCase("var g = xs.GroupBy(x => x % 2).First(); Debug.Log(g.Key);", TestName = "First group")]
        [TestCase("foreach (var g in xs.GroupBy(x => x % 2)) Debug.Log(g.Key);", TestName = "foreach over groups")]
        [TestCase("var gs = xs.GroupBy(x => x % 2).Where(g => g.Key > 0).ToList(); Debug.Log(gs.Count);", TestName = "ends with groups after Where")]
        [TestCase("var gs = xs.GroupBy(x => x % 2).Select(g => g).ToList(); Debug.Log(gs.Count);", TestName = "Select returns the group")]
        [TestCase("Debug.Log(string.Join(\",\", xs.GroupBy(x => x % 2).Select(g => Describe(g))));", TestName = "group passed to a method")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Where(Big).Count());", TestName = "method group")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Select(g => { _kept = g; return 1; }).Sum());", TestName = "group stored in a field")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Select(g => { _any = g; return 1; }).Sum());", TestName = "group stored as object")]
        [TestCase("Debug.Log(xs.GroupBy(x => x % 2).Distinct().Count());", TestName = "Distinct on groups")]
        [TestCase("Debug.Log(new GroupReusePlain().Run(xs));", TestName = "inside a plain class method")]
        public void DoesNotReuseWhenGroupsMayEscape(string body)
        {
            Assert.That(Reuses(body), Is.False);
        }
    }
}
