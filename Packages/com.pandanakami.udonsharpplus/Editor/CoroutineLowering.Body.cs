using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    internal static partial class CoroutineLowering
    {
        private sealed partial class ClassRewriter
        {
            /// <summary>
            /// コルーチンのメソッド 1 つを、その場で状態機械にする（#107）。
            ///
            /// U# は goto に対応していないので、yield return を含む文（if・ループ・switch・ブロック）は、
            /// 頭と終わりを「__pc = N; continue; case N:」（次の位置へ進む）に置き換え、中の文を switch (__pc) の直下に並べる。
            /// yield return を含まない文は、そのまま残す（中の break・continue が、置き換えたループを抜けるものなら、位置へ進むものにする）。
            ///
            ///   while (c) { A; yield return null; B; }
            ///     → __pc = 1; continue; case 1: if (!(c)) { __pc = 2; continue; }
            ///       A; __s[3] = x; __h[2] = 3; __co_wf(__s); return; case 3: x = (int)__s[3];
            ///       B; __pc = 1; continue; case 2:
            ///
            /// - 並べた文の中で宣言したローカル変数は、メソッドの頭で宣言する（宣言は代入にする）。同じ名前がほかにもあれば名前を変える
            /// - yield をまたいで使う変数（あとで読む、または yield を含むループの外で宣言してループの中で使う）は、
            ///   yield の直前に状態に書き、再開したところで読み戻す（#107 の決めたこと 9）。引数は再開のたびに頭で読むので、
            ///   書き換えるものだけ書く
            /// </summary>
            private sealed class BodyWriter
            {
                private sealed class Var
                {
                    public string Name;
                    public string Type;
                    public int DeclPos;
                    public bool IsParam;
                    public bool Written;
                    public int Slot = -1;
                    public readonly List<IdentifierNameSyntax> Refs = new List<IdentifierNameSyntax>();
                    public readonly List<int> Positions = new List<int>();
                }

                private readonly ClassRewriter _o;
                private readonly Coroutine _co;
                private readonly BlockSyntax _body;
                private readonly Edits _edits;
                private readonly SemanticModel _model;

                /// <summary>yield return を含む文</summary>
                private readonly HashSet<StatementSyntax> _flat = new HashSet<StatementSyntax>();
                private readonly List<Var> _vars = new List<Var>();
                private readonly Dictionary<ISymbol, Var> _bySymbol = new Dictionary<ISymbol, Var>(SymbolEqualityComparer.Default);
                /// <summary>yield return を含むループの、繰り返す範囲（for は初期化のうしろから）</summary>
                private readonly List<(int Start, int End)> _loops = new List<(int, int)>();
                private int _label;
                private int _nextSlot;
                private int _temp;

                public BodyWriter(ClassRewriter owner, Coroutine co)
                {
                    _o = owner;
                    _co = co;
                    _body = co.Declaration.Body;
                    _edits = owner._edits;
                    _model = owner._model;
                }

                private static string Label(int n) => $"__pc = {n}; continue; case {n}:";
                private static string Jump(int n) => $"{{ __pc = {n}; continue; }}";
                /// <summary>頭で宣言する変数の型（Coroutine・IEnumerator は状態の番号の int）</summary>
                private static string TypeText(ITypeSymbol type)
                {
                    if (IsHandle(type))
                        return "int";
                    if (type is IArrayTypeSymbol array && IsHandle(array.ElementType))
                        return "int[]";
                    return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }

                public void Run()
                {
                    MethodDeclarationSyntax m = _co.Declaration;

                    // 引数（状態の [1] から）
                    var parameters = m.ParameterList.Parameters.ToList();
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        ParameterSyntax p = parameters[i];
                        var v = new Var { Name = p.Identifier.ValueText, Type = _edits.Take(p.Type), DeclPos = -1, IsParam = true, Slot = i + 1 };
                        _vars.Add(v);
                        if (_model.GetDeclaredSymbol(p) is IParameterSymbol symbol)
                            _bySymbol[symbol] = v;
                    }
                    // 開始のメソッドの引数は、元の修飾子（params）・既定値のまま（ref・out・in は C# がコルーチンに許さない）
                    string defaults = string.Join(", ", parameters.Select((p, i) =>
                        $"{(p.Modifiers.Any(SyntaxKind.ParamsKeyword) ? "params " : "")}{_vars[i].Type} {_vars[i].Name}{(p.Default != null ? " " + _edits.Take(p.Default) : "")}"));
                    _nextSlot = parameters.Count + 1;

                    // yield return を含む文と、頭で宣言するローカル変数・ループ
                    foreach (YieldStatementSyntax y in Yields(_body).Where(y => y.IsKind(SyntaxKind.YieldReturnStatement)))
                        for (SyntaxNode n = y; n != _body; n = n.Parent)
                            if (n is StatementSyntax s)
                                _flat.Add(s);
                    ScanList(_body.Statements);
                    List<SingleVariableDesignationSyntax> designations = ScanDesignations();
                    CollectReferences();
                    RenameDuplicates();
                    RewriteDesignations(designations);

                    // yield break はどこでも、終わりにして戻る
                    foreach (YieldStatementSyntax y in Yields(_body).Where(y => y.IsKind(SyntaxKind.YieldBreakStatement)))
                    {
                        _edits.Take(y);
                        _edits.Add(y.Span, "{ __co_done(__s); return; }");
                    }

                    List(_body.Statements);

                    // 頭：修飾子から本体の開き括弧まで（引数が複数行なら、その分の改行を残す）
                    var head = new StringBuilder();
                    foreach (Var v in _vars)
                        head.Append(v.IsParam ? $"{v.Type} {v.Name} = ({v.Type})__s[{v.Slot}]; " : $"{v.Type} {v.Name} = default({v.Type}); ");
                    var headSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                        m.Modifiers.Count > 0 ? m.Modifiers.First().SpanStart : m.ReturnType.SpanStart, _body.OpenBraceToken.Span.End);
                    string headText = _edits.Take(headSpan);
                    _edits.Add(headSpan, $"private void {_co.StepName}(object[] __s) {{ int[] __h = (int[])__s[0]; __h[5] = 1; {head}int __pc = __h[2]; while (true) {{ switch (__pc) {{ case 0:" + LineBreaks(headText));   // 頭の中身（引数の読み出し）に、元の改行は入らない
                    _edits.Add(_body.CloseBraceToken.Span, "__co_done(__s); return; } } }");

                    // 開始のメソッド（元の名前・引数で、状態を作って番号を返す）
                    string modifiers = string.Join(" ", m.Modifiers.Select(t => t.ValueText));
                    var stores = new StringBuilder();
                    foreach (Var v in _vars.Where(v => v.IsParam))
                        stores.Append($"__s[{v.Slot}] = {v.Name}; ");
                    _o._members.Add($"{(modifiers.Length > 0 ? modifiers + " " : "")}int {m.Identifier.ValueText}({defaults}) {{ object[] __s = __co_alloc({_co.Id}, {_nextSlot}); {stores}return ((int[])__s[0])[0]; }}");
                }

                // ---- 下調べ ------------------------------------------------------

                private void ScanList(IEnumerable<StatementSyntax> statements)
                {
                    foreach (StatementSyntax s in statements)
                        Scan(s);
                }

                private void Scan(StatementSyntax s)
                {
                    if (s is LocalDeclarationStatementSyntax d && !d.IsConst)
                        foreach (VariableDeclaratorSyntax v in d.Declaration.Variables)
                            AddLocal(_model.GetDeclaredSymbol(v) as ILocalSymbol, s.SpanStart);
                    if (!_flat.Contains(s))
                        return;
                    switch (s)
                    {
                        case BlockSyntax b:
                            ScanList(b.Statements);
                            break;
                        case IfStatementSyntax i:
                            Scan(i.Statement);
                            if (i.Else != null)
                                Scan(i.Else.Statement);
                            break;
                        case WhileStatementSyntax w:
                            _loops.Add((w.SpanStart, w.Span.End));
                            Scan(w.Statement);
                            break;
                        case DoStatementSyntax o:
                            _loops.Add((o.SpanStart, o.Span.End));
                            Scan(o.Statement);
                            break;
                        case ForStatementSyntax f:
                            if (f.Declaration != null)
                                foreach (VariableDeclaratorSyntax v in f.Declaration.Variables)
                                    AddLocal(_model.GetDeclaredSymbol(v) as ILocalSymbol, f.SpanStart);
                            _loops.Add((f.FirstSemicolonToken.Span.End, f.Span.End));
                            Scan(f.Statement);
                            break;
                        case ForEachStatementSyntax e:
                            AddLocal(_model.GetDeclaredSymbol(e), e.Statement.SpanStart);
                            _loops.Add((e.SpanStart, e.Span.End));
                            Scan(e.Statement);
                            break;
                        case SwitchStatementSyntax sw:
                            foreach (SwitchSectionSyntax section in sw.Sections)
                                ScanList(section.Statements);
                            break;
                    }
                }

                /// <summary>
                /// 並べた文の中で宣言する out の変数（out var v・out int v）。スコープが並べた文の並びに広がるので、
                /// 頭で宣言する（#107 の追加テスト案の 2）。is T v のパターンは、UdonSharp がもともと扱えない（IsPatternExpression）ので、そのまま残す
                /// </summary>
                private List<SingleVariableDesignationSyntax> ScanDesignations()
                {
                    var found = new List<SingleVariableDesignationSyntax>();
                    foreach (SingleVariableDesignationSyntax d in _body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax))
                                 .OfType<SingleVariableDesignationSyntax>())
                    {
                        StatementSyntax s = d.Ancestors().OfType<StatementSyntax>().First();
                        bool inList = _flat.Contains(s)
                                      || (s.Parent is BlockSyntax b && (b == _body || _flat.Contains(b)))
                                      || (s.Parent is SwitchSectionSyntax section && _flat.Contains((StatementSyntax)section.Parent));
                        if (!inList || !(d.Parent is DeclarationExpressionSyntax declaration && declaration.Parent is ArgumentSyntax)
                            || !(_model.GetDeclaredSymbol(d) is ILocalSymbol symbol))
                            continue;
                        AddLocal(symbol, s.SpanStart);
                        found.Add(d);
                    }
                    return found;
                }

                /// <summary>out var v・out int v → out v（宣言は頭）</summary>
                private void RewriteDesignations(List<SingleVariableDesignationSyntax> designations)
                {
                    foreach (SingleVariableDesignationSyntax d in designations)
                    {
                        if (!(_model.GetDeclaredSymbol(d) is ILocalSymbol symbol) || !_bySymbol.TryGetValue(symbol, out Var v))
                            continue;
                        _edits.Add(((DeclarationExpressionSyntax)d.Parent).Span, v.Name);
                    }
                }

                private void AddLocal(ILocalSymbol symbol, int declPos)
                {
                    if (symbol == null || _bySymbol.ContainsKey(symbol))
                        return;
                    var v = new Var { Name = symbol.Name, Type = TypeText(symbol.Type), DeclPos = declPos };
                    _vars.Add(v);
                    _bySymbol[symbol] = v;
                }

                /// <summary>ループで使う、名前のない変数（foreach の配列と番号）。ループの中で使うものとして数える</summary>
                private Var AddTemp(string prefix, string type, ForEachStatementSyntax loop)
                {
                    var v = new Var { Name = $"__{prefix}{_temp++}", Type = type, DeclPos = loop.SpanStart - 1 };
                    v.Positions.Add(loop.Statement.SpanStart);
                    _vars.Add(v);
                    return v;
                }

                private void CollectReferences()
                {
                    foreach (IdentifierNameSyntax id in _body.DescendantNodes().OfType<IdentifierNameSyntax>())
                    {
                        if (!(_model.GetSymbolInfo(id).Symbol is ISymbol symbol) || !_bySymbol.TryGetValue(symbol, out Var v))
                            continue;
                        v.Refs.Add(id);
                        v.Positions.Add(id.SpanStart);
                        if (v.IsParam && IsWrite(id))
                            v.Written = true;
                    }
                }

                private static bool IsWrite(ExpressionSyntax e)
                {
                    SyntaxNode p = e.Parent;
                    if (p is AssignmentExpressionSyntax a)
                        return a.Left == e;
                    if (p is PrefixUnaryExpressionSyntax pre)
                        return pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression);
                    if (p is PostfixUnaryExpressionSyntax)
                        return true;
                    return p is ArgumentSyntax arg && !arg.RefKindKeyword.IsKind(SyntaxKind.None);
                }

                /// <summary>頭で宣言する変数と同じ名前が、メソッドの中にほかにもあれば、名前を変える（入れ子のスコープの同じ名前とぶつかるため）</summary>
                private void RenameDuplicates()
                {
                    var names = _co.Declaration.ParameterList.Parameters.Select(p => p.Identifier.ValueText)
                        .Concat(_body.DescendantNodes().Select(DeclaredName).Where(n => n != null))
                        .GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
                    int k = 0;
                    foreach (Var v in _vars.Where(v => !v.IsParam))
                    {
                        if (!names.TryGetValue(v.Name, out int count) || count < 2)
                            continue;
                        v.Name = $"{v.Name}__{k++}";
                        foreach (IdentifierNameSyntax id in v.Refs)
                            _edits.Add(id.Span, v.Name);
                    }
                }

                private static string DeclaredName(SyntaxNode n)
                {
                    switch (n)
                    {
                        case VariableDeclaratorSyntax v: return v.Identifier.ValueText;
                        case ForEachStatementSyntax e: return e.Identifier.ValueText;
                        case SingleVariableDesignationSyntax d: return d.Identifier.ValueText;
                        case CatchDeclarationSyntax c: return c.Identifier.ValueText;
                        default: return null;
                    }
                }

                /// <summary>pos の yield で、状態に書いておく変数</summary>
                private List<Var> Saved(int pos)
                {
                    var saved = new List<Var>();
                    foreach (Var v in _vars)
                    {
                        if (v.IsParam && !v.Written)
                            continue;
                        // 宣言より前の yield では書かない（再開したあと、使う前に必ず宣言（代入）を通る。C# のスコープと、代入してから読む決まりのため）
                        if (!v.IsParam && v.DeclPos > pos)
                            continue;
                        bool later = v.Positions.Any(r => r > pos)
                                     || _loops.Any(l => l.Start <= pos && pos < l.End && v.DeclPos < l.Start && v.Positions.Any(r => r >= l.Start && r < l.End));
                        if (!later)
                            continue;
                        if (v.Slot < 0)
                            v.Slot = _nextSlot++;
                        saved.Add(v);
                    }
                    return saved;
                }

                // ---- 書き換え ------------------------------------------------------

                private void List(IEnumerable<StatementSyntax> statements)
                {
                    foreach (StatementSyntax s in statements)
                        Statement(s);
                }

                private void Statement(StatementSyntax s)
                {
                    if (!_flat.Contains(s))
                    {
                        if (s is LocalDeclarationStatementSyntax d && !d.IsConst)
                            Hoist(d);
                        return;
                    }
                    switch (s)
                    {
                        case YieldStatementSyntax y:
                            Yield(y);
                            break;
                        case BlockSyntax b:
                            _edits.Add(b.OpenBraceToken.Span, "");
                            List(b.Statements);
                            _edits.Add(b.CloseBraceToken.Span, "");
                            break;
                        case IfStatementSyntax i:
                            If(i);
                            break;
                        case WhileStatementSyntax w:
                            While(w);
                            break;
                        case DoStatementSyntax o:
                            Do(o);
                            break;
                        case ForStatementSyntax f:
                            For(f);
                            break;
                        case ForEachStatementSyntax e:
                            ForEach(e);
                            break;
                        case SwitchStatementSyntax sw:
                            Switch(sw);
                            break;
                        default:
                            _o.Error(s, Loc.T("この文（try・using・lock・ラベルなど）の中の yield return は未対応です", "yield return inside this statement (try, using, lock, labels and similar) is not supported"));
                            break;
                    }
                }

                /// <summary>ローカル変数の宣言 → 代入（宣言はメソッドの頭）</summary>
                private void Hoist(LocalDeclarationStatementSyntax d)
                {
                    var code = new StringBuilder();
                    foreach (VariableDeclaratorSyntax declarator in d.Declaration.Variables)
                    {
                        if (declarator.Initializer == null || !(_model.GetDeclaredSymbol(declarator) is ILocalSymbol symbol) || !_bySymbol.TryGetValue(symbol, out Var v))
                            continue;
                        ExpressionSyntax value = declarator.Initializer.Value;
                        string text = _edits.Take(value);
                        code.Append($"{v.Name} = {(value is InitializerExpressionSyntax ? $"new {v.Type} {text}" : text)}; ");
                    }
                    string original = _edits.Take(d);
                    _edits.Add(d.Span, KeepLines(code.ToString(), original));
                }

                private void Yield(YieldStatementSyntax y)
                {
                    string wait = _o.Wait(y.Expression);
                    if (wait == null)
                        return;
                    int n = ++_label;
                    List<Var> saved = Saved(y.SpanStart);
                    string saves = string.Concat(saved.Select(v => $"__s[{v.Slot}] = {v.Name}; "));
                    string restores = string.Concat(saved.Where(v => !v.IsParam).Select(v => $" {v.Name} = ({v.Type})__s[{v.Slot}];"));
                    string original = _edits.Take(y);
                    _edits.Add(y.Span, KeepLines($"{saves}__h[2] = {n}; {wait} return; case {n}:{restores}", original));
                }

                /// <summary>頭（キーワードから閉じ括弧まで）を置き換える。中の式は先に Take で取り出しておく</summary>
                private void Head(SyntaxToken first, SyntaxToken last, string code)
                {
                    var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(first.SpanStart, last.Span.End);
                    string original = _edits.Take(span);
                    _edits.Add(span, KeepLines(code, original));
                }

                private void End(StatementSyntax s, string code) => _edits.Add(new Microsoft.CodeAnalysis.Text.TextSpan(s.Span.End, 0), " " + code);

                private void If(IfStatementSyntax s)
                {
                    int otherwise = ++_label;
                    int end = s.Else != null ? ++_label : otherwise;
                    string condition = _edits.Take(s.Condition);
                    Head(s.IfKeyword, s.CloseParenToken, $"if (!({condition})) {Jump(otherwise)}");
                    Statement(s.Statement);
                    if (s.Else != null)
                    {
                        _edits.Add(s.Else.ElseKeyword.Span, $"__pc = {end}; continue; case {otherwise}:");
                        Statement(s.Else.Statement);
                    }
                    End(s, Label(end));
                }

                private void While(WhileStatementSyntax s)
                {
                    int top = ++_label, end = ++_label;
                    string condition = _edits.Take(s.Condition);
                    Head(s.WhileKeyword, s.CloseParenToken, $"{Label(top)} if (!({condition})) {Jump(end)}");
                    Jumps(s, end, top);
                    Statement(s.Statement);
                    End(s, $"__pc = {top}; continue; case {end}:");
                }

                private void Do(DoStatementSyntax s)
                {
                    int top = ++_label, check = ++_label, end = ++_label;
                    _edits.Add(s.DoKeyword.Span, Label(top));
                    Jumps(s, end, check);
                    Statement(s.Statement);
                    string condition = _edits.Take(s.Condition);
                    Head(s.WhileKeyword, s.SemicolonToken, $"{Label(check)} if ({condition}) {Jump(top)} {Label(end)}");
                }

                private void For(ForStatementSyntax s)
                {
                    int top = ++_label, next = ++_label, end = ++_label;
                    var init = new StringBuilder();
                    if (s.Declaration != null)
                        foreach (VariableDeclaratorSyntax declarator in s.Declaration.Variables)
                            if (declarator.Initializer != null && _model.GetDeclaredSymbol(declarator) is ILocalSymbol symbol && _bySymbol.TryGetValue(symbol, out Var v))
                                init.Append($"{v.Name} = {_edits.Take(declarator.Initializer.Value)}; ");
                    foreach (ExpressionSyntax e in s.Initializers)
                        init.Append(_edits.Take(e)).Append("; ");
                    string condition = s.Condition != null ? $" if (!({_edits.Take(s.Condition)})) {Jump(end)}" : "";
                    string increments = string.Concat(s.Incrementors.Select(e => _edits.Take(e) + "; "));
                    Head(s.ForKeyword, s.CloseParenToken, $"{init}{Label(top)}{condition}");
                    Jumps(s, end, next);
                    Statement(s.Statement);
                    End(s, $"{Label(next)} {increments}__pc = {top}; continue; case {end}:");
                }

                private void ForEach(ForEachStatementSyntax s)
                {
                    ITypeSymbol type = _model.GetTypeInfo(s.Expression).Type;
                    if (!(type is IArrayTypeSymbol) && type?.SpecialType != SpecialType.System_String)
                    {
                        _o.Error(s.Expression, Loc.T("yield return を含む foreach は、List・Dictionary・配列・文字列に対応しています", "A foreach containing yield return supports List, Dictionary, arrays and strings"));
                        return;
                    }
                    int top = ++_label, next = ++_label, end = ++_label;
                    Var item = _model.GetDeclaredSymbol(s) is ILocalSymbol symbol && _bySymbol.TryGetValue(symbol, out Var found) ? found : null;
                    Var array = AddTemp("fa", TypeText(type), s), index = AddTemp("fi", "int", s);
                    string source = _edits.Take(s.Expression);
                    Head(s.ForEachKeyword, s.CloseParenToken,
                        $"{array.Name} = {source}; {index.Name} = 0; {Label(top)} if (!({index.Name} < {array.Name}.Length)) {Jump(end)} {item?.Name} = ({item?.Type}){array.Name}[{index.Name}];");
                    Jumps(s, end, next);
                    Statement(s.Statement);
                    End(s, $"{Label(next)} {index.Name}++; __pc = {top}; continue; case {end}:");
                }

                private void Switch(SwitchStatementSyntax s)
                {
                    int end = ++_label;
                    var labels = s.Sections.Select(_ => ++_label).ToList();
                    var dispatch = new StringBuilder($"switch ({_edits.Take(s.Expression)}) {{ ");
                    bool hasDefault = false;
                    for (int i = 0; i < s.Sections.Count; i++)
                    {
                        SwitchSectionSyntax section = s.Sections[i];
                        foreach (SwitchLabelSyntax label in section.Labels)
                        {
                            // パターンのラベル（when・var x など）は、switch の段（#198）が番号の case に書き換えてあるので、ここには来ない
                            if (label.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Any())
                                _o.Error(label, Loc.T("yield return を含む switch の、変数を宣言する case は未対応です", "A case that declares a variable is not supported in a switch containing yield return"));
                            hasDefault |= label is DefaultSwitchLabelSyntax;
                            dispatch.Append(_edits.Take(label)).Append(' ');
                        }
                        dispatch.Append($"__pc = {labels[i]}; continue; ");
                        var labelSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(section.Labels.First().SpanStart, section.Labels.Last().Span.End);
                        string original = _edits.Take(labelSpan);
                        _edits.Add(labelSpan, KeepLines(Label(labels[i]), original));
                    }
                    dispatch.Append("} ").Append(hasDefault ? "" : $"__pc = {end}; continue;");
                    Head(s.SwitchKeyword, s.OpenBraceToken, dispatch.ToString());
                    Jumps(s, end, -1);
                    foreach (SwitchSectionSyntax section in s.Sections)
                        List(section.Statements);
                    _edits.Add(s.CloseBraceToken.Span, Label(end));
                }

                /// <summary>target（置き換えたループ・switch）を抜ける break・continue を、位置へ進むものにする</summary>
                private void Jumps(StatementSyntax target, int breakLabel, int continueLabel)
                {
                    foreach (StatementSyntax jump in target.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax || n is LocalFunctionStatementSyntax))
                                 .OfType<StatementSyntax>().Where(n => n is BreakStatementSyntax || n is ContinueStatementSyntax))
                    {
                        if (TargetOf(jump) != target)
                            continue;
                        int label = jump is BreakStatementSyntax ? breakLabel : continueLabel;
                        _edits.Add(jump.Span, Jump(label));
                    }
                }

                private static SyntaxNode TargetOf(StatementSyntax jump)
                {
                    for (SyntaxNode p = jump.Parent; p != null; p = p.Parent)
                    {
                        if (p is AnonymousFunctionExpressionSyntax || p is LocalFunctionStatementSyntax || p is MemberDeclarationSyntax)
                            return null;
                        if (p is WhileStatementSyntax || p is DoStatementSyntax || p is ForStatementSyntax || p is CommonForEachStatementSyntax)
                            return p;
                        if (p is SwitchStatementSyntax && jump is BreakStatementSyntax)
                            return p;
                    }
                    return null;
                }
            }
        }
    }
}
