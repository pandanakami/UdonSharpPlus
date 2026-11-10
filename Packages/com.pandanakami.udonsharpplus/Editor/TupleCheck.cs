using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// タプル（Redmine #81）。UdonSharp はタプルに対応しておらず、そのままだと内部の例外
    /// （ArgumentNullException、"Source symbol cannot be null" など）になって原因が分かりにくいので、分かりやすいエラーにする。
    ///
    /// - タプルの型（(int, string)、ValueTuple&lt;int, string&gt;）、タプルの式（(1, "a")）、分解（var (x, y) = ...、foreach (var (k, v) in ...)）を見つける
    /// - 対象は UdonSharpBehaviour と普通のクラス。普通のクラスのものは、そのクラスが使われるときだけ残す（使われないクラスは検査しない。#49 と同じ）
    /// - 1 か所につきエラーは 1 つ（見つけたものの中は見ない）
    /// - 書き換えの前に動かすので、元のソースの位置にエラーが出る（ジェネリクスのコピーの中は、SourceMap で元の位置に戻る）
    /// </summary>
    internal static class TupleCheck
    {
        private const string Message = "タプルは未対応です（UdonSharp が対応していないため）。クラスにするか、別々の変数にしてください";

        public static bool MayHaveTuples(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n =>
                n is TupleTypeSyntax || n is TupleExpressionSyntax || n is ParenthesizedVariableDesignationSyntax
                || n is ForEachVariableStatementSyntax
                || n is GenericNameSyntax g && g.Identifier.ValueText == "ValueTuple"));

        public static void Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_TuplePrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol))
                        continue;
                    // 入れ子のクラスは別のエラーになる（#77）ので見ない
                    if (symbol.ContainingType != null)
                        continue;

                    string owner;
                    if (InterfaceLowering.IsUdonSharpBehaviour(symbol))
                        owner = null;
                    else if (ClassLowering.IsPlainClass(symbol))
                        owner = ClassLowering.TagOf(symbol);
                    else
                        continue;   // 普通の MonoBehaviour など、UdonSharp で使わないクラス

                    foreach (MemberDeclarationSyntax member in decl.Members)
                        if (!(member is BaseTypeDeclarationSyntax))
                            Walk(member, model, diagnostics, owner);
                }
            }
        }

        private static void Walk(SyntaxNode node, SemanticModel model, InterfaceLowering.LoweringDiagnostics diagnostics, string owner)
        {
            string text = Describe(node, model);
            if (text != null)
            {
                diagnostics.Error(node.GetLocation(), $"'{text}' : {Message}", owner);
                return;
            }
            foreach (SyntaxNode child in node.ChildNodes())
                Walk(child, model, diagnostics, owner);
        }

        /// <summary>タプルなら、エラーに出す表記。そうでなければ null</summary>
        private static string Describe(SyntaxNode node, SemanticModel model)
        {
            switch (node)
            {
                // 分解：(a, b) = ...、var (x, y) = ...（右辺のタプルを含め、代入全体で 1 か所）
                case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.SimpleAssignmentExpression) && IsDeconstructionTarget(assign.Left):
                    return assign.Left.ToString();

                // foreach (var (k, v) in ...)
                case ForEachVariableStatementSyntax forEach:
                    return forEach.Variable.ToString();

                case TupleTypeSyntax tupleType:
                    return TypeText(model.GetTypeInfo(tupleType).Type) ?? tupleType.ToString();

                case TupleExpressionSyntax tuple:
                    return TypeText(model.GetTypeInfo(tuple).Type) ?? tuple.ToString();

                // ValueTuple<int, string>、System.ValueTuple<...>
                case NameSyntax name when !(name.Parent is QualifiedNameSyntax q && q.Right == name)
                                          && (name is GenericNameSyntax || name is QualifiedNameSyntax)
                                          && model.GetSymbolInfo(name).Symbol is INamedTypeSymbol type && type.IsTupleType:
                    return TypeText(type);

                default:
                    return null;
            }
        }

        private static bool IsDeconstructionTarget(ExpressionSyntax left) =>
            left is TupleExpressionSyntax
            || left is DeclarationExpressionSyntax d && d.Designation is ParenthesizedVariableDesignationSyntax;

        private static string TypeText(ITypeSymbol type) =>
            type == null || type.TypeKind == TypeKind.Error ? null : type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }
}
