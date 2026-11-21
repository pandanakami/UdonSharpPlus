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
        /// 同じコルーチンへの再入をコンパイル時に見つける（#107 の決めたこと 10）。
        ///
        /// コルーチン C の本体の呼び出しから、その場で動くもの（yield return の外の StartCoroutine(X()) なら X の最初の区間、
        /// ふつうのメソッドならその本体）をたどり、C の開始に戻れたら、C の本体のその呼び出しをエラーにする。
        /// yield return StartCoroutine(...)・yield return X() は、親が変数を状態に書いてから子を開始するので、たどらない。
        /// デリゲート・SendCustomEvent・ほかのビヘイビアを通るものは、たどれない（実行時の確認で止める）
        /// </summary>
        private sealed class ReentryCheck
        {
            private readonly Compilation _compilation;
            private readonly Dictionary<IMethodSymbol, MethodDeclarationSyntax> _methods = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
            private readonly Dictionary<IMethodSymbol, List<IMethodSymbol>> _overrides = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
            private readonly HashSet<IMethodSymbol> _coroutines = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            private ReentryCheck(Compilation compilation) { _compilation = compilation; }

            /// <summary>
            /// method の、クラスの中の本体のある宣言（なければ null）。前はすべてのクラスのメソッドの宣言を先に問い合わせて表にしていたが、
            /// たどるのはコルーチンから呼ぶものだけなので、要るときにシンボルから引く（#203）
            /// </summary>
            private MethodDeclarationSyntax DeclarationOf(IMethodSymbol method)
            {
                if (_methods.TryGetValue(method, out MethodDeclarationSyntax found))
                    return found;
                found = null;
                if (method.MethodKind != MethodKind.ReducedExtension && SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
                    foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
                        if (reference.GetSyntax() is MethodDeclarationSyntax decl && decl.Parent is ClassDeclarationSyntax
                            && (decl.Body != null || decl.ExpressionBody != null))
                        {
                            found = decl;
                            break;
                        }
                _methods[method] = found;
                return found;
            }

            public static void Run(Compilation compilation, IEnumerable<SyntaxTree> trees, IEnumerable<TypeWork> works, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                var check = new ReentryCheck(compilation);
                // override の表。全部のメソッドの宣言を問い合わせず、ソースの型の override のメンバーだけから作る（#203）。
                // 候補の並びは、前と同じファイルとソースの順（たどった道の名前がエラーの文に入るため）
                var treeIndex = new Dictionary<SyntaxTree, int>();
                foreach (SyntaxTree tree in trees)
                    if (!treeIndex.ContainsKey(tree))
                        treeIndex[tree] = treeIndex.Count;
                var overriding = new List<(IMethodSymbol Symbol, MethodDeclarationSyntax Decl)>();
                foreach (INamedTypeSymbol type in TreeFilter.SourceTypes(compilation.Assembly.GlobalNamespace))
                {
                    if (type.TypeKind != TypeKind.Class)
                        continue;
                    foreach (ISymbol member in type.GetMembers())
                        if (member is IMethodSymbol method && method.IsOverride && check.DeclarationOf(method) is MethodDeclarationSyntax decl)
                            overriding.Add((method, decl));
                }
                foreach (var (symbol, _) in overriding.OrderBy(o => treeIndex.TryGetValue(o.Decl.SyntaxTree, out int i) ? i : int.MaxValue).ThenBy(o => o.Decl.SpanStart))
                    for (IMethodSymbol o = symbol.OverriddenMethod; o != null; o = o.OverriddenMethod)
                    {
                        if (!check._overrides.TryGetValue(o.OriginalDefinition, out var list))
                            check._overrides[o.OriginalDefinition] = list = new List<IMethodSymbol>();
                        list.Add(symbol);
                    }
                foreach (TypeWork work in works)
                    foreach (Coroutine co in work.Coroutines)
                        if (co.Symbol != null)
                            check._coroutines.Add(co.Symbol);

                foreach (TypeWork work in works)
                    foreach (Coroutine co in work.Coroutines.Where(c => c.Symbol != null))
                        foreach ((InvocationExpressionSyntax site, IMethodSymbol target, bool start) in check.Sites(co.Declaration, firstSegment: false))
                        {
                            List<string> path = start && SymbolEqualityComparer.Default.Equals(target, co.Symbol)
                                ? new List<string>()
                                : check.PathTo(target, start, co.Symbol, new HashSet<(IMethodSymbol, bool)>());
                            if (path == null)
                                continue;
                            string name = co.Declaration.Identifier.ValueText;
                            string route = string.Join(" → ", new[] { name, target.Name }.Concat(path));
                            diagnostics.Error(site.GetLocation(), Loc.T(
                                $"'{name}' : コルーチンが動いている途中に、同じコルーチン {name} を開始します（{route}）。UdonSharp ではローカル変数が上書きされるため未対応です。yield return StartCoroutine(...) の形で開始してください",
                                $"'{name}' : This starts the coroutine {name} again while it is running ({route}). This is not supported because UdonSharp would overwrite its local variables. Start it with yield return StartCoroutine(...)"));
                        }
            }

            /// <summary>
            /// target から、その場で動くものをたどって、coroutine の開始に着く道（target のあとの名前の並び）。着かなければ null。
            /// start なら target はコルーチンで、その最初の区間だけが動く
            /// </summary>
            private List<string> PathTo(IMethodSymbol target, bool start, IMethodSymbol coroutine, HashSet<(IMethodSymbol, bool)> visited)
            {
                foreach (IMethodSymbol candidate in Candidates(target))
                {
                    if (!visited.Add((candidate, start)) || !(DeclarationOf(candidate) is MethodDeclarationSyntax decl))
                        continue;
                    foreach ((_, IMethodSymbol next, bool nextStart) in Sites(decl, firstSegment: start))
                    {
                        if (nextStart && SymbolEqualityComparer.Default.Equals(next, coroutine))
                            return new List<string> { next.Name };
                        List<string> rest = PathTo(next, nextStart, coroutine, visited);
                        if (rest != null)
                        {
                            rest.Insert(0, next.Name);
                            return rest;
                        }
                    }
                }
                return null;
            }

            /// <summary>呼んだメソッドと、virtual なら override の候補</summary>
            private IEnumerable<IMethodSymbol> Candidates(IMethodSymbol method)
            {
                yield return method;
                if (_overrides.TryGetValue(method.OriginalDefinition, out var list))
                    foreach (IMethodSymbol o in list)
                        yield return o;
            }

            /// <summary>
            /// decl の中の、その場で動く呼び出し：(呼び出し, 相手, コルーチンの開始か)。
            /// firstSegment なら、本体の最初の区間（その前に必ず通る yield return のないところ）だけ
            /// </summary>
            private IEnumerable<(InvocationExpressionSyntax, IMethodSymbol, bool)> Sites(MethodDeclarationSyntax decl, bool firstSegment)
            {
                SyntaxNode body = (SyntaxNode)decl.Body ?? decl.ExpressionBody;
                SemanticModel model = _compilation.Model(decl.SyntaxTree);
                foreach (InvocationExpressionSyntax call in body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax)).OfType<InvocationExpressionSyntax>())
                {
                    if (!(model.SymbolInfoOf(call).Symbol is IMethodSymbol method))
                        continue;
                    if (firstSegment && AfterYield(call, body))
                        continue;
                    if (method.Name == "StartCoroutine" && method.ContainingType.ToDisplayString() == MonoBehaviourName)
                    {
                        if (call.Parent is YieldStatementSyntax || call.ArgumentList.Arguments.Count != 1)
                            continue;   // yield return StartCoroutine(...) は安全
                        if (call.ArgumentList.Arguments[0].Expression is InvocationExpressionSyntax inner
                            && model.SymbolInfoOf(inner).Symbol is IMethodSymbol coroutine && _coroutines.Contains(coroutine.OriginalDefinition))
                            yield return (call, coroutine.OriginalDefinition, true);
                        continue;
                    }
                    // コルーチンを直接呼ぶだけ（Foo()）は、状態を作るだけで中身は動かない
                    if (_coroutines.Contains(method.OriginalDefinition) || DeclarationOf(method.OriginalDefinition) == null)
                        continue;
                    yield return (call, method.OriginalDefinition, false);
                }
            }

            /// <summary>node の前に、必ず通る yield return があるか（node を含むブロックの、node より前の文が yield return）</summary>
            private static bool AfterYield(SyntaxNode node, SyntaxNode body)
            {
                for (SyntaxNode n = node; n != null && n != body.Parent; n = n.Parent)
                {
                    if (!(n is StatementSyntax s) || !(s.Parent is BlockSyntax block))
                        continue;
                    foreach (StatementSyntax before in block.Statements)
                    {
                        if (before == s)
                            break;
                        if (before is YieldStatementSyntax y && y.IsKind(SyntaxKind.YieldReturnStatement))
                            return true;
                    }
                }
                return false;
            }
        }
    }
}
