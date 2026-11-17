using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UdonSharpPlus
{
    /// <summary>
    /// 書き換えの段が使うコンパイルを、前の段とファイル（tree）が同じなら使い回す（Redmine #163）。
    ///
    /// 段ごとにコンパイルを作ると、意味の問い合わせのたびに、メソッドの中の型の解析（バインド）をやり直す。
    /// 前の段がどのファイルも書き換えなかったとき（その段の対象がなかったとき）や、注意を探す処理と最初の段のように
    /// 同じファイルを見るときは、同じコンパイルと意味モデル（SemanticModels）を使えば、解析は 1 回で済む。
    /// 書き換え 1 回分（InterfaceLowering.Process）の中だけで使い、終わったら Clear する
    /// </summary>
    internal static class Compilations
    {
        private static readonly CSharpCompilationOptions Options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        // 書き換えは InterfaceLowering.ProcessLock で 1 つずつにしているが、念のためスレッドごとに持つ
        [System.ThreadStatic] private static SyntaxTree[] _trees;
        [System.ThreadStatic] private static MetadataReference[] _references;
        [System.ThreadStatic] private static CSharpCompilation _last;

        public static CSharpCompilation For(IReadOnlyList<SyntaxTree> trees, IEnumerable<MetadataReference> references)
        {
            MetadataReference[] refs = references as MetadataReference[] ?? references.ToArray();
            if (_last != null && Same(_trees, trees) && Same(_references, refs))
                return _last;
            _last = CSharpCompilation.Create("UdonSharpPlus_Lowering", trees, refs, Options);
            _trees = trees.ToArray();
            _references = refs;
            return _last;
        }

        public static void Clear()
        {
            _last = null;
            _trees = null;
            _references = null;
        }

        private static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : class
        {
            if (a == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i]))
                    return false;
            return true;
        }
    }
}
