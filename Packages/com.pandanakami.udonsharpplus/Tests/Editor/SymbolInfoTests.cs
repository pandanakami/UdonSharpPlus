using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// SemanticModels.SymbolInfoOf が、すべての式・名前で GetSymbolInfo と同じ結果を返すこと（Redmine #165）。
    /// 呼び出しの名前の部分は、呼び出し全体への問い合わせに振り替えるので、振り替えてはいけないもの
    /// （デリゲートの呼び出し・決まらない呼び出し・nameof）も並べる
    /// </summary>
    public class SymbolInfoTests
    {
        private const string Source = @"
using System;
using System.Collections.Generic;
using System.Linq;
public delegate int Calc(int x);
public static class Ext { public static int Twice(this int x) => x * 2; }
public class Holder { public Func<int, int> F; public Calc C { get; set; } public int M(int x) => x; public static int S(int x) => x; }
public class Probe
{
    private Func<int> field = () => 1;
    private event Action Changed;
    private int Over(int x) => x;
    private int Over(string s) => 0;
    private T Gen<T>(T x) => x;
    public int Run(List<int> xs, Holder h, Calc calc, Holder maybe)
    {
        int Local(int v) => v + 1;
        int a = xs.Sum() + xs.Where(x => x > 0).Select(x => x * 2).Sum(x => x);
        int b = h.M(1) + Holder.S(2) + h.F(3) + h.C(4) + calc(5) + calc.Invoke(6) + field() + Local(7);
        int c = 3.Twice() + Ext.Twice(4) + Gen(5) + Gen<int>(6) + this.Over(1) + Over(""s"");
        int? d = maybe?.M(1);
        int? e = maybe?.F(2);
        string n = nameof(Run) + nameof(h.M) + nameof(xs.Count);
        Func<int, int> g = h.M;
        int bad = Over(1.5) + Missing(1) + h.Nope(2);
        Changed?.Invoke();
        new List<int>().ForEach(Console.WriteLine);
        base.ToString();
        return a + b + c + d.GetValueOrDefault() + e.GetValueOrDefault() + n.Length + g(1) + bad;
    }
}";

        [Test]
        public void SameAsGetSymbolInfoForEveryNode()
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(Source);
            var compilation = CSharpCompilation.Create("SymbolInfoProbe", new[] { tree },
                (IEnumerable<MetadataReference>)UdonSharpInternals.GetMetadataReferences.Invoke(null, null));
            int names = 0;
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (!(node is ExpressionSyntax))
                    continue;
                // 意味モデルを分ける（同じ意味モデルだと、先の問い合わせの結果を覚えていて違いが隠れるおそれがある）
                SymbolInfo expected = compilation.GetSemanticModel(tree).GetSymbolInfo(node);
                SymbolInfo actual = compilation.GetSemanticModel(tree).SymbolInfoOf(node);
                string at = node + " (" + node.Kind() + ")";
                Assert.That(SymbolEqualityComparer.Default.Equals(actual.Symbol, expected.Symbol), Is.True, at + ": " + actual.Symbol + " / " + expected.Symbol);
                Assert.That(actual.CandidateReason, Is.EqualTo(expected.CandidateReason), at);
                Assert.That(actual.CandidateSymbols.Select(s => s.ToDisplayString()), Is.EqualTo(expected.CandidateSymbols.Select(s => s.ToDisplayString())), at);
                if (node.Parent is InvocationExpressionSyntax || node.Parent is MemberAccessExpressionSyntax || node.Parent is MemberBindingExpressionSyntax)
                    names++;
            }
            Assert.That(names, Is.GreaterThan(40));
        }
    }
}
