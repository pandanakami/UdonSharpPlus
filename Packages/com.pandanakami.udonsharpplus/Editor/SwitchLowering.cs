using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UdonSharpPlus
{
    /// <summary>
    /// パターンのラベルを含む switch 文 → どの case に入るかを if で決めた番号の switch（#198）。
    ///
    /// UdonSharp は switch 文のラベルのうち、case 定数: と default: しか見ず、パターンのラベル（case "b" when n &gt; 0: ・case &gt; 5: ・
    /// case 1 or 2: ・case var x: など）を、エラーも出さずに読み飛ばす。そのため、条件を見ずに入ったり、入るべき case に入らなかったりする。
    ///
    ///   switch (k) { case "b" when n &gt; 0: A; break; case &gt; 5: B; break; default: C; break; }
    ///   → { var __sw0 = (k); int __sc0 = -1; if (__sw0 == ("b") &amp;&amp; (n &gt; 0)) __sc0 = 0; else if (__sw0 &gt; (5)) __sc0 = 1;
    ///       switch (__sc0) { case 0: A; break; case 1: B; break; default: C; break; } }
    ///
    /// - どの case に入るかは、C# と同じく、書いた順に調べて最初に合ったもの（default は最後）
    /// - 中身は番号の switch に残すので、break・continue・return の意味は変わらない。switch の式は 1 回だけ評価する
    /// - var x・int n などで宣言した変数は、その case の頭で宣言し直す（when の中では、switch の式の一時変数を読む）
    /// - 行は増やさない（頭はその行に、ラベルはその行に置き換え、閉じ括弧のうしろに } を足す）
    /// - ほかの段より前に置く（書き換えたあとはふつうの C# なので、ほかの段が中の式をそのまま書き換えられる）
    /// </summary>
    internal static class SwitchLowering
    {
        public static bool MayHavePatternSwitches(List<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().OfType<CasePatternSwitchLabelSyntax>().Any());

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = Compilations.For(trees, references);
            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                SyntaxNode root = tree.GetRoot();
                var switches = root.DescendantNodes().OfType<SwitchStatementSyntax>()
                    .Where(s => s.Sections.SelectMany(x => x.Labels).Any(l => l is CasePatternSwitchLabelSyntax)).ToList();
                if (switches.Count == 0)
                {
                    result.Add(tree);
                    continue;
                }
                var writer = new Writer(compilation.Model(tree), tree.GetText(), diagnostics);
                foreach (SwitchStatementSyntax s in switches)
                    writer.Rewrite(s);
                result.Add(writer.Changed
                    ? CSharpSyntaxTree.ParseText(writer.Apply(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }
            return result;
        }

        private sealed class Writer
        {
            private readonly SemanticModel _model;
            private readonly SourceText _text;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly List<TextChange> _changes = new List<TextChange>();
            private int _count;
            private string _tag;
            private bool _failed;

            public Writer(SemanticModel model, SourceText text, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _text = text;
                _diagnostics = diagnostics;
            }

            public bool Changed => _changes.Count > 0;

            public string Apply() => _text.WithChanges(_changes.OrderBy(c => c.Span.Start)).ToString();

            private void Error(SyntaxNode at, string message)
            {
                _diagnostics.Error(at.GetLocation(), message, _tag);
                _failed = true;
            }

            public void Rewrite(SwitchStatementSyntax node)
            {
                // UdonSharpBehaviour と、普通のクラス（使われるものだけエラーを残す）の中だけ。MonoBehaviour などは UdonSharp が扱わない
                if (!(node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is TypeDeclarationSyntax typeDecl)
                    || !(_model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol containing))
                    return;
                if (InterfaceLowering.IsUdonSharpBehaviour(containing))
                    _tag = null;
                else if (ClassLowering.IsPlainClass(containing))
                    _tag = ClassLowering.TagOf(containing);
                else
                    return;
                _failed = false;

                ITypeSymbol type = _model.GetTypeInfo(node.Expression).Type;
                if (!IsSupportedType(type))
                {
                    if (!(type is INamedTypeSymbol named && named.IsTupleType))   // タプルの switch は、タプルの段がエラーにする
                        Error(node.Sections.SelectMany(s => s.Labels).First(l => l is CasePatternSwitchLabelSyntax),
                            Loc.T($"{type?.ToDisplayString() ?? "この型"} の switch では、パターン・when の付いた case は未対応です（数・文字・bool・文字列・enum の switch で使えます）。if で書いてください",
                                $"Patterns and case guards (when) are not supported in a switch on {type?.ToDisplayString() ?? "this type"} (they work on numbers, char, bool, string and enum). Use if instead"));
                    return;
                }

                int n = _count++;
                string v = $"__sw{n}", c = $"__sc{n}";
                var head = new StringBuilder();
                head.Append($"{{ var {v} = ({Render(node.Expression, null)}); int {c} = -1; ");
                var labelEdits = new List<TextChange>();
                bool first = true;
                // 必ず合うもの（下）を default にするので、それより前の default: は、使われない case にする（default が 2 つにならないように）
                bool catchAll = false;
                var sections = new List<(List<string> Conditions, Dictionary<string, ITypeSymbol> Declared, bool HasDefault)>();
                for (int i = 0; i < node.Sections.Count; i++)
                {
                    SwitchSectionSyntax section = node.Sections[i];
                    var conditions = new List<string>();
                    var declared = new Dictionary<string, ITypeSymbol>();
                    bool hasDefault = false;
                    foreach (SwitchLabelSyntax label in section.Labels)
                    {
                        switch (label)
                        {
                            case DefaultSwitchLabelSyntax _:
                                hasDefault = true;
                                break;
                            case CaseSwitchLabelSyntax constant:
                                conditions.Add($"{v} == ({Render(constant.Value, null)})");
                                break;
                            case CasePatternSwitchLabelSyntax pattern:
                                var designations = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
                                string condition = Pattern(pattern.Pattern, v, type, designations);
                                if (condition == null)
                                    return;
                                foreach (ISymbol d in designations.Keys)
                                    if (!declared.ContainsKey(d.Name))
                                        declared[d.Name] = ((ILocalSymbol)d).Type;
                                if (pattern.WhenClause != null)
                                    condition = $"({condition}) && ({Render(pattern.WhenClause.Condition, designations.ToDictionary(p => p.Key, p => v, SymbolEqualityComparer.Default))})";
                                conditions.Add(condition);
                                break;
                        }
                    }
                    // 必ず合うもの（case var x: ・case int n: ・case _:）は default にする。番号の case にすると、元は終わりまで行かない switch
                    // （return で終わるメソッドなど）が、終わりまで行けることになり、「値を返さない道がある」エラーになるため
                    if (conditions.Contains("true"))
                        catchAll = true;
                    sections.Add((conditions, declared, hasDefault));
                }
                if (_failed)
                    return;
                for (int i = 0; i < node.Sections.Count; i++)
                {
                    SwitchSectionSyntax section = node.Sections[i];
                    var (conditions, declared, hasDefault) = sections[i];
                    bool unreachable = false;
                    if (conditions.Contains("true"))
                    {
                        conditions.Clear();
                        hasDefault = true;
                    }
                    else if (hasDefault && catchAll)
                    {
                        hasDefault = false;       // 必ず合うものがあるので、元の default には来ない
                        unreachable = conditions.Count == 0;
                    }
                    if (conditions.Count > 0)
                    {
                        string joined = conditions.Count == 1 ? conditions[0] : string.Join(" || ", conditions.Select(x => "(" + x + ")"));
                        head.Append(first ? "" : "else ").Append($"if ({joined}) {c} = {i}; ");
                        first = false;
                    }
                    var label2 = new StringBuilder();
                    if (conditions.Count > 0)
                        label2.Append($"case {i}: ");
                    if (hasDefault)
                        label2.Append("default: ");
                    if (unreachable)
                        label2.Append("case -2: ");
                    // パターンの変数は、C# ではその case の中だけのもの。switch の中の宣言は switch 全体のものになるので、case の中身を { } で囲む
                    if (declared.Count > 0)
                    {
                        label2.Append("{ ");
                        foreach (var d in declared)
                            label2.Append($"{d.Value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {d.Key} = {v}; ");
                        labelEdits.Add(new TextChange(new TextSpan(section.Statements.Last().Span.End, 0), " }"));
                    }
                    TextSpan labels = TextSpan.FromBounds(section.Labels.First().SpanStart, section.Labels.Last().Span.End);
                    labelEdits.Add(new TextChange(labels, KeepLines(label2.ToString().TrimEnd(), _text.ToString(labels))));
                }
                head.Append($"switch ({c})");

                TextSpan header = TextSpan.FromBounds(node.SwitchKeyword.SpanStart, node.CloseParenToken.Span.End);
                var edits = new List<TextChange> { new TextChange(header, KeepLines(head.ToString(), _text.ToString(header))) };
                edits.AddRange(labelEdits);
                edits.Add(new TextChange(new TextSpan(node.CloseBraceToken.Span.End, 0), " }"));
                // 入れ子で重なるもの（ラベルや式の中の switch など）は書き換えられない
                if (edits.Any(e => _changes.Any(o => o.Span.OverlapsWith(e.Span) || (e.Span.Length == 0 && o.Span.Start < e.Span.Start && e.Span.Start < o.Span.End)
                                                     || (o.Span.Length == 0 && e.Span.Start < o.Span.Start && o.Span.Start < e.Span.End))))
                {
                    Error(node.SwitchKeyword.Parent, Loc.T("この場所の、パターン・when の付いた case を含む switch は未対応です。if で書いてください",
                        "A switch with patterns or case guards (when) is not supported here. Use if instead"));
                    return;
                }
                _changes.AddRange(edits);
            }

            private static bool IsSupportedType(ITypeSymbol type)
            {
                if (type == null)
                    return false;
                if (type.TypeKind == TypeKind.Enum)
                    return true;
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Char:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_Decimal:
                    case SpecialType.System_String:
                        return true;
                }
                return false;
            }

            /// <summary>パターン → v を調べる条件の式（書けないものはエラーにして null）。宣言した変数は designations に入れる</summary>
            private string Pattern(PatternSyntax pattern, string v, ITypeSymbol type, Dictionary<ISymbol, string> designations)
            {
                switch (pattern)
                {
                    case ConstantPatternSyntax constant:
                        return constant.Expression.IsKind(SyntaxKind.NullLiteralExpression) ? $"{v} == null" : $"{v} == ({Render(constant.Expression, null)})";
                    case RelationalPatternSyntax relational:
                        return $"{v} {relational.OperatorToken.Text} ({Render(relational.Expression, null)})";
                    case BinaryPatternSyntax binary:
                    {
                        string l = Pattern(binary.Left, v, type, designations), r = Pattern(binary.Right, v, type, designations);
                        if (l == null || r == null)
                            return null;
                        return binary.OperatorToken.IsKind(SyntaxKind.AndKeyword) ? $"({l}) && ({r})" : $"({l}) || ({r})";
                    }
                    case UnaryPatternSyntax unary:
                    {
                        string p = Pattern(unary.Pattern, v, type, designations);
                        return p == null ? null : $"!({p})";
                    }
                    case ParenthesizedPatternSyntax parenthesized:
                        return Pattern(parenthesized.Pattern, v, type, designations);
                    case DiscardPatternSyntax _:
                        return "true";
                    case VarPatternSyntax var:
                        return Designate(var.Designation, pattern, designations) ? "true" : null;
                    case DeclarationPatternSyntax declaration:
                        if (!SameType(declaration.Type, type, pattern))
                            return null;
                        return Designate(declaration.Designation, pattern, designations) ? NotNull(v, type) : null;
                    case TypePatternSyntax typePattern:
                        return SameType(typePattern.Type, type, pattern) ? NotNull(v, type) : null;
                }
                Error(pattern, Loc.T("このパターン（プロパティ・位置のパターンなど）は未対応です。定数・関係（< > など）・and / or / not・var・when を使うか、if で書いてください",
                    "This pattern (property / positional patterns, etc.) is not supported. Use constants, relational patterns (< > ...), and / or / not, var and when, or use if"));
                return null;
            }

            // 型のパターンは null に合わない（var は合う）
            private static string NotNull(string v, ITypeSymbol type) => type.IsReferenceType ? $"{v} != null" : "true";

            private bool SameType(TypeSyntax typeSyntax, ITypeSymbol type, SyntaxNode at)
            {
                if (SymbolEqualityComparer.Default.Equals(_model.GetTypeInfo(typeSyntax).Type, type))
                    return true;
                Error(at, Loc.T($"switch の式と違う型の型パターン（{typeSyntax}）は未対応です（UdonSharp は is の型の判定ができません）",
                    $"A type pattern with a type different from the switch expression ({typeSyntax}) is not supported (UdonSharp cannot test types with is)"));
                return false;
            }

            private bool Designate(VariableDesignationSyntax designation, SyntaxNode at, Dictionary<ISymbol, string> designations)
            {
                switch (designation)
                {
                    case DiscardDesignationSyntax _:
                        return true;
                    case SingleVariableDesignationSyntax single when _model.GetDeclaredSymbol(single) is ILocalSymbol local:
                        designations[local] = single.Identifier.ValueText;
                        return true;
                }
                Error(at, Loc.T("このパターンの変数の宣言は未対応です。if で書いてください", "This variable declaration in a pattern is not supported. Use if instead"));
                return false;
            }

            /// <summary>式の文字列（改行は空白に、コメントは外す。replace の変数は置き換える）</summary>
            private string Render(SyntaxNode node, Dictionary<ISymbol, string> replace)
            {
                var sb = new StringBuilder();
                foreach (SyntaxToken token in node.DescendantTokens())
                {
                    if (token != node.GetFirstToken())
                        AppendTrivia(sb, token.LeadingTrivia);
                    if (replace != null && token.Parent is IdentifierNameSyntax id && _model.GetSymbolInfo(id).Symbol is ISymbol s && replace.TryGetValue(s, out string r))
                        sb.Append(r);
                    else
                        sb.Append(token.Text);
                    if (token != node.GetLastToken())
                        AppendTrivia(sb, token.TrailingTrivia);
                }
                return sb.ToString();
            }

            private static void AppendTrivia(StringBuilder sb, SyntaxTriviaList trivia)
            {
                if (trivia.Any(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia)
                                    || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)))
                    sb.Append(' ');
            }

            /// <summary>置き換えた code のうしろに、元の original にあった改行を足す（うしろの行をずらさない）</summary>
            private static string KeepLines(string code, string original) =>
                code + new string('\n', original.Count(ch => ch == '\n'));
        }
    }
}
