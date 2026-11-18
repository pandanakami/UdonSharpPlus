using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// SourceMap.Find（生成コードの位置 → 元の位置）が、tree ごとの印の表を使っても、前の作り
    /// （ノードから根まで、各ノードの最初のトークンと直前のトークンのトリビアを読む）と同じ印を返すこと（Redmine #165）
    /// </summary>
    public class SourceMapTests
    {
        // 印は、文の先頭（直前のトークンの末尾のトリビア）・行の先頭（トークンの先頭のトリビア）・同じ所に 2 つ・
        // 番号が表にないもの（99）に置く。書きかけのコード（足りないトークン）も混ぜる
        private const string Generated = @"
class A { /*cus@0*/ public int M() { /*cus@1*/ int x = 1; /*cus@2*/ /*cus@3*/ return x + F(/*cus@0*/ 2); } }
class B
{
    /*cus@1*/
    void N() { int y = (1 + ; /*cus@2*/ y++; }
    /*cus@99*/ void O() { /*cus@3*/ if (true) { } else /*cus@1*/ { O(); } }
    void P() { /*cus@2*/ var s = new int[] { 1, /*cus@0*/ 2 }; Q(() => /*cus@3*/ 3); }
}
/*cus@2*/ class C { int f = /*cus@99*/ 1; }";

        private static readonly Regex Pattern = new Regex(@"^/\*cus@(\d+)\*/$");

        [Test]
        public void FindIsSameAsWalkingTrivia()
        {
            lock (InterfaceLowering.ProcessLock)
            {
                SourceMap.Clear();
                try
                {
                    // 番号 0〜3 の印を作る（元の位置は、別の tree のノード）
                    SyntaxTree origin = CSharpSyntaxTree.ParseText("class O { void M() { int a = 1; int b = 2; int c = 3; int d = 4; } }");
                    var statements = origin.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax>().ToList();
                    var made = statements.Select((s, i) => SourceMap.Marker(s, "O", "M" + i)).ToList();
                    Assert.That(made, Is.EqualTo(new[] { "/*cus@0*/", "/*cus@1*/", "/*cus@2*/", "/*cus@3*/" }));

                    SyntaxTree tree = CSharpSyntaxTree.ParseText(Generated);
                    Assert.That(tree.GetDiagnostics().Any(), Is.True, "書きかけのコードを含める");
                    int found = 0;
                    for (int position = 0; position < tree.GetRoot().FullSpan.End; position++)
                    {
                        Location location = Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));
                        SourceMap.Entry expected = Reference(location, made.Count);
                        SourceMap.Entry actual = SourceMap.Find(location);
                        Assert.That(actual, Is.SameAs(expected), "位置 " + position + "：" + tree.GetRoot().FindToken(position));
                        if (actual != null)
                            found++;
                    }
                    Assert.That(found, Is.GreaterThan(100));
                }
                finally
                {
                    SourceMap.Clear();
                }
            }
        }

        // 前の作りの Find（#165 より前）
        private static SourceMap.Entry Reference(Location location, int count)
        {
            SyntaxNode root = location.SourceTree.GetRoot();
            int position = location.SourceSpan.Start;
            if (position >= root.FullSpan.End)
                return null;
            for (SyntaxNode node = root.FindToken(position).Parent; node != null; node = node.Parent)
            {
                SyntaxToken first = node.GetFirstToken();
                SourceMap.Entry found = Last(first.LeadingTrivia, count) ?? Last(first.GetPreviousToken().TrailingTrivia, count);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static SourceMap.Entry Last(SyntaxTriviaList trivia, int count)
        {
            for (int i = trivia.Count - 1; i >= 0; i--)
            {
                if (!SourceMap.IsMarker(trivia[i]))
                    continue;
                int id = int.Parse(Pattern.Match(trivia[i].ToString()).Groups[1].Value);
                // 前の作りと同じく、表にない番号は、その場所に印がないものとして扱う
                return id < count ? SourceMap.EntryOf(id) : null;
            }
            return null;
        }
    }
}
