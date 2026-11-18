using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// tree の中の型の宣言（入れ子の型も）を、tree の順に返す（Redmine #165）。
    ///
    /// 型は名前空間か型の中にしか書けないので、メソッドの中身・式にはたどらない。DescendantNodes().OfType&lt;T&gt;() と同じものを返すが、
    /// 書き換えた tree（生成したメソッドが多い）では、ノードの大半がメソッドの中身なので、それを読まずに済む
    /// </summary>
    internal static class TypeDeclarations
    {
        public static IEnumerable<T> Of<T>(SyntaxNode root) where T : SyntaxNode =>
            root.DescendantNodes(n => n is CompilationUnitSyntax || n is NamespaceDeclarationSyntax || n is TypeDeclarationSyntax).OfType<T>();
    }
}
