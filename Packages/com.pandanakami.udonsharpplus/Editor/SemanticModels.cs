using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 意味モデルを、（コンパイル, 木）ごとに 1 つだけ作って使い回す（Redmine #163）。
    ///
    /// Compilation.GetSemanticModel は呼ぶたびに新しい意味モデルを作り、型の解析（バインド）の結果も作り直す。
    /// 書き換えの段では、同じ木の意味モデルをクラス・メンバーごとに取り直していたので、同じ解析を何百回も繰り返していた。
    /// コンパイルが使われなくなれば、表も一緒に消える（ConditionalWeakTable）。意味モデルはスレッドセーフなので、
    /// 並列の Emit（テスト）から使ってもよい
    /// </summary>
    internal static class SemanticModels
    {
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SyntaxTree, SemanticModel>> Cache =
            new ConditionalWeakTable<Compilation, ConcurrentDictionary<SyntaxTree, SemanticModel>>();

        public static SemanticModel Model(this Compilation compilation, SyntaxTree tree) =>
            Cache.GetValue(compilation, _ => new ConcurrentDictionary<SyntaxTree, SemanticModel>())
                .GetOrAdd(tree, t => compilation.GetSemanticModel(t));

        /// <summary>
        /// GetSymbolInfo と同じ結果を返す。書き換えの段は、意味の問い合わせをすべてこれで行う（Redmine #165）。
        ///
        /// メソッドの呼び出しの名前の部分（xs.Sum(...) の xs.Sum と Sum）への GetSymbolInfo は、Roslyn が拡張メソッドの探索を
        /// 毎回やり直すので、同じ意味モデルで 2 回目でも 1 回 0.2 ミリ秒ほどかかる（呼び出し全体への問い合わせの 20 倍以上）。
        /// 段はほとんどの名前に問い合わせるので、ここが書き換えの時間の 2 割ほどになっていた。
        /// 呼び出し全体の結果がメソッドなら、名前の部分の結果も同じメソッドなので、そちらを返す。
        /// デリゲートの呼び出し（d() の Invoke。名前の部分はフィールド・変数など）と、決まらなかったもの（候補だけ・nameof）は、
        /// 名前の部分にそのまま問い合わせる
        /// </summary>
        public static SymbolInfo SymbolInfoOf(this SemanticModel model, SyntaxNode node)
        {
            InvocationExpressionSyntax call = CallOfName(node);
            if (call != null)
            {
                SymbolInfo info = model.GetSymbolInfo(call);
                if (info.Symbol is IMethodSymbol method && method.MethodKind != MethodKind.DelegateInvoke)
                    return info;
            }
            return model.GetSymbolInfo(node);
        }

        /// <summary>node が呼び出しの名前の部分（M・x.M・x?.M の .M と M）なら、その呼び出し</summary>
        private static InvocationExpressionSyntax CallOfName(SyntaxNode node)
        {
            if (!(node is SimpleNameSyntax || node is MemberAccessExpressionSyntax || node is MemberBindingExpressionSyntax))
                return null;
            SyntaxNode target = node;
            if ((node.Parent is MemberAccessExpressionSyntax access && access.Name == node)
                || (node.Parent is MemberBindingExpressionSyntax binding && binding.Name == node))
                target = node.Parent;
            return target.Parent is InvocationExpressionSyntax call && call.Expression == target ? call : null;
        }
    }
}
