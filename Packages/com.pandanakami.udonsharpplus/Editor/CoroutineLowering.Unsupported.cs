using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    internal static partial class CoroutineLowering
    {
        /// <summary>
        /// コルーチンの対応しない書き方を、ほかの段が書き換える前の形で見つけてエラーにする（#107 の決めたこと 3・5、追加テスト案の 15）。
        /// ほかの段（ローカル関数・普通のクラス・インタフェース）が書き換えたあとでは元の形が分からず、エラーが出ないか、
        /// 生成したコードについての分かりにくいエラーになるため、下ごしらえの最初に調べる。原因のところに 1 つだけ出す
        /// - yield を書ける場所：UdonSharpBehaviour の、IEnumerator を返すインスタンスメソッドだけ
        /// - IEnumerator の値：コルーチン（ソースのメソッド）が返すものだけ。外部のメソッドが返すもの・MoveNext・Current・Reset は使えない
        /// - ほかのビヘイビアの、IEnumerator を返すメソッド（other.Foo()）は呼べない（番号は、そのビヘイビアの状態の表のもの）
        /// </summary>
        private static void CheckUnsupported(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            if (!trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is YieldStatementSyntax
                    || (n is IdentifierNameSyntax id && (id.Identifier.ValueText == "IEnumerator" || UsesCoroutineApi(id))))))
                return;
            var compilation = Compilations.For(trees, references);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                SyntaxNode root = tree.GetRoot();
                var reported = new HashSet<SyntaxNode>();

                // yield を書いたところ（ラムダ・ローカル関数・メソッド・アクセサー）ごとに 1 つ
                foreach (YieldStatementSyntax y in root.DescendantNodes().OfType<YieldStatementSyntax>())
                {
                    SyntaxNode owner = y.Ancestors().FirstOrDefault(a => a is AnonymousFunctionExpressionSyntax || a is LocalFunctionStatementSyntax
                                                                        || a is BaseMethodDeclarationSyntax || a is AccessorDeclarationSyntax);
                    if (owner == null || !reported.Add(owner))
                        continue;
                    string message = YieldPlaceError(model, owner);
                    if (message != null)
                        diagnostics.Error(PlaceLocation(owner), message);
                }

                // インタフェースのメソッドの IEnumerator（インタフェースを通してコルーチンを開始する形）
                foreach (InterfaceDeclarationSyntax itf in TypeDeclarations.Of<InterfaceDeclarationSyntax>(root))
                    foreach (MethodDeclarationSyntax m in itf.Members.OfType<MethodDeclarationSyntax>())
                        if (model.GetDeclaredSymbol(m) is IMethodSymbol s && s.ReturnType.ToDisplayString() == EnumeratorName)
                            diagnostics.Error(m.Identifier.GetLocation(), Loc.T(
                                "インタフェースのメソッドのコルーチン（IEnumerator を返すメソッド）は未対応です。インタフェースには、コルーチンを開始する void のメソッドを書いてください",
                                "Coroutines in interface methods (methods returning IEnumerator) are not supported. Declare a void method that starts the coroutine instead"));

                // IEnumerator の値の使い方（UdonSharpBehaviour の中）
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(root))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    foreach (SyntaxNode node in OwnNodes(cls))
                        CheckEnumeratorUse(model, node, diagnostics);
                }
            }
        }

        /// <summary>yield を書いたところが、コルーチンとして書けない場所ならエラーの文（書ける場所なら null）</summary>
        private static string YieldPlaceError(SemanticModel model, SyntaxNode owner)
        {
            switch (owner)
            {
                case AnonymousFunctionExpressionSyntax _:
                    return null;   // C# でもエラー（CS1621）
                case LocalFunctionStatementSyntax _:
                    return Loc.T("ローカル関数のコルーチンは未対応です。クラスのメソッドにしてください",
                        "Coroutines in local functions are not supported. Make it a method of the class");
                case AccessorDeclarationSyntax _:
                    return Loc.T("プロパティ・インデクサーの中の yield は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                        "yield inside a property or indexer is not supported. Write coroutines as methods returning IEnumerator");
            }
            if (!(owner is MethodDeclarationSyntax m) || !(model.GetDeclaredSymbol(m) is IMethodSymbol symbol))
                return Loc.T("ここの yield は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                    "yield is not supported here. Write coroutines as methods returning IEnumerator");
            if (symbol.ReturnType.ToDisplayString() != EnumeratorName)
                return Loc.T($"{symbol.ReturnType.ToDisplayString()} を返す yield（列挙のための yield）は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                    $"yield returning {symbol.ReturnType.ToDisplayString()} (an iterator for enumeration) is not supported. Write coroutines as methods returning IEnumerator");
            if (!InterfaceLowering.IsUdonSharpBehaviour(symbol.ContainingType))
                return Loc.T("UdonSharpBehaviour でないクラスのメソッドのコルーチンは未対応です。UdonSharpBehaviour のメソッドに書いてください",
                    "Coroutines in methods of classes that are not UdonSharpBehaviour are not supported. Write them in an UdonSharpBehaviour");
            if (symbol.IsStatic)
                return Loc.T("static メソッドのコルーチンは未対応です。static を外してください",
                    "Coroutines in static methods are not supported. Remove static");
            return null;
        }

        private static Location PlaceLocation(SyntaxNode owner)
        {
            switch (owner)
            {
                case LocalFunctionStatementSyntax f: return f.Identifier.GetLocation();
                case MethodDeclarationSyntax m: return m.Identifier.GetLocation();
                case AccessorDeclarationSyntax a: return a.Keyword.GetLocation();
                default: return owner.GetLocation();
            }
        }

        private static void CheckEnumeratorUse(SemanticModel model, SyntaxNode node, InterfaceLowering.LoweringDiagnostics diagnostics)
        {
            // e.MoveNext()・e.Current・e.Reset()
            if (node is MemberAccessExpressionSyntax access
                && (access.Name.Identifier.ValueText == "MoveNext" || access.Name.Identifier.ValueText == "Current" || access.Name.Identifier.ValueText == "Reset")
                && model.GetTypeInfo(access.Expression).Type?.ToDisplayString() == EnumeratorName)
            {
                diagnostics.Error(access.Name.GetLocation(), Loc.T(
                    $"IEnumerator の {access.Name.Identifier.ValueText} は未対応です。IEnumerator はコルーチンにだけ使えます（StartCoroutine で動かしてください）",
                    $"IEnumerator.{access.Name.Identifier.ValueText} is not supported. IEnumerator can only be used for coroutines (run it with StartCoroutine)"));
                return;
            }

            if (!(node is InvocationExpressionSyntax call) || !(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)
                || method.ReturnType.ToDisplayString() != EnumeratorName)
                return;
            if (method.DeclaringSyntaxReferences.Length == 0)
            {
                // 外部のメソッドが返す IEnumerator（transform.GetEnumerator() など）
                diagnostics.Error(call.GetLocation(), Loc.T(
                    $"外部のメソッド（{method.ContainingType.Name}.{method.Name}）が返す IEnumerator は、コルーチンとして使えません。IEnumerator はコルーチンにだけ使えます",
                    $"An IEnumerator returned by an external method ({method.ContainingType.Name}.{method.Name}) cannot be used as a coroutine. IEnumerator can only be used for coroutines"));
                return;
            }
            // other.Foo()：ほかのビヘイビアの、IEnumerator を返すメソッド
            if (call.Expression is MemberAccessExpressionSyntax target && !method.IsStatic
                && !(target.Expression is ThisExpressionSyntax) && !(target.Expression is BaseExpressionSyntax)
                && InterfaceLowering.IsUdonSharpBehaviour(method.ContainingType))
            {
                diagnostics.Error(call.GetLocation(), Loc.T(
                    $"ほかのビヘイビアのコルーチン（{target.Expression}.{method.Name}(...)）は未対応です。そのビヘイビアに、コルーチンを開始する public メソッドを作って呼んでください",
                    $"Coroutines of another behaviour ({target.Expression}.{method.Name}(...)) are not supported. Add a public method that starts the coroutine to that behaviour and call it"));
            }
        }
    }
}
