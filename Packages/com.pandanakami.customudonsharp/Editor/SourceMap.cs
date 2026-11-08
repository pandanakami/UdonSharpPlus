using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CustomUdonSharp
{
    /// <summary>
    /// 生成したコード（汎用クラスのコピー）の位置を、元のソースの位置に戻すための対応表（Redmine #52）。
    ///
    /// コピーの各文の前に "/*cus@12*/" という印のコメントを置き、番号 → 元の位置 をここに記録する。
    /// コメントはパースし直しても、後の書き換え（インタフェースの段）を通っても残り、1 行に収まるので行番号もずらさない。
    /// 対応表は U# のコンパイル 1 回分（LoadSyntaxTreesAndCreateModules の Postfix で作り直す）。
    ///
    /// tree はファイルパスではなく binding の番号で識別する。U# は tree をパスなしでパースし
    /// （SyntaxTree.FilePath は空）、ファイル名は ModuleBinding.filePath にだけ持っているため。
    /// 書き換えの各段の tree は、binding と同じ並びで作るので、番号で対応がとれる。
    /// </summary>
    internal static class SourceMap
    {
        internal sealed class Entry
        {
            public int TreeIndex;   // binding の番号
            public int Line;        // 0 始まり
            public int Character;   // 0 始まり
            public string ClassName;
            public string MemberName;
        }

        private static readonly Regex MarkerPattern = new Regex(@"^/\*cus@(\d+)\*/$", RegexOptions.Compiled);
        private static readonly object Gate = new object();
        private static List<Entry> _entries = new List<Entry>();
        private static Dictionary<SyntaxTree, int> _treeIndex = new Dictionary<SyntaxTree, int>();

        public static void Clear()
        {
            lock (Gate)
            {
                _entries = new List<Entry>();
                _treeIndex = new Dictionary<SyntaxTree, int>();
            }
        }

        /// <summary>書き換えの各段の tree を、binding の番号と結び付ける</summary>
        public static void RegisterTrees(IReadOnlyList<SyntaxTree> trees)
        {
            lock (Gate)
                for (int i = 0; i < trees.Count; i++)
                    _treeIndex[trees[i]] = i;
        }

        /// <summary>tree の binding の番号（登録されていなければ -1）</summary>
        public static int TreeIndex(SyntaxTree tree)
        {
            lock (Gate)
                return tree != null && _treeIndex.TryGetValue(tree, out int index) ? index : -1;
        }

        /// <summary>元の位置を記録し、生成コードに置く印のコメントを返す</summary>
        public static string Marker(SyntaxNode original, string className, string memberName)
        {
            // 元のノードが前の段のコピーの中（ジェネリクスのコピー、#59）なら、その元の位置と名前を引き継ぐ
            Entry inCopy = Find(original.GetLocation());
            if (inCopy != null)
            {
                lock (Gate)
                {
                    _entries.Add(new Entry
                    {
                        TreeIndex = inCopy.TreeIndex,
                        Line = inCopy.Line,
                        Character = inCopy.Character,
                        ClassName = inCopy.ClassName ?? className,
                        MemberName = inCopy.MemberName ?? memberName,
                    });
                    return $"/*cus@{_entries.Count - 1}*/";
                }
            }

            FileLinePositionSpan span = original.GetLocation().GetLineSpan();
            lock (Gate)
            {
                _entries.Add(new Entry
                {
                    TreeIndex = _treeIndex.TryGetValue(original.SyntaxTree, out int index) ? index : -1,
                    Line = span.StartLinePosition.Line,
                    Character = span.StartLinePosition.Character,
                    ClassName = className,
                    MemberName = memberName,
                });
                return $"/*cus@{_entries.Count - 1}*/";
            }
        }

        /// <summary>
        /// 生成メソッド（ラムダの本体など、#57）に置く印を返す。
        /// 元のノードが汎用クラスのコピーの中なら、そのコピーの元の位置を引き継ぐ
        /// </summary>
        public static string MarkerFor(SyntaxNode original)
        {
            Entry inCopy = Find(original.GetLocation());
            if (inCopy == null)
                return Marker(original, null, null);
            lock (Gate)
            {
                _entries.Add(new Entry
                {
                    TreeIndex = inCopy.TreeIndex,
                    Line = inCopy.Line,
                    Character = inCopy.Character,
                    ClassName = inCopy.ClassName,
                    MemberName = inCopy.MemberName,
                });
                return $"/*cus@{_entries.Count - 1}*/";
            }
        }

        /// <summary>エラーメッセージに付ける補足：「（汎用クラス Saver のメソッド Exists の中）」</summary>
        public static string Describe(Entry entry)
        {
            if (entry?.ClassName == null)
                return "";
            if (string.IsNullOrEmpty(entry.MemberName))
                return $"（汎用クラス {entry.ClassName} の中）";
            // 「メソッド Exists の中」のように英数字で終わるときだけ空白を入れる
            char last = entry.MemberName[entry.MemberName.Length - 1];
            string gap = last < 0x80 && char.IsLetterOrDigit(last) ? " " : "";
            return $"（汎用クラス {entry.ClassName} の{entry.MemberName}{gap}の中）";
        }

        public static bool IsMarker(SyntaxTrivia trivia) =>
            trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia) && MarkerPattern.IsMatch(trivia.ToString());

        /// <summary>生成コードの中の位置なら、元の位置。そうでなければ null</summary>
        public static Entry Find(Location location)
        {
            if (location == null || !location.IsInSource)
                return null;

            SyntaxNode root = location.SourceTree.GetRoot();
            int position = location.SourceSpan.Start;
            if (position >= root.FullSpan.End)
                return null;

            // 位置を含むノードから親へたどり、そのノードの直前にある印を探す。
            // 生成コードは 1 行なので、印のコメントは次のトークンの先頭ではなく、直前のトークンの末尾のトリビアになる
            for (SyntaxNode node = root.FindToken(position).Parent; node != null; node = node.Parent)
            {
                SyntaxToken first = node.GetFirstToken();
                Entry found = LastMarker(first.LeadingTrivia) ?? LastMarker(first.GetPreviousToken().TrailingTrivia);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>ノードの中の最初の印（生成コードでなければ null）</summary>
        public static Entry FirstIn(SyntaxNode node)
        {
            foreach (SyntaxTrivia trivia in node.DescendantTrivia())
            {
                if (!IsMarker(trivia))
                    continue;
                int id = int.Parse(MarkerPattern.Match(trivia.ToString()).Groups[1].Value);
                lock (Gate)
                    return id < _entries.Count ? _entries[id] : null;
            }
            return null;
        }

        private static Entry LastMarker(SyntaxTriviaList trivia)
        {
            for (int i = trivia.Count - 1; i >= 0; i--)
            {
                if (!IsMarker(trivia[i]))
                    continue;
                int id = int.Parse(MarkerPattern.Match(trivia[i].ToString()).Groups[1].Value);
                lock (Gate)
                    return id < _entries.Count ? _entries[id] : null;
            }
            return null;
        }

        /// <summary>元の位置を、同じファイルの tree（U# が持っているもの）の Location にする</summary>
        public static Location ToLocation(Entry entry, SyntaxTree tree)
        {
            var lines = tree.GetText().Lines;
            if (entry.Line >= lines.Count)
                return null;
            var line = lines[entry.Line];
            int position = line.Start + System.Math.Min(entry.Character, line.End - line.Start);
            return Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));
        }
    }
}
