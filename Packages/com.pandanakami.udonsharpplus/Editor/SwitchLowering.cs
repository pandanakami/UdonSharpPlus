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
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is CasePatternSwitchLabelSyntax || n is SwitchExpressionSyntax));

        private static bool IsTarget(SyntaxNode n) =>
            n is SwitchExpressionSyntax
            || (n is SwitchStatementSyntax s && s.Sections.SelectMany(x => x.Labels).Any(l => l is CasePatternSwitchLabelSyntax));

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = Compilations.For(trees, references);
            var result = new List<SyntaxTree>(trees.Count);
            for (int t = 0; t < trees.Count; t++)
            {
                SyntaxTree tree = trees[t];
                SyntaxNode root = tree.GetRoot();
                // 外側のものから（中の switch 式は、外側を書き換えるときに一緒に書き換える）
                var targets = root.DescendantNodes().Where(IsTarget).ToList();
                if (targets.Count == 0)
                {
                    result.Add(tree);
                    continue;
                }
                var writer = new Writer(compilation.Model(tree), tree.GetText(), diagnostics, t);
                foreach (SyntaxNode node in targets)
                {
                    if (node is SwitchStatementSyntax s)
                        writer.Rewrite(s);
                    else
                        writer.Rewrite((SwitchExpressionSyntax)node);
                }
                writer.AddNoMatchMethods();
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
            private readonly int _treeIndex;
            /// <summary>どの腕にも合わないときのメソッドを足すクラス</summary>
            private readonly HashSet<TypeDeclarationSyntax> _noMatch = new HashSet<TypeDeclarationSyntax>();

            public Writer(SemanticModel model, SourceText text, InterfaceLowering.LoweringDiagnostics diagnostics, int treeIndex)
            {
                _model = model;
                _text = text;
                _diagnostics = diagnostics;
                _treeIndex = treeIndex;
            }

            /// <summary>
            /// switch 式がどの腕にも合わないとき（C# では SwitchExpressionException）に呼ぶメソッド。エラーのログを出し、null の配列を返す。
            /// 呼ぶ側が __sw_nomatch_N()[0] と添字を読み、その場で実行時例外にして止める（例外の位置が、元の switch 式に出るように。#205）。
            /// partial のクラスのほかのファイルと名前が重ならないよう、ファイルの番号を付ける
            /// </summary>
            private string NoMatchName => $"__sw_nomatch_{_treeIndex}";

            public void AddNoMatchMethods()
            {
                string message = Loc.T("[UdonSharpPlus] switch 式：どの腕にも合いませんでした（C# では SwitchExpressionException）",
                    "[UdonSharpPlus] switch expression: no arm matched (SwitchExpressionException in C#)");
                foreach (TypeDeclarationSyntax type in _noMatch)
                    _changes.Add(new TextChange(new TextSpan(type.CloseBraceToken.SpanStart, 0),
                        $" private static object[] {NoMatchName}() {{ UnityEngine.Debug.LogError(\"{message}\"); return null; }} "));
            }

            /// <summary>書き換え済みの範囲の中か（外側の switch を書き換えたときに、一緒に書き換えたもの）</summary>
            private bool Covered(SyntaxNode node) => _changes.Any(c => c.Span.Length > 0 && c.Span.Start <= node.SpanStart && node.Span.End <= c.Span.End);

            /// <summary>UdonSharpBehaviour と、普通のクラス（使われるものだけエラーを残す）の中だけ。MonoBehaviour などは UdonSharp が扱わない</summary>
            private bool SetOwner(SyntaxNode node)
            {
                if (!(node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is TypeDeclarationSyntax typeDecl)
                    || !(_model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol containing))
                    return false;
                if (InterfaceLowering.IsUdonSharpBehaviour(containing))
                    _tag = null;
                else if (ClassLowering.IsPlainClass(containing))
                    _tag = ClassLowering.TagOf(containing);
                else
                    return false;
                _failed = false;
                return true;
            }

            private bool Overlaps(IEnumerable<TextChange> edits) =>
                edits.Any(e => _changes.Any(o => o.Span.OverlapsWith(e.Span) || (e.Span.Length == 0 && o.Span.Start < e.Span.Start && e.Span.Start < o.Span.End)
                                                 || (o.Span.Length == 0 && e.Span.Start < o.Span.Start && o.Span.Start < e.Span.End)));

            public bool Changed => _changes.Count > 0;

            public string Apply() => _text.WithChanges(_changes.OrderBy(c => c.Span.Start)).ToString();

            private void Error(SyntaxNode at, string message)
            {
                _diagnostics.Error(at.GetLocation(), message, _tag);
                _failed = true;
            }

            public void Rewrite(SwitchStatementSyntax node)
            {
                if (!SetOwner(node))
                    return;
                if (Covered(node))
                {
                    // switch 式の腕のラムダの中など。外側を書き換えた文字列に、元のまま入ってしまった
                    Error(node.SwitchKeyword.Parent, Loc.T("この場所の、パターン・when の付いた case を含む switch は未対応です。if で書いてください",
                        "A switch with patterns or case guards (when) is not supported here. Use if instead"));
                    return;
                }

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
                var sections = new List<(List<string> Conditions, List<string> Marks, Dictionary<string, ITypeSymbol> Declared, bool HasDefault)>();
                for (int i = 0; i < node.Sections.Count; i++)
                {
                    SwitchSectionSyntax section = node.Sections[i];
                    var conditions = new List<string>();
                    var marks = new List<string>();   // 条件ごとの、元のラベルの位置の印（#205）
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
                                marks.Add(SourceMap.InPlaceMarker(label));
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
                                marks.Add(SourceMap.InPlaceMarker(label));
                                break;
                        }
                    }
                    // 必ず合うもの（case var x: ・case int n: ・case _:）は default にする。番号の case にすると、元は終わりまで行かない switch
                    // （return で終わるメソッドなど）が、終わりまで行けることになり、「値を返さない道がある」エラーになるため
                    if (conditions.Contains("true"))
                        catchAll = true;
                    sections.Add((conditions, marks, declared, hasDefault));
                }
                if (_failed)
                    return;
                for (int i = 0; i < node.Sections.Count; i++)
                {
                    SwitchSectionSyntax section = node.Sections[i];
                    var (conditions, marks, declared, hasDefault) = sections[i];
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
                        // 条件は switch の行にまとめて置くので、各条件の前に元のラベルの位置の印を置き、条件の中の実行時例外・エラーを
                        // そのラベルの位置に出す（#205）
                        string joined = conditions.Count == 1 ? marks[0] + conditions[0] : string.Join(" || ", conditions.Select((x, k) => "(" + marks[k] + x + ")"));
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
                if (Overlaps(edits))
                {
                    Error(node.SwitchKeyword.Parent, Loc.T("この場所の、パターン・when の付いた case を含む switch は未対応です。if で書いてください",
                        "A switch with patterns or case guards (when) is not supported here. Use if instead"));
                    return;
                }
                _changes.AddRange(edits);
            }

            // ---- switch 式（#202） -------------------------------------------------------

            /// <summary>
            /// switch 式 → 三項演算子の並び（g switch { p1 => r1, p2 when c => r2, _ => r3 } → ((条件1) ? (T)(r1) : (条件2) ? (T)(r2) : (T)(r3))）。
            /// 調べる式は 1 回だけ評価する：ローカル変数・引数・フィールド・定数なら、そのまま何度も読む。それ以外は、文の前で一時変数に入れる
            /// （return・宣言・代入の式全体と、=> の本体のときだけ。式の途中はエラー）
            /// </summary>
            public void Rewrite(SwitchExpressionSyntax node)
            {
                if (Covered(node) || !SetOwner(node))
                    return;
                if (IsTupleSwitch(node))
                    return;
                var edits = new List<TextChange>();
                string v;
                if (IsSimple(node.GoverningExpression, null))
                    v = "(" + Render(node.GoverningExpression, null) + ")";
                else
                {
                    v = $"__se{_count++}";
                    if (!Hoist(node, v, edits))
                        return;
                }
                string code = Ternary(node, v, null, keepLines: true);
                if (code == null || _failed)
                    return;
                edits.Add(new TextChange(node.Span, code));
                if (Overlaps(edits))
                {
                    Error(node, Loc.T("この場所の switch 式は未対応です。if で書いてください", "A switch expression is not supported here. Use if instead"));
                    return;
                }
                _changes.AddRange(edits);
            }

            private bool IsTupleSwitch(SwitchExpressionSyntax node) =>
                node.GoverningExpression is TupleExpressionSyntax || (_model.GetTypeInfo(node.GoverningExpression).Type is INamedTypeSymbol t && t.IsTupleType);   // タプルの段がエラーにする

            /// <summary>何度読んでも同じで、副作用のない式（ローカル変数・引数・フィールド・定数、パターンの変数の置き換え先）</summary>
            private bool IsSimple(ExpressionSyntax e, Dictionary<ISymbol, string> replace)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                if (e is LiteralExpressionSyntax || _model.GetConstantValue(e).HasValue)
                    return true;
                if (e is MemberAccessExpressionSyntax access && access.Expression is ThisExpressionSyntax)
                    e = access.Name;
                if (!(e is IdentifierNameSyntax id))
                    return false;
                ISymbol s = _model.GetSymbolInfo(id).Symbol;
                if (s != null && replace != null && replace.ContainsKey(s))
                    return true;
                return s is ILocalSymbol || s is IParameterSymbol || (s is IFieldSymbol f && !f.IsVolatile);
            }

            /// <summary>調べる式を、文の前で一時変数 v に入れる（入れられない場所ならエラーにして false）</summary>
            private bool Hoist(SwitchExpressionSyntax node, string v, List<TextChange> edits)
            {
                string decl = $"var {v} = ({Render(node.GoverningExpression, null)}); ";
                SyntaxNode parent = node.Parent;
                while (parent is ParenthesizedExpressionSyntax)
                    parent = parent.Parent;
                switch (parent)
                {
                    // return g switch { ... };  → { var v = (g); return ...; }
                    case ReturnStatementSyntax ret:
                        edits.Add(new TextChange(new TextSpan(ret.SpanStart, 0), "{ " + decl));
                        edits.Add(new TextChange(new TextSpan(ret.Span.End, 0), " }"));
                        return true;
                    // var y = g switch { ... };  → var v = (g); var y = ...;（宣言は { } で囲めないので、前に置く）
                    case EqualsValueClauseSyntax eq when eq.Parent is VariableDeclaratorSyntax d && d.Parent is VariableDeclarationSyntax vd && vd.Variables.Count == 1
                                                         && vd.Parent is LocalDeclarationStatementSyntax local && (local.Parent is BlockSyntax || local.Parent is SwitchSectionSyntax):
                        edits.Add(new TextChange(new TextSpan(local.SpanStart, 0), decl));
                        return true;
                    // x = g switch { ... };  → { var v = (g); x = ...; }（左は変数・フィールドだけ。左を先に評価するものは、順番が変わるため）
                    case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.SimpleAssignmentExpression) && assign.Parent is ExpressionStatementSyntax statement
                                                               && !assign.Left.DescendantNodesAndSelf().Any(n => n is InvocationExpressionSyntax || n is AssignmentExpressionSyntax || n is PostfixUnaryExpressionSyntax || n is PrefixUnaryExpressionSyntax):
                        edits.Add(new TextChange(new TextSpan(statement.SpanStart, 0), "{ " + decl));
                        edits.Add(new TextChange(new TextSpan(statement.Span.End, 0), " }"));
                        return true;
                    // int F() => g switch { ... };  → int F() { var v = (g); return ...; }
                    case ArrowExpressionClauseSyntax arrow when arrow.Parent is MethodDeclarationSyntax || arrow.Parent is LocalFunctionStatementSyntax
                                                               || arrow.Parent is OperatorDeclarationSyntax || arrow.Parent is ConversionOperatorDeclarationSyntax
                                                               || (arrow.Parent is AccessorDeclarationSyntax a && a.IsKind(SyntaxKind.GetAccessorDeclaration))
                                                               || arrow.Parent is PropertyDeclarationSyntax || arrow.Parent is IndexerDeclarationSyntax:
                    {
                        SyntaxToken semicolon = arrow.Parent is BaseMethodDeclarationSyntax m ? m.SemicolonToken
                            : arrow.Parent is LocalFunctionStatementSyntax lf ? lf.SemicolonToken
                            : arrow.Parent is AccessorDeclarationSyntax acc ? acc.SemicolonToken
                            : arrow.Parent is PropertyDeclarationSyntax prop ? prop.SemicolonToken
                            : ((IndexerDeclarationSyntax)arrow.Parent).SemicolonToken;
                        bool getter = arrow.Parent is PropertyDeclarationSyntax || arrow.Parent is IndexerDeclarationSyntax;
                        edits.Add(new TextChange(arrow.ArrowToken.Span, (getter ? "{ get { " : "{ ") + decl + "return"));
                        edits.Add(new TextChange(semicolon.Span, getter ? "; } }" : "; }"));
                        return true;
                    }
                }
                Error(node.GoverningExpression, Loc.T("この場所の switch 式は、調べる式を変数・引数・フィールド・定数にしてください（式を 1 回だけ評価するため。変数に入れてから switch してください）",
                    "In this position, the value a switch expression tests must be a variable, parameter, field or constant (so it is evaluated once). Store it in a variable first"));
                return false;
            }

            /// <summary>switch 式の三項演算子の並び。v は調べる値の式（何度読んでもよいもの）。書けないものはエラーにして null</summary>
            private string Ternary(SwitchExpressionSyntax node, string v, Dictionary<ISymbol, string> outer, bool keepLines)
            {
                ITypeSymbol type = _model.GetTypeInfo(node.GoverningExpression).Type;
                if (!IsSupportedType(type))
                {
                    Error(node.GoverningExpression, Loc.T($"{type?.ToDisplayString() ?? "この型"} の switch 式は未対応です（数・文字・bool・文字列・enum で使えます）。if で書いてください",
                        $"A switch expression on {type?.ToDisplayString() ?? "this type"} is not supported (it works on numbers, char, bool, string and enum). Use if instead"));
                    return null;
                }
                TypeInfo info = _model.GetTypeInfo(node);
                ITypeSymbol result = info.ConvertedType ?? info.Type;
                bool writable = result != null && result.TypeKind != TypeKind.Error && !result.IsAnonymousType && !(result is INamedTypeSymbol named && named.IsTupleType && named.TupleElements.Any(e => e.Type.IsAnonymousType));
                string cast = writable ? "(" + result.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" : "";

                var sb = new StringBuilder("(");
                int anchor = node.SpanStart;
                bool closed = false;
                foreach (SwitchExpressionArmSyntax arm in node.Arms)
                {
                    if (keepLines)
                        sb.Append(Lines(anchor, arm.SpanStart));
                    anchor = arm.Span.End;
                    var designations = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
                    string condition = Pattern(arm.Pattern, v, type, designations);
                    if (condition == null)
                        return null;
                    var replace = outer == null ? new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default) : new Dictionary<ISymbol, string>(outer, SymbolEqualityComparer.Default);
                    foreach (ISymbol d in designations.Keys)
                        replace[d] = v;
                    if (arm.WhenClause != null)
                        condition = $"({condition}) && ({Render(arm.WhenClause.Condition, replace)})";
                    string value = $"{cast}({Render(arm.Expression, replace)})";
                    if (condition == "true")
                    {
                        sb.Append(value);   // 必ず合う腕。うしろの腕には来ない（C# もうしろに腕を書くとエラー）
                        closed = true;
                    }
                    else
                        sb.Append($"({condition}) ? {value} : ");
                    if (keepLines)
                        sb.Append(Lines(arm.SpanStart, arm.Span.End));
                    if (closed)
                        break;
                }
                if (!closed)
                {
                    if (!writable)
                    {
                        Error(node, Loc.T("この switch 式は、最後に _ => の腕を書いてください（どの腕にも合わないときの値の型が書けないため）",
                            "Add a _ => arm at the end of this switch expression (the type of its value cannot be written for the no-match case)"));
                        return null;
                    }
                    if (node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is TypeDeclarationSyntax holder)
                        _noMatch.Add(holder);
                    // 印（SourceMap）で、ここの実行時例外を元の switch 式の位置に出す
                    sb.Append($"{cast}{SourceMap.InPlaceMarker(node)}{NoMatchName}()[0]");
                }
                sb.Append(')');
                string code = sb.ToString();
                return keepLines ? KeepLines(code, _text.ToString(node.Span)) : code;
            }

            /// <summary>from から to までの改行（腕の並びの行を、元の行に残すため）</summary>
            private string Lines(int from, int to) => new string('\n', _text.ToString(TextSpan.FromBounds(from, to)).Count(ch => ch == '\n'));

            /// <summary>式の途中（条件・腕の値・when）にある switch 式。調べる式は、何度読んでもよいものだけ</summary>
            private string Inline(SwitchExpressionSyntax node, Dictionary<ISymbol, string> replace)
            {
                if (IsTupleSwitch(node) || !IsSimple(node.GoverningExpression, replace))
                {
                    if (!IsTupleSwitch(node))
                        Error(node.GoverningExpression, Loc.T("この場所の switch 式は、調べる式を変数・引数・フィールド・定数にしてください（式を 1 回だけ評価するため。変数に入れてから switch してください）",
                            "In this position, the value a switch expression tests must be a variable, parameter, field or constant (so it is evaluated once). Store it in a variable first"));
                    return null;
                }
                return Ternary(node, "(" + Render(node.GoverningExpression, replace) + ")", replace, keepLines: false);
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

            /// <summary>式の文字列（改行は空白に、コメントは外す。replace の変数は置き換え、中の switch 式は三項演算子にする）</summary>
            private string Render(SyntaxNode node, Dictionary<ISymbol, string> replace)
            {
                var sb = new StringBuilder();
                RenderInto(sb, node, replace, node);
                return sb.ToString();
            }

            private void RenderInto(StringBuilder sb, SyntaxNode node, Dictionary<ISymbol, string> replace, SyntaxNode root)
            {
                string swapped = null;
                if (node is SwitchExpressionSyntax inner)
                    swapped = Inline(inner, replace) ?? "default";
                else if (replace != null && node is IdentifierNameSyntax id && _model.GetSymbolInfo(id).Symbol is ISymbol s && replace.TryGetValue(s, out string r))
                    swapped = r;
                if (swapped != null)
                {
                    SyntaxToken first = node.GetFirstToken(), last = node.GetLastToken();
                    if (first != root.GetFirstToken())
                        AppendTrivia(sb, first.LeadingTrivia);
                    sb.Append(swapped);
                    if (last != root.GetLastToken())
                        AppendTrivia(sb, last.TrailingTrivia);
                    return;
                }
                foreach (SyntaxNodeOrToken child in node.ChildNodesAndTokens())
                {
                    if (child.IsNode)
                    {
                        RenderInto(sb, child.AsNode(), replace, root);
                        continue;
                    }
                    SyntaxToken token = child.AsToken();
                    if (token != root.GetFirstToken())
                        AppendTrivia(sb, token.LeadingTrivia);
                    sb.Append(token.Text);
                    if (token != root.GetLastToken())
                        AppendTrivia(sb, token.TrailingTrivia);
                }
            }

            private static void AppendTrivia(StringBuilder sb, SyntaxTriviaList trivia)
            {
                if (trivia.Any(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia)
                                    || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)))
                    sb.Append(' ');
            }

            /// <summary>置き換えた code のうしろに、元の original より足りない分の改行を足す（うしろの行をずらさない）</summary>
            private static string KeepLines(string code, string original) =>
                code + new string('\n', System.Math.Max(0, original.Count(ch => ch == '\n') - code.Count(ch => ch == '\n')));
        }
    }
}
