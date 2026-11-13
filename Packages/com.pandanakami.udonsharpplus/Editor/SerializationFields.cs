using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 型を object[] に書き換えるフィールド（普通のクラス型・デリゲート型と、その配列）を、UdonSharp にシリアライズさせない（Redmine #127）。
    ///
    /// UdonSharp は、シリアライズするフィールドの配列の既定値を空の配列にする（EmitContext）。シリアライズさせたままだと、
    /// フィールドが null ではなく object[0] で始まり、== null が false になる。
    ///   - public なら [NonSerialized] を付ける
    ///   - [OdinSerialize] は外す（UdonSharp は [NonSerialized] より [OdinSerialize] を優先する。自動アップグレードが
    ///     ユーザーのスクリプトに足していることがある）
    /// 行は変えない（外した属性の前後の trivia は、次のトークンに移す）
    /// </summary>
    internal static class SerializationFields
    {
        public static bool IsOdinSerialize(AttributeSyntax a)
        {
            string name = a.Name is QualifiedNameSyntax q ? q.Right.Identifier.ValueText
                : a.Name is AliasQualifiedNameSyntax aq ? aq.Name.Identifier.ValueText
                : a.Name is IdentifierNameSyntax id ? id.Identifier.ValueText : a.Name.ToString();
            return name == "OdinSerialize" || name == "OdinSerializeAttribute";
        }

        private static bool IsNonSerialized(AttributeSyntax a)
        {
            string text = a.Name.ToString();
            return text.EndsWith("NonSerialized") || text.EndsWith("NonSerializedAttribute");
        }

        /// <summary>field を書き換えなかったときは同じインスタンスを返す</summary>
        public static FieldDeclarationSyntax MakeNonSerialized(FieldDeclarationSyntax field)
        {
            // private（[SerializeField] はエラーにしている）なら、[OdinSerialize] を外すだけでシリアライズされない
            FieldDeclarationSyntax result = RemoveOdinSerialize(field);
            if (!result.Modifiers.Any(SyntaxKind.PublicKeyword) || result.AttributeLists.SelectMany(l => l.Attributes).Any(IsNonSerialized))
                return result;

            // public でも Unity・UdonSharp にシリアライズさせない（同じ行に属性を足す）
            var attribute = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.NonSerialized"))))
                .WithTrailingTrivia(SyntaxFactory.Space);
            SyntaxTriviaList leading = result.GetLeadingTrivia();
            return result.WithoutLeadingTrivia().WithAttributeLists(result.AttributeLists.Insert(0, attribute)).WithLeadingTrivia(leading);
        }

        private static FieldDeclarationSyntax RemoveOdinSerialize(FieldDeclarationSyntax field)
        {
            if (!field.AttributeLists.SelectMany(l => l.Attributes).Any(IsOdinSerialize))
                return field;

            var lists = new List<AttributeListSyntax>();
            var pending = new List<SyntaxTrivia>();
            foreach (AttributeListSyntax list in field.AttributeLists)
            {
                var keep = list.Attributes.Where(a => !IsOdinSerialize(a)).ToList();
                if (keep.Count == 0)
                {
                    // 属性の文字の分は空白にして、列も保つ
                    pending.AddRange(list.GetLeadingTrivia());
                    pending.Add(SyntaxFactory.Whitespace(new string(' ', list.Span.Length)));
                    pending.AddRange(list.GetTrailingTrivia());
                    continue;
                }
                AttributeListSyntax kept = keep.Count == list.Attributes.Count ? list : list.WithAttributes(SyntaxFactory.SeparatedList(keep));
                if (pending.Count > 0)
                {
                    kept = kept.WithLeadingTrivia(pending.Concat(kept.GetLeadingTrivia()));
                    pending.Clear();
                }
                lists.Add(kept);
            }

            FieldDeclarationSyntax result = field.WithAttributeLists(SyntaxFactory.List(lists));
            if (pending.Count > 0)
            {
                SyntaxToken first = result.Modifiers.Count > 0 ? result.Modifiers[0] : result.Declaration.GetFirstToken();
                result = result.ReplaceToken(first, first.WithLeadingTrivia(pending.Concat(first.LeadingTrivia)));
            }
            return result;
        }
    }
}
