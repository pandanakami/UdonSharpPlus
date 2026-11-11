using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 書き換えで生成したメンバー（1 行のコード）を、クラスの閉じ括弧 '}' と同じ行に置く（行番号をずらさないため）。
    ///
    /// Members.AddRange で足すだけだと、閉じ括弧の前の trivia（改行・字下げ・コメント・#if〜#endif・#endregion など）は
    /// 閉じ括弧に付いたまま残るので、生成したコードがその trivia の前に来る。trivia にプリプロセッサ指令があると、
    /// 指令が行頭でなくなり CS1040 になる（#96）。そこで、閉じ括弧の前の trivia を最初の生成メンバーの前に移し、
    /// 「…最後のメンバー / trivia / 字下げ + 生成したコード + '}'」の並びにする
    /// </summary>
    internal static class GeneratedMembers
    {
        /// <summary>code（1 行のメンバーの宣言）をパースし、閉じ括弧と同じ行に置く。パースできないものは捨てる</summary>
        public static ClassDeclarationSyntax AppendOnClosingLine(ClassDeclarationSyntax cls, IEnumerable<string> code)
        {
            List<MemberDeclarationSyntax> parsed = code.Select(c => SyntaxFactory.ParseMemberDeclaration(c + " ")).Where(d => d != null).ToList();
            if (parsed.Count == 0)
                return cls;

            SyntaxToken close = cls.CloseBraceToken;
            parsed[0] = parsed[0].WithLeadingTrivia(close.LeadingTrivia.AddRange(parsed[0].GetLeadingTrivia()));
            return cls.WithMembers(cls.Members.AddRange(parsed))
                .WithCloseBraceToken(close.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }
    }
}
