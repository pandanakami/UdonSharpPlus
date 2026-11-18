using System;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace UdonSharpPlus
{
    /// <summary>
    /// 型について何度も調べる性質（UdonSharpBehaviour の派生か、など）を、型ごとに 1 回だけ求めて覚えておく（Redmine #165）。
    ///
    /// 基底を 1 つずつ ToDisplayString() で文字列にして比べるので、式ごとに呼ぶと文字列を作り直し続ける。
    /// 型（シンボル）は変わらないので、答えも変わらない。シンボルが使われなくなれば、表からも消える（ConditionalWeakTable）。
    /// U# の並列の処理から呼ばれてもよい（ConditionalWeakTable はスレッドセーフ）
    /// </summary>
    internal sealed class TypeFact
    {
        private sealed class Box
        {
            public bool Value;
        }

        private readonly ConditionalWeakTable<ITypeSymbol, Box> _table = new ConditionalWeakTable<ITypeSymbol, Box>();
        private readonly Func<ITypeSymbol, bool> _compute;

        public TypeFact(Func<ITypeSymbol, bool> compute)
        {
            _compute = compute;
        }

        public bool Of(ITypeSymbol type) => _table.GetValue(type, t => new Box { Value = _compute(t) }).Value;
    }
}
