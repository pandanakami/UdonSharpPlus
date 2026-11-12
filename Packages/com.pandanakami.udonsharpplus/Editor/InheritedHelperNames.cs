using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 基底と派生の UdonSharpBehaviour の両方に、同じ名前の生成メソッド（振り分け・普通のクラスのコピー・ラムダのメソッドなど）が
    /// できたとき、派生の側の名前を変える（Redmine #111）。
    ///
    /// 生成メソッドは、使う UdonSharpBehaviour ごとに同じ名前で作る（private static）。
    /// U# は private static でも、基底と同じ名前のメソッドを「基底のメソッドを隠している」とみなしてエラーにする
    /// （U# does not yet support hiding base methods）ため、書き換えの最後の段で、派生の側の宣言と呼び出しを
    /// 「名前__クラス名」に変える。生成メソッドは private なので、呼び出しはそのクラスの中にしかない。
    /// </summary>
    internal static class InheritedHelperNames
    {
        /// <summary>生成メソッドの名前（ユーザーのコードは __ で始まる名前を使わない）</summary>
        private static bool IsGenerated(string name) => name.StartsWith("__");

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, IEnumerable<MetadataReference> references)
        {
            // 生成メソッドのあるクラスが 2 つ以上なければ、重なりようがない（コンパイルを作らずに済ませる）
            int withGenerated = trees.Sum(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Count(c => c.Members.OfType<MethodDeclarationSyntax>().Any(m => IsGenerated(m.Identifier.ValueText))));
            if (withGenerated < 2)
                return trees;

            var compilation = CSharpCompilation.Create("UdonSharpPlusInheritedNames", trees, references);

            // クラスごとの、宣言している生成メソッドの名前（partial はまとめる）
            var declared = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
            var declarations = new List<(int Tree, ClassDeclarationSyntax Node, INamedTypeSymbol Symbol)>();
            for (int i = 0; i < trees.Count; i++)
            {
                SemanticModel model = compilation.GetSemanticModel(trees[i]);
                foreach (ClassDeclarationSyntax node in trees[i].GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol) || !InterfaceLowering.IsUdonSharpBehaviour(symbol))
                        continue;
                    if (!declared.TryGetValue(symbol, out var names))
                        declared[symbol] = names = new HashSet<string>();
                    names.UnionWith(node.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.ValueText).Where(IsGenerated));
                    declarations.Add((i, node, symbol));
                }
            }

            // 基底（ソースの UdonSharpBehaviour）と重なる名前を、派生の側で変える
            var renames = new Dictionary<INamedTypeSymbol, Dictionary<string, string>>(SymbolEqualityComparer.Default);
            foreach (var pair in declared)
            {
                var inherited = new HashSet<string>();
                for (INamedTypeSymbol b = pair.Key.BaseType; b != null; b = b.BaseType)
                    if (declared.TryGetValue(b, out var baseNames))
                        inherited.UnionWith(baseNames);

                var map = pair.Value.Where(inherited.Contains).ToDictionary(n => n, n => $"{n}__{pair.Key.Name}");
                if (map.Count > 0)
                    renames[pair.Key] = map;
            }
            if (renames.Count == 0)
                return trees;

            var result = new List<SyntaxTree>(trees);
            foreach (var group in declarations.Where(d => renames.ContainsKey(d.Symbol)).GroupBy(d => d.Tree))
            {
                SyntaxNode root = trees[group.Key].GetRoot();
                var nodes = group.ToList();
                root = root.ReplaceNodes(nodes.Select(d => d.Node), (o, _) =>
                {
                    var map = renames[nodes.First(d => d.Node == o).Symbol];
                    // 宣言の名前と、呼び出し（IdentifierName）の名前。長さが変わるだけで、行は変わらない
                    return o.ReplaceTokens(
                        o.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && map.ContainsKey(t.ValueText)
                            && (t.Parent is MethodDeclarationSyntax || t.Parent is IdentifierNameSyntax)),
                        (token, __) => SyntaxFactory.Identifier(token.LeadingTrivia, map[token.ValueText], token.TrailingTrivia));
                });
                result[group.Key] = trees[group.Key].WithRootAndOptions(root, trees[group.Key].Options);
            }
            return result;
        }
    }
}
