using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

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
    }
}
