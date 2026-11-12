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
    /// 「名前__クラスの完全名」に変える。生成メソッドは private なので、呼び出しはそのクラスの中にしかない。
    ///
    /// partial のクラスで、2 つの宣言（ファイル）の両方に同じ名前の生成メソッドができたときも、2 つ目以降の宣言の側を
    /// 「名前__p2」などに変える（#114。生成メソッドは宣言ごとに作り、ラムダの番号も宣言ごとに 0 から振るため）。
    /// 呼び出しは、その生成メソッドを作った宣言の中にある。
    /// </summary>
    internal static class InheritedHelperNames
    {
        /// <summary>名前に付けるクラスの完全名（識別子に使える文字にする）</summary>
        private static string Key(INamedTypeSymbol symbol) =>
            new string(symbol.ToDisplayString().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

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

            // 宣言ごとに、変える名前を決める（宣言の並びは tree の順。partial の 1 つ目の宣言が先）
            //   - 基底（ソースの UdonSharpBehaviour）と重なる名前：「名前__完全名」（#111。名前空間が違う同じ名前のクラスでも重ならないよう完全名。#115）
            //   - 同じクラスの前の宣言（partial）と重なる名前：さらに「__p2」などを足す（#114）
            var renames = new Dictionary<ClassDeclarationSyntax, Dictionary<string, string>>();
            var finalNames = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
            var partIndex = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
            var classFinal = new Dictionary<INamedTypeSymbol, Dictionary<string, string>>(SymbolEqualityComparer.Default);   // 最初に宣言した側の名前
            foreach (var d in declarations)
            {
                var inherited = new HashSet<string>();
                for (INamedTypeSymbol b = d.Symbol.BaseType; b != null; b = b.BaseType)
                    if (declared.TryGetValue(b, out var baseNames))
                        inherited.UnionWith(baseNames);

                if (!finalNames.TryGetValue(d.Symbol, out var earlier))
                    finalNames[d.Symbol] = earlier = new HashSet<string>();
                partIndex[d.Symbol] = partIndex.TryGetValue(d.Symbol, out int k) ? k + 1 : 1;

                var map = new Dictionary<string, string>();
                foreach (string name in d.Node.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.ValueText).Where(IsGenerated).Distinct())
                {
                    string final = inherited.Contains(name) ? $"{name}__{Key(d.Symbol)}" : name;
                    if (earlier.Contains(final))
                        final += "__p" + partIndex[d.Symbol];
                    if (final != name)
                        map[name] = final;
                    earlier.Add(final);
                }
                if (map.Count > 0)
                    renames[d.Node] = map;
                foreach (string name in map.Keys.Concat(d.Node.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.ValueText).Where(IsGenerated)))
                {
                    if (!classFinal.TryGetValue(d.Symbol, out var names))
                        classFinal[d.Symbol] = names = new Dictionary<string, string>();
                    if (!names.ContainsKey(name))
                        names[name] = map.TryGetValue(name, out string f) ? f : name;
                }
            }

            // partial の別の宣言にだけある生成メソッド（普通のクラスのコピーは 1 つの宣言にだけ置く）の呼び出しは、
            // それを宣言した側の名前に合わせる
            foreach (var d in declarations)
            {
                if (!classFinal.TryGetValue(d.Symbol, out var names))
                    continue;
                var own = new HashSet<string>(d.Node.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.ValueText));
                foreach (var pair in names.Where(n => n.Key != n.Value && !own.Contains(n.Key)))
                {
                    if (!renames.TryGetValue(d.Node, out var map))
                        renames[d.Node] = map = new Dictionary<string, string>();
                    map[pair.Key] = pair.Value;
                }
            }
            if (renames.Count == 0)
                return trees;

            var result = new List<SyntaxTree>(trees);
            foreach (var group in declarations.Where(d => renames.ContainsKey(d.Node)).GroupBy(d => d.Tree))
            {
                SyntaxNode root = trees[group.Key].GetRoot();
                root = root.ReplaceNodes(group.Select(d => d.Node), (o, _) =>
                {
                    var map = renames[o];
                    // 宣言の名前と、その宣言の中の呼び出し（IdentifierName）の名前。長さが変わるだけで、行は変わらない
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
