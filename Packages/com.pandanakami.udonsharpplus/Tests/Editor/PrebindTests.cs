using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 書き換えの段の前に、解析だけを並列で済ませる下準備（Prebind。Redmine #168）が、結果を変えないこと。
    /// 下準備は問い合わせるだけで、書き換えは順に行うので、書き換え後のソース（印の番号も）・エラーと警告（順番も）・UASM が、
    /// 下準備のないときと同じになるはず。並列のときだけ起きる取り合いがあれば、たまに違う結果になるので、同じソースを何回か書き換えて比べる
    /// </summary>
    public class PrebindTests
    {
        private string _dumpDir;

        [SetUp]
        public void SetUp()
        {
            // 本パッケージのエラー・警告ログは結果の中で比べるので、ログによるテスト失敗は止める
            LogAssert.ignoreFailingMessages = true;
            // ClearDump は名前に UdonSharpPlus を含むフォルダだけを消す
            _dumpDir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusPrebindDump"));
        }

        [TearDown]
        public void TearDown()
        {
            Prebind.Enabled = true;
            if (Directory.Exists(_dumpDir))
                Directory.Delete(_dumpDir, recursive: true);
        }

        // ---- ソース ---------------------------------------------------------

        /// <summary>どの段も通るように、演算子・匿名型・タプル・ジェネリクス・デリゲート・インタフェース・普通のクラス・List / LINQ を混ぜたもの</summary>
        private static readonly TestSource[] Mixed =
        {
            new TestSource("PbTypes.cs", @"using System.Collections.Generic;
public delegate int PbFn(int v);
public interface IPbShape { int Area(); }
public class PbVec
{
    public int x;
    public PbVec(int x) { this.x = x; }
    public static PbVec operator +(PbVec a, PbVec b) => new PbVec(a.x + b.x);
    public static implicit operator PbVec(int v) => new PbVec(v);
}
public class PbBox<T>
{
    public T Value;
    public PbBox(T value) { Value = value; }
    public T Get() => Value;
}
public class PbSquare : IPbShape
{
    private readonly int _side;
    public PbSquare(int side) { _side = side; }
    public int Area() => _side * _side;
}
public class PbBag
{
    public List<int> Items = new List<int>();
    public int Sum { get { int s = 0; foreach (int i in Items) s += i; return s; } }
}
"),
            new TestSource("PbUser.cs", @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
public class PbUser : UdonSharpBehaviour
{
    private PbBag _bag = new PbBag();
    private int _total = 0;

    private void Start()
    {
        PbVec a = 3;
        PbVec c = a + new PbVec(2);
        c += a;
        var items = new List<int> { 3, 1, 2 };
        var rows = items.Where(x => x > 1).Select(x => new { V = x, D = x * 2 }).OrderBy(r => r.V).ToList();
        var pair = (count: rows.Count, name: ""s"");
        PbFn twice = v => v * 2;
        var box = new PbBox<int>(twice(pair.count));
        IPbShape shape = new PbSquare(c.x);
        _bag.Items.Add(box.Get());
        Dictionary<string, int> map = new Dictionary<string, int>();
        map[pair.name] = shape.Area();
        _total = _bag.Sum + map[pair.name] + rows[0].D;
        Debug.Log(_total);
    }
}
"),
        };

        /// <summary>中身の解析が失敗するところ（名前がない・メンバーがない・型が合わない）を、ラムダ・LINQ・ジェネリクスの中にも置いたもの</summary>
        private static readonly TestSource[] Broken =
        {
            new TestSource("PbBroken.cs", @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
public class PbBrokenBox<T> { public T Value; public T Get() => Value.Nope; }
public class PbBroken : UdonSharpBehaviour
{
    private List<int> _xs = new List<int> { 1, 2 };
    private int Field = Missing;

    private void Start()
    {
        string s = _xs.Select(x => x.Nope).First();
        int n = MissingMethod(3);
        var t = (1, 2);
        int bad = t;
        var box = new PbBrokenBox<int>();
        System.Func<int, int> f = x => x + undefined;
        Debug.Log(s + n + bad + box.Get() + f(1));
    }

    private int Broken => _xs.Where(x => x.Length > 0).Count();
}
"),
        };

        private static TestSource[] GoldenSources(string caseName)
        {
            string dir = Path.Combine(GoldenCases.CasesRoot, caseName);
            return Directory.GetFiles(dir, "*.cs")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new TestSource(Path.GetFileName(p), File.ReadAllText(p)))
                .ToArray();
        }

        // ---- 比べるもの -----------------------------------------------------

        /// <summary>書き換え後のソース（ファイルごと）と、プログラム・エラーと警告・ログ</summary>
        private string Lower(TestSource[] sources, bool prebind)
        {
            Prebind.Enabled = prebind;
            try
            {
                CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dumpDir, sources);
                var sb = new StringBuilder();
                if (Directory.Exists(_dumpDir))
                {
                    foreach (string path in Directory.GetFiles(_dumpDir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                        sb.Append("## Lowered: ").Append(path.Substring(_dumpDir.Length)).Append('\n').Append(File.ReadAllText(path)).Append('\n');
                }
                sb.Append(GoldenCases.Render(result));
                return sb.ToString();
            }
            finally
            {
                Prebind.Enabled = true;
            }
        }

        private void AssertSameWithAndWithout(TestSource[] sources)
        {
            string without = Lower(sources, prebind: false);
            int runs = Prebind.Runs;
            string with = Lower(sources, prebind: true);
            if (Environment.ProcessorCount > 2)
                Assert.That(Prebind.Runs, Is.GreaterThan(runs), "下準備が動いていない");
            Assert.That(with, Is.EqualTo(without));
        }

        // ---- 1. 下準備のあり・なしで同じ -------------------------------------

        [Test]
        public void MixedFeaturesAreLoweredTheSame()
        {
            AssertSameWithAndWithout(Mixed);
            // 段をすべて通ったこと（エラーで途中で止まっていないこと）
            Assert.That(Lower(Mixed, prebind: true), Does.Not.Contain("\nError"));
        }

        [TestCase("Combination")]
        [TestCase("Syntax")]
        [TestCase("Class/Generic")]
        [TestCase("Collection/Linq")]
        [TestCase("Interface/Basic")]
        [TestCase("ErrorLocation/GenericCopy")]
        public void GoldenCaseIsLoweredTheSame(string caseName) => AssertSameWithAndWithout(GoldenSources(caseName));

        // ---- 3. エラーのあるソースでも止まらず、同じエラーになる -----------

        [Test]
        public void BrokenSourceGivesTheSameErrors()
        {
            AssertSameWithAndWithout(Broken);
            Assert.That(Lower(Broken, prebind: true), Does.Contain("\nError"));
        }

        [TestCase("Class/Errors")]
        [TestCase("Class/GenericErrors")]
        [TestCase("Collection/LinqErrors")]
        [TestCase("Collection/LambdaErrors")]
        [TestCase("Interface/MoreErrors")]
        [TestCase("Tuple/Errors")]
        public void GoldenErrorCaseGivesTheSameErrors(string caseName) => AssertSameWithAndWithout(GoldenSources(caseName));

        // ---- 2. 何回やっても同じ（見つかればもうけもの） --------------------

        [TestCase("Combination")]
        [TestCase("Syntax")]
        public void RepeatedLoweringIsStable(string caseName) => AssertStable(GoldenSources(caseName));

        [Test]
        public void RepeatedLoweringOfMixedFeaturesIsStable() => AssertStable(Mixed);

        [Test]
        public void RepeatedLoweringOfBrokenSourceIsStable() => AssertStable(Broken);

        private const int Repeats = 5;

        private void AssertStable(TestSource[] sources)
        {
            string first = Lower(sources, prebind: true);
            for (int i = 1; i < Repeats; i++)
                Assert.That(Lower(sources, prebind: true), Is.EqualTo(first), $"{i + 1} 回目が 1 回目と違う");
        }
    }
}
