using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 書き換えの段が問い合わせる前に、メンバーの中身の解析（バインド）を、ファイルごとに並列で済ませておく（Redmine #168）。
    ///
    /// 段ごとにコンパイルを作り直すので、段の時間の多くは、メンバーの中身の最初の解析になる。意味モデルは解析した結果を覚えておき、
    /// 2 回目からの問い合わせは速い。そこで段の書き換え（順に行う）の前に、段が見るメンバーの文・ラムダ・呼び出しの名前に 1 回ずつ
    /// 問い合わせて、解析だけを並列にさせる。段の書き換えは今までどおり順に行うので、結果・エラーの順番・生成する名前は変わらない。
    /// 意味モデルはスレッドセーフ（Roslyn の作り）。ここでは問い合わせるだけで、結果は使わない。
    ///
    /// Unity（Mono）の GC はすべてのスレッドを止めるので、解析はコア数ほどには速くならない（8 並列で 3 倍弱）
    /// </summary>
    internal static class Prebind
    {
        /// <summary>false なら何もしない（比べるとき用）</summary>
        internal static bool Enabled = true;

        /// <summary>並列に問い合わせた回数（テストで、下準備が動いたことを確かめる）</summary>
        internal static int Runs;

        private static readonly int Parallelism = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 8));

        // コンパイルごとの、解析を済ませたメンバー（前の段とコンパイルが同じなら、もう一度はしない）
        private static readonly ConditionalWeakTable<Compilation, HashSet<SyntaxNode>> Done = new ConditionalWeakTable<Compilation, HashSet<SyntaxNode>>();

        /// <param name="selected">見るファイル（null ならすべて）</param>
        /// <param name="needsMember">見るメンバー（null ならすべて）。順に呼ぶ</param>
        public static void Run(Compilation compilation, IReadOnlyList<SyntaxTree> trees, IReadOnlyList<bool> selected = null,
            Func<MemberDeclarationSyntax, SemanticModel, bool> needsMember = null)
        {
            if (!Enabled || Parallelism < 2)
                return;

            // 問い合わせるところを、順に集める（構文を見るだけ。needsMember も順に呼ぶ）
            HashSet<SyntaxNode> done = Done.GetOrCreateValue(compilation);
            var work = new List<(SemanticModel Model, List<ExpressionSyntax> Seeds, int Size)>();
            for (int i = 0; i < trees.Count; i++)
            {
                if (selected != null && !selected[i])
                    continue;
                SemanticModel model = compilation.Model(trees[i]);
                var seeds = new List<ExpressionSyntax>();
                foreach ((MemberDeclarationSyntax member, SyntaxNode body) in Bodies(trees[i].GetRoot()))
                {
                    if (needsMember != null && !needsMember(member, model) || !done.Add(body))
                        continue;
                    AddSeeds(body, seeds);
                }
                if (seeds.Count > 0)
                    work.Add((model, seeds, seeds.Count));
            }
            if (work.Count == 0)
                return;

            Runs++;
            try
            {
                // 大きいファイルから先に渡す（最後に大きいファイルが 1 つだけ残らないように）
                Parallel.ForEach(work.OrderByDescending(w => w.Size), new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, w =>
                {
                    foreach (ExpressionSyntax seed in w.Seeds)
                        w.Model.GetTypeInfo(seed);
                });
            }
            catch (AggregateException)
            {
                // 下準備なので、何かあってもそのまま進める（段の問い合わせが、改めて解析する）
            }
        }

        /// <summary>中身を持つところ（メソッド・アクセサー・=>・初期化子）と、それを持つメンバー</summary>
        private static IEnumerable<(MemberDeclarationSyntax Member, SyntaxNode Body)> Bodies(SyntaxNode root)
        {
            foreach (SyntaxNode node in root.DescendantNodes(n => !(n is BlockSyntax) && !(n is ExpressionSyntax)))
            {
                switch (node)
                {
                    case BaseMethodDeclarationSyntax method:
                        if (method is ConstructorDeclarationSyntax ctor && ctor.Initializer != null)
                            yield return (method, ctor.Initializer);
                        if (method.Body != null)
                            yield return (method, method.Body);
                        if (method.ExpressionBody != null)
                            yield return (method, method.ExpressionBody);
                        break;
                    case AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is MemberDeclarationSyntax owner:
                        if (accessor.Body != null)
                            yield return (owner, accessor.Body);
                        if (accessor.ExpressionBody != null)
                            yield return (owner, accessor.ExpressionBody);
                        break;
                    case PropertyDeclarationSyntax property:
                        if (property.ExpressionBody != null)
                            yield return (property, property.ExpressionBody);
                        if (property.Initializer != null)
                            yield return (property, property.Initializer);
                        break;
                    case IndexerDeclarationSyntax indexer when indexer.ExpressionBody != null:
                        yield return (indexer, indexer.ExpressionBody);
                        break;
                    case VariableDeclaratorSyntax variable when variable.Initializer != null && variable.Parent?.Parent is BaseFieldDeclarationSyntax field:
                        yield return (field, variable.Initializer);
                        break;
                }
            }
        }

        /// <summary>
        /// 解析をさせる式。意味モデルは文ごとに解析するので、文ごとに最初の式。式のラムダの本体と、呼び出しの名前の部分
        /// （xs.Select の Select）は、別に解析するので、それぞれにも
        /// </summary>
        private static void AddSeeds(SyntaxNode body, List<ExpressionSyntax> seeds)
        {
            ExpressionSyntax first = body.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault();
            if (first != null)
                seeds.Add(first);
            foreach (SyntaxNode node in body.DescendantNodes())
            {
                switch (node)
                {
                    case StatementSyntax statement:
                        ExpressionSyntax own = statement.DescendantNodes(n => n == statement || !(n is StatementSyntax)).OfType<ExpressionSyntax>().FirstOrDefault();
                        if (own != null)
                            seeds.Add(own);
                        break;
                    case AnonymousFunctionExpressionSyntax lambda when lambda.Body is ExpressionSyntax expression:
                        seeds.Add(expression);
                        break;
                    case InvocationExpressionSyntax call:
                        seeds.Add(call.Expression);
                        break;
                }
            }
        }
    }
}
