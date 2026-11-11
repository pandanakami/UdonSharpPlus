using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// コメント・文字列の中に、本パッケージが書き換える書き方（インタフェース・普通のクラス・List・LINQ・ジェネリクス・タプル・#if）や、
    /// 位置の印と同じ形（/*cus@N*/）を書いても、書き換えに影響しない（#90）。
    /// コメントを同じ長さの空白にしたソース（行・列は同じ）と、Uasm・ヒープの初期値が同じになることで確かめる
    /// </summary>
    public class CommentContentTests
    {
        private const string Source = @"using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CommentContent
{
    public interface IUnit { int Hp(); }

    public class Unit : IUnit
    {
        public int hp = 3;
        // IUnit u = new Unit(); var l = new List<int>(); foreach (var kv in dict) { }
        public int Hp()
        {
            /* var box = new Box<int>(); box.Get(); list.Where(x => x > 0).Count(); */
            return hp; // u.Hp(); new Dictionary<string, int>()
        }
    }

    public class Box<T> { public T value; /* new Box<string>() */ public T Get() { return value; } }

    public class User : UdonSharpBehaviour
    {
        /// <summary>IUnit を <see cref=""List{T}""/> で持つ。例：new List<IUnit>()</summary>
        private List<int> _list = new List<int>();

        private void Start()
        {
            // #if UNITY_EDITOR （コメントなので指令ではない）
            IUnit u = new Unit(); /*cus@3*/
            /* (int, string) t = (1, ""a""); */
            _list.Add(u.Hp()); // _list.Add(new Unit()); _list.Find(x => x > 0);
            string code = ""new List<int>() IUnit u.Hp() /*cus@5*/ foreach (var kv in dict)"";
            Debug.Log(code + _list.Count(x => x > 0) + new Box<int>().Get());
        }
    }
}";

        /// <summary>コメントを同じ長さの空白にする（改行は残す。文字列リテラルの中は触らない）</summary>
        private static string BlankComments(string text)
        {
            var sb = new StringBuilder(text);
            foreach (SyntaxTrivia trivia in CSharpSyntaxTree.ParseText(text).GetRoot().DescendantTrivia(descendIntoTrivia: true))
            {
                if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                    && !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                    continue;
                for (int i = trivia.FullSpan.Start; i < trivia.FullSpan.End; i++)
                    if (sb[i] != '\n' && sb[i] != '\r')
                        sb[i] = ' ';
            }
            return sb.ToString();
        }

        [Test]
        public void CodeInCommentsIsNotRewritten()
        {
            CompileResult withComments = UdonSharpTestCompiler.Compile(new TestSource("User.cs", Source));
            CompileResult withoutComments = UdonSharpTestCompiler.Compile(new TestSource("User.cs", BlankComments(Source)));

            Assert.That(withComments.Diagnostics, Is.Empty);
            Assert.That(withoutComments.Diagnostics, Is.Empty);
            ProgramResult a = withComments["User"], b = withoutComments["User"];
            Assert.That(a.Uasm, Is.EqualTo(b.Uasm), "コメントの中身で、Uasm が変わりました");
            Assert.That(a.HeapDefaults, Is.EquivalentTo(b.HeapDefaults), "コメントの中身で、ヒープの初期値が変わりました");
        }

        [Test]
        public void CodeInStringIsKept()
        {
            ProgramResult program = UdonSharpTestCompiler.Compile(new TestSource("User.cs", Source))["User"];
            Assert.That(program.HeapDefaults.Values,
                Has.Member("\"new List<int>() IUnit u.Hp() /*cus@5*/ foreach (var kv in dict)\""));
        }
    }
}
