using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 書き換えの段が、見る必要のあるファイル（tree）を選ぶ（Redmine #163）。
    ///
    /// 段ごとに、全部のファイルのメソッドの中身を解析すると、それだけで 1 段あたり数秒かかる。段の対象になる型
    /// （演算子を宣言したクラス、タプル、生成した匿名型のクラスなど）の値は、どこかでその型の名前か、その型を型・戻り値・引数に
    /// 含むメンバーの名前を書かないと手に入らないので、そのどれも書いていないファイルは、段の対象を含まない。
    /// 名前は宣言だけから集める（メソッドの中身は解析しない）。名前が同じだけの別のものも拾うので、選ぶのは多めになる。
    ///
    /// 名前を書かずに値を取り出せるもの（インデクサー・foreach の列挙子・Deconstruct・演算子・変換）が対象の型を返す型は、
    /// その型も対象として扱う（その型の値の名前を書いたファイルを選ぶ）。派生クラスも対象として扱う
    /// </summary>
    internal static class TreeFilter
    {
        /// <param name="isTarget">段の対象の型か（型引数・配列の要素の中も、この関数で見る）</param>
        /// <param name="isTargetDeclaration">ソースの型のうち、段の対象として始めるもの（演算子を宣言したクラスなど。null なら isTarget だけ）</param>
        /// <param name="syntaxHit">そのファイルを必ず選ぶ書き方（タプルの書き方など）</param>
        /// <returns>ファイルごとに、見る必要があるか</returns>
        public static bool[] Select(Compilation compilation, IReadOnlyList<SyntaxTree> trees, Func<ITypeSymbol, bool> isTarget,
            Func<INamedTypeSymbol, bool> isTargetDeclaration, Func<SyntaxNode, bool> syntaxHit) =>
            SelectMembers(compilation, trees, isTarget, isTargetDeclaration, syntaxHit).Trees;

        /// <summary>
        /// Select と同じファイルを選び、選んだファイルの中で見る必要のあるメンバーも決める（Redmine #165）。
        /// 値は、メンバーの境を越えるとき（フィールド・メソッド・プロパティ・引数）に名前か型を書くので、ファイルと同じ考え方で、
        /// 対象の名前も選ぶ書き方も含まないメンバーは、段の対象を含まない。ただし、対象の型（と派生）の中のメンバーは、
        /// this で対象の値を名前なしに使えるので、いつも見る
        /// </summary>
        public static Selection SelectMembers(Compilation compilation, IReadOnlyList<SyntaxTree> trees, Func<ITypeSymbol, bool> isTarget,
            Func<INamedTypeSymbol, bool> isTargetDeclaration, Func<SyntaxNode, bool> syntaxHit)
        {
            List<INamedTypeSymbol> sourceTypes = SourceTypes(compilation.Assembly.GlobalNamespace).ToList();

            // 対象の型（ソースのもの）。派生・名前のない取り出し方で対象を返す型・対象を含むデリゲートを、増えなくなるまで足す
            var targets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            if (isTargetDeclaration != null)
                foreach (INamedTypeSymbol t in sourceTypes)
                    if (isTargetDeclaration(t))
                        targets.Add(t);

            bool Involves(ITypeSymbol type, int depth = 0)
            {
                if (type == null || depth > 16)
                    return false;
                if (isTarget(type))
                    return true;
                switch (type)
                {
                    case IArrayTypeSymbol array:
                        return Involves(array.ElementType, depth + 1);
                    case INamedTypeSymbol named:
                        if (targets.Contains(named.OriginalDefinition))
                            return true;
                        foreach (ITypeSymbol argument in named.TypeArguments)
                            if (Involves(argument, depth + 1))
                                return true;
                        return false;
                }
                return false;
            }

            bool SignatureInvolves(ISymbol member)
            {
                switch (member)
                {
                    case IFieldSymbol f: return Involves(f.Type);
                    case IPropertySymbol p: return Involves(p.Type) || p.Parameters.Any(x => Involves(x.Type));
                    case IEventSymbol e: return Involves(e.Type);
                    case IMethodSymbol m: return Involves(m.ReturnType) || m.Parameters.Any(x => Involves(x.Type));
                }
                return false;
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (INamedTypeSymbol t in sourceTypes)
                {
                    if (targets.Contains(t))
                        continue;
                    bool add = t.BaseType != null && targets.Contains(t.BaseType.OriginalDefinition)
                               || t.TypeKind == TypeKind.Delegate && t.DelegateInvokeMethod != null && SignatureInvolves(t.DelegateInvokeMethod)
                               || t.GetMembers().Any(m => IsUnnamedAccess(m) && SignatureInvolves(m));
                    if (add)
                    {
                        targets.Add(t);
                        changed = true;
                    }
                }
            }

            // 選ぶきっかけになる名前：対象の型の名前と、対象を含むメンバーの名前。コンストラクタが対象を受け取る型は、その型の名前
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (INamedTypeSymbol t in targets)
                names.Add(t.Name);
            foreach (INamedTypeSymbol t in sourceTypes)
                foreach (ISymbol m in t.GetMembers())
                {
                    if (m.IsImplicitlyDeclared || !SignatureInvolves(m))
                        continue;
                    if (m is IMethodSymbol method && (method.MethodKind == MethodKind.Constructor || method.MethodKind == MethodKind.StaticConstructor))
                        names.Add(t.Name);
                    else
                        names.Add(m.Name);
                }

            var result = new bool[trees.Count];
            for (int i = 0; i < trees.Count; i++)
            {
                SyntaxNode root = trees[i].GetRoot();
                result[i] = syntaxHit != null && root.DescendantNodes().Any(syntaxHit)
                            || root.DescendantTokens().Any(token => token.IsKind(SyntaxKind.IdentifierToken) && names.Contains(token.ValueText));
            }
            return new Selection(result, names, targets, syntaxHit);
        }

        /// <summary>選んだファイルと、その中で見る必要のあるメンバー</summary>
        internal sealed class Selection
        {
            /// <summary>ファイルごとに、見る必要があるか</summary>
            public readonly bool[] Trees;

            private readonly HashSet<string> _names;
            private readonly HashSet<INamedTypeSymbol> _targets;
            private readonly Func<SyntaxNode, bool> _syntaxHit;
            private readonly Dictionary<SyntaxTree, HashSet<string>> _aliases = new Dictionary<SyntaxTree, HashSet<string>>();

            public Selection(bool[] trees, HashSet<string> names, HashSet<INamedTypeSymbol> targets, Func<SyntaxNode, bool> syntaxHit)
            {
                Trees = trees;
                _names = names;
                _targets = targets;
                _syntaxHit = syntaxHit;
            }

            /// <summary>
            /// member（型の中のメンバー。型の宣言・名前空間は含めない）の中を見る必要があるか。
            /// 対象の型（と派生）の中のもの、選ぶ書き方を含むもの、対象の名前（using の別名も）を書いたものは見る
            /// </summary>
            public bool NeedsMember(MemberDeclarationSyntax member, SemanticModel model)
            {
                for (SyntaxNode n = member.Parent; n != null; n = n.Parent)
                {
                    if (n is BaseTypeDeclarationSyntax type && model.GetDeclaredSymbol(type) is INamedTypeSymbol symbol
                        && _targets.Contains(symbol.OriginalDefinition))
                        return true;
                }
                if (_syntaxHit != null && member.DescendantNodesAndSelf().Any(_syntaxHit))
                    return true;
                HashSet<string> aliases = Aliases(member.SyntaxTree);
                return member.DescendantTokens().Any(token => token.IsKind(SyntaxKind.IdentifierToken)
                    && (_names.Contains(token.ValueText) || aliases.Contains(token.ValueText)));
            }

            /// <summary>using の別名のうち、対象の名前を含むもの（using V = Vec; の V）</summary>
            private HashSet<string> Aliases(SyntaxTree tree)
            {
                if (_aliases.TryGetValue(tree, out var aliases))
                    return aliases;
                aliases = new HashSet<string>(StringComparer.Ordinal);
                foreach (UsingDirectiveSyntax u in tree.GetRoot().DescendantNodes(n => !(n is TypeDeclarationSyntax)).OfType<UsingDirectiveSyntax>())
                {
                    if (u.Alias != null && u.Name.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && _names.Contains(t.ValueText)))
                        aliases.Add(u.Alias.Name.Identifier.ValueText);
                }
                _aliases[tree] = aliases;
                return aliases;
            }
        }

        /// <summary>名前を書かずに使われるメンバー（インデクサー・列挙子・Deconstruct・演算子・変換）</summary>
        private static bool IsUnnamedAccess(ISymbol member) =>
            member is IPropertySymbol p && (p.IsIndexer || p.Name == "Current")
            || member is IMethodSymbol m && (m.MethodKind == MethodKind.UserDefinedOperator || m.MethodKind == MethodKind.Conversion
                                             || m.Name == "GetEnumerator" || m.Name == "Deconstruct" || m.Name == "Add");

        private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol ns)
        {
            foreach (INamespaceOrTypeSymbol member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    foreach (INamedTypeSymbol t in SourceTypes(child))
                        yield return t;
                }
                else if (member is INamedTypeSymbol type && type.Locations.Any(l => l.IsInSource))
                {
                    yield return type;
                    foreach (INamedTypeSymbol nested in Nested(type))
                        yield return nested;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
        {
            foreach (INamedTypeSymbol nested in type.GetTypeMembers())
            {
                yield return nested;
                foreach (INamedTypeSymbol inner in Nested(nested))
                    yield return inner;
            }
        }
    }
}
