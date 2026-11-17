using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// ユーザー定義の演算子・変換（Redmine #105）。UdonSharp は演算子の宣言に対応しておらず、UdonSharpBehaviour に宣言すると
    /// それだけで内部の例外になる。宣言を名前の付いた普通の static メソッドに、使う側の式をその呼び出しに書き換える。
    ///
    ///   public static Vec operator +(Vec a, Vec b) { ... }     →  public static Vec op_Addition__Vec__Vec(Vec a, Vec b) { ... }
    ///   public static implicit operator Vec(int v) => ...;    →  public static Vec op_Implicit__int__to_Vec(int v) => ...;
    ///   var c = a + b;  Vec d = 3;  a += b;  ++a;             →  var c = Vec.op_Addition__Vec__Vec(a, b);  Vec d = Vec.op_Implicit__int__to_Vec(3);  ...
    ///
    /// - 普通のクラスでは、書き換えたメソッドは、ほかの static メソッドと同じく、使う UdonSharpBehaviour にコピーされる（ClassLowering）
    /// - UdonSharpBehaviour では、別の UdonSharpBehaviour の static メソッドの呼び出しになる（U# で使える）
    /// - 名前は __ で始めない（__ で始まる生成メソッドは、そのクラスの中からだけ呼ぶものとして、#111 の段で名前を変えることがあるため）
    /// - ジェネリクスのコピー・タプルの段のあとに置く（コピーの中の演算子と、タプルの == が生成する要素の == も書き換えるため）
    /// - 行は増やさない（宣言の頭と、使う側の式を、同じ行のまま置き換える）
    /// </summary>
    internal static class OperatorLowering
    {
        public static bool MayHaveOperators(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is OperatorDeclarationSyntax || n is ConversionOperatorDeclarationSyntax));

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_OperatorPrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // ソースで宣言した演算子と、書き換えたメソッドの名前
            var names = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (BaseMethodDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                {
                    if (!(decl is OperatorDeclarationSyntax || decl is ConversionOperatorDeclarationSyntax)
                        || !(model.GetDeclaredSymbol(decl) is IMethodSymbol symbol))
                        continue;
                    Validate(symbol, decl, diagnostics);
                    string name = MethodName(symbol);
                    if (symbol.ContainingType.GetMembers(name).Any())
                        diagnostics.Error(OperatorToken(decl).GetLocation(),
                            Loc.T($"'{symbol.ContainingType.Name}' : メンバーの名前 {name} は、この演算子の書き換えに使うので、ほかの名前にしてください", $"'{symbol.ContainingType.Name}' : The name {name} is reserved for the method generated for this operator, so use a different name"));
                    names[symbol] = name;
                }

                // 演算子・変換の候補が曖昧なもの。普通のクラスは object[] に書き換えるので、U# のコンパイルでは型が合ってしまい、エラーにならない
                foreach (Diagnostic d in model.GetDiagnostics())
                    if (d.Severity == DiagnosticSeverity.Error && AmbiguousOperatorErrors.Contains(d.Id))
                        diagnostics.Error(d.Location, $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                var rewriter = new Rewriter(compilation.GetSemanticModel(tree), names, diagnostics);
                SyntaxNode root = rewriter.Visit(tree.GetRoot());
                result.Add(rewriter.Changed
                    ? CSharpSyntaxTree.ParseText(root.ToFullString(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }
            return result;
        }

        /// <summary>演算子・変換が曖昧（CS0034 2 項、CS0035 単項、CS0457 変換）</summary>
        private static readonly HashSet<string> AmbiguousOperatorErrors = new HashSet<string> { "CS0034", "CS0035", "CS0457" };

        // ---- 宣言 ---------------------------------------------------------------

        private static void Validate(IMethodSymbol symbol, BaseMethodDeclarationSyntax decl, InterfaceLowering.LoweringDiagnostics diagnostics)
        {
            INamedTypeSymbol type = symbol.ContainingType;
            Location at = OperatorToken(decl).GetLocation();
            switch (symbol.Name)
            {
                case WellKnownMemberNames.TrueOperatorName:
                case WellKnownMemberNames.FalseOperatorName:
                    // 普通のクラスのエラーは、UdonSharpBehaviour から使われるときだけ残す（ClassLowering の検査と同じ）
                    diagnostics.Error(at, Loc.T($"'{type.Name}' : true / false 演算子は未対応です。bool を返すメソッドかプロパティにしてください", $"'{type.Name}' : true / false operators are not supported. Use a method or property that returns bool"),
                        InterfaceLowering.IsUdonSharpBehaviour(type) ? null : ClassLowering.TagOf(type.OriginalDefinition));
                    return;
            }
            if (!InterfaceLowering.IsUdonSharpBehaviour(type))
                return;
            if (symbol.Name == WellKnownMemberNames.EqualityOperatorName || symbol.Name == WellKnownMemberNames.InequalityOperatorName)
                diagnostics.Error(at, Loc.T($"'{type.Name}' : UdonSharpBehaviour の == / != のオーバーロードは未対応です（UnityEngine.Object の比較（破棄したものを null とみなす）を置き換えてしまうため）。比べるメソッドにしてください", $"'{type.Name}' : Overloading == / != on an UdonSharpBehaviour is not supported (it would replace the UnityEngine.Object comparison, which treats destroyed objects as null). Use a comparison method"));
            else if (symbol.MethodKind == MethodKind.Conversion && symbol.ReturnType is INamedTypeSymbol target && InterfaceLowering.IsUdonSharpBehaviour(target))
                diagnostics.Error(at, Loc.T($"'{type.Name}' : UdonSharpBehaviour の型への変換演算子は未対応です（UdonSharpBehaviour のキャストと区別できないため）。static メソッドにしてください", $"'{type.Name}' : Conversion operators to an UdonSharpBehaviour type are not supported (they cannot be told apart from UdonSharpBehaviour casts). Use a static method"));
        }

        private static SyntaxToken OperatorToken(BaseMethodDeclarationSyntax decl) =>
            decl is OperatorDeclarationSyntax o ? o.OperatorToken : ((ConversionOperatorDeclarationSyntax)decl).Type.GetFirstToken();

        /// <summary>書き換えたメソッドの名前（op_Addition__Vec__Vec、op_Implicit__int__to_Vec）。引数の型を入れて、オーバーロードを別の名前にする</summary>
        private static string MethodName(IMethodSymbol symbol)
        {
            var sb = new StringBuilder(symbol.Name);
            foreach (IParameterSymbol p in symbol.Parameters)
                sb.Append("__").Append(TypeToken(p.Type));
            if (symbol.MethodKind == MethodKind.Conversion)
                sb.Append("__to_").Append(TypeToken(symbol.ReturnType));   // 変換は、戻り値の型だけが違うものもある
            return sb.ToString();
        }

        private static string TypeToken(ITypeSymbol type) => InterfaceNaming.Sanitize(type.ToDisplayString());

        private static readonly Dictionary<string, string> Tokens = new Dictionary<string, string>
        {
            { WellKnownMemberNames.AdditionOperatorName, "+" }, { WellKnownMemberNames.SubtractionOperatorName, "-" },
            { WellKnownMemberNames.MultiplyOperatorName, "*" }, { WellKnownMemberNames.DivisionOperatorName, "/" },
            { WellKnownMemberNames.ModulusOperatorName, "%" }, { WellKnownMemberNames.BitwiseAndOperatorName, "&" },
            { WellKnownMemberNames.BitwiseOrOperatorName, "|" }, { WellKnownMemberNames.ExclusiveOrOperatorName, "^" },
            { WellKnownMemberNames.LeftShiftOperatorName, "<<" }, { WellKnownMemberNames.RightShiftOperatorName, ">>" },
            { WellKnownMemberNames.EqualityOperatorName, "==" }, { WellKnownMemberNames.InequalityOperatorName, "!=" },
            { WellKnownMemberNames.LessThanOperatorName, "<" }, { WellKnownMemberNames.GreaterThanOperatorName, ">" },
            { WellKnownMemberNames.LessThanOrEqualOperatorName, "<=" }, { WellKnownMemberNames.GreaterThanOrEqualOperatorName, ">=" },
            { WellKnownMemberNames.UnaryPlusOperatorName, "+" }, { WellKnownMemberNames.UnaryNegationOperatorName, "-" },
            { WellKnownMemberNames.LogicalNotOperatorName, "!" }, { WellKnownMemberNames.OnesComplementOperatorName, "~" },
            { WellKnownMemberNames.IncrementOperatorName, "++" }, { WellKnownMemberNames.DecrementOperatorName, "--" },
            { WellKnownMemberNames.TrueOperatorName, "true" }, { WellKnownMemberNames.FalseOperatorName, "false" },
        };

        /// <summary>
        /// 書き換えたメソッドの、エラーの補足に出す名前（「演算子 +」「変換 implicit operator int」）。演算子のメソッドでなければ null
        /// </summary>
        internal static string MemberLabel(IMethodSymbol method)
        {
            string name = method.Name;
            int cut = name.IndexOf("__", System.StringComparison.Ordinal);
            if (cut <= 0)
                return null;
            string head = name.Substring(0, cut);
            if (Tokens.TryGetValue(head, out string token))
                return Loc.T($"演算子 {token}", $"operator {token}");
            if (head == WellKnownMemberNames.ImplicitConversionName || head == WellKnownMemberNames.ExplicitConversionName)
                return Loc.T($"変換 {(head == WellKnownMemberNames.ImplicitConversionName ? "implicit" : "explicit")} operator {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}", $"conversion {(head == WellKnownMemberNames.ImplicitConversionName ? "implicit" : "explicit")} operator {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
            return null;
        }

        // ---- 書き換え -----------------------------------------------------------

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;
            private readonly Dictionary<IMethodSymbol, string> _names;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;

            public bool Changed;

            public Rewriter(SemanticModel model, Dictionary<IMethodSymbol, string> names, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _names = names;
                _diagnostics = diagnostics;
            }

            private void Error(SyntaxNode node, string message) => _diagnostics.Error(node.GetLocation(), message);

            /// <summary>node で使われるユーザー定義の演算子・変換（ソースで宣言したもの）。なければ null</summary>
            private IMethodSymbol Operator(SyntaxNode node) => Known(_model.GetSymbolInfo(node).Symbol as IMethodSymbol);

            private IMethodSymbol Known(IMethodSymbol method) =>
                method != null && (method.MethodKind == MethodKind.UserDefinedOperator || method.MethodKind == MethodKind.Conversion)
                && _names.ContainsKey(method.OriginalDefinition) ? method : null;

            private string TypeText(ITypeSymbol type, SyntaxNode at) => type.ToMinimalDisplayString(_model, at.SpanStart);

            private static string Text(SyntaxNode node) => node.WithoutTrivia().ToFullString();

            /// <summary>演算子のメソッドの呼び出し（Vec.op_Addition__Vec__Vec(a, b)）。引数は、元の trivia（改行も）のまま並べる</summary>
            private ExpressionSyntax Call(IMethodSymbol method, SyntaxNode original, params (ExpressionSyntax Argument, SyntaxToken? Separator)[] arguments)
            {
                Changed = true;
                var callee = SyntaxFactory.ParseExpression($"{TypeText(method.ContainingType, original)}.{_names[method.OriginalDefinition]}");
                var nodes = new List<SyntaxNodeOrToken>();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                    {
                        SyntaxToken? sep = arguments[i].Separator;
                        nodes.Add(sep.HasValue
                            ? SyntaxFactory.Token(sep.Value.LeadingTrivia, SyntaxKind.CommaToken, sep.Value.TrailingTrivia)
                            : SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));
                    }
                    nodes.Add(SyntaxFactory.Argument(arguments[i].Argument));
                }
                return SyntaxFactory.InvocationExpression(callee, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(nodes)));
            }

            private ExpressionSyntax Parse(string code, SyntaxNode original)
            {
                Changed = true;
                return SyntaxFactory.ParseExpression(code).WithTriviaFrom(original);
            }

            // ---- 宣言 -----------------------------------------------------------

            public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
            {
                var visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
                if (!(_model.GetDeclaredSymbol(node) is IMethodSymbol symbol) || !_names.TryGetValue(symbol, out string name))
                    return visited;
                // 「operator +」を、名前 1 つにする（operator の前の空白と、+ のうしろの trivia は残す）
                SyntaxToken id = SyntaxFactory.Identifier(visited.OperatorKeyword.LeadingTrivia, name, visited.OperatorToken.TrailingTrivia);
                return Method(visited.AttributeLists, visited.Modifiers, visited.ReturnType, id, visited.ParameterList, visited.Body, visited.ExpressionBody, visited.SemicolonToken);
            }

            public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
            {
                var visited = (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
                if (!(_model.GetDeclaredSymbol(node) is IMethodSymbol symbol) || !_names.TryGetValue(symbol, out string name))
                    return visited;
                // 「implicit operator int」を「int 名前」にする
                TypeSyntax returnType = visited.Type.WithLeadingTrivia(visited.ImplicitOrExplicitKeyword.LeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space);
                SyntaxToken id = SyntaxFactory.Identifier(SyntaxFactory.TriviaList(), name, visited.Type.GetTrailingTrivia());
                return Method(visited.AttributeLists, visited.Modifiers, returnType, id, visited.ParameterList, visited.Body, visited.ExpressionBody, visited.SemicolonToken);
            }

            private SyntaxNode Method(SyntaxList<AttributeListSyntax> attributes, SyntaxTokenList modifiers, TypeSyntax returnType, SyntaxToken id,
                ParameterListSyntax parameters, BlockSyntax body, ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon)
            {
                Changed = true;
                return SyntaxFactory.MethodDeclaration(attributes, modifiers, returnType, null, id, null, parameters,
                    SyntaxFactory.List<TypeParameterConstraintClauseSyntax>(), body, arrow, semicolon);
            }

            // ---- 使う側 ---------------------------------------------------------

            public override SyntaxNode Visit(SyntaxNode node)
            {
                SyntaxNode visited = base.Visit(node);
                // 暗黙の変換（Vec v = 3;、引数・戻り値・演算子の引数の 3 など）
                if (node is ExpressionSyntax expr && visited is ExpressionSyntax result && !(node.Parent is CastExpressionSyntax cast && cast.Expression == node && Operator(cast) != null))
                {
                    Conversion conversion = _model.GetConversion(expr);
                    if (conversion.IsUserDefined && Known(conversion.MethodSymbol) is IMethodSymbol method)
                    {
                        ITypeSymbol from = _model.GetTypeInfo(expr).Type;
                        ITypeSymbol to = _model.GetTypeInfo(expr).ConvertedType;
                        return Convert(method, result, from, to, expr).WithTriviaFrom(result);
                    }
                }
                return visited;
            }

            /// <summary>変換のメソッドの呼び出し。前後の標準の変換（int → long など）は、キャストで書く</summary>
            private ExpressionSyntax Convert(IMethodSymbol method, ExpressionSyntax operand, ITypeSymbol from, ITypeSymbol to, SyntaxNode original)
            {
                ITypeSymbol parameter = method.Parameters[0].Type;
                ExpressionSyntax argument = operand.WithoutTrivia();
                if (from != null && !SymbolEqualityComparer.Default.Equals(from, parameter))
                    argument = SyntaxFactory.ParseExpression($"({TypeText(parameter, original)})({Text(argument)})");
                ExpressionSyntax call = Call(method, original, (argument, null));
                if (to != null && !SymbolEqualityComparer.Default.Equals(to, method.ReturnType))
                    call = SyntaxFactory.ParseExpression($"({TypeText(to, original)})({call.ToFullString()})");
                return call;
            }

            private int _eachCount;

            /// <summary>
            /// foreach の要素から変数の型への変換（foreach (Vec v in ints)、foreach (int n in vecs)）。式に現れないので Visit では拾えない。
            ///   foreach (Vec v in ints) body  →  foreach (int __op_each0 in ints) { Vec v = Vec.op_Implicit__int__to_Vec(__op_each0); body }
            /// 行は増やさない（本体の前に同じ行で宣言を置き、本体のうしろの trivia を閉じ括弧のうしろに移す）
            /// </summary>
            public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            {
                ForEachStatementInfo info = _model.GetForEachStatementInfo(node);
                if (!info.ElementConversion.IsUserDefined || !(Known(info.ElementConversion.MethodSymbol) is IMethodSymbol method)
                    || !(_model.GetDeclaredSymbol(node) is ILocalSymbol local))
                    return base.VisitForEachStatement(node);

                var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
                string temp = $"__op_each{_eachCount++}";
                ExpressionSyntax value = Convert(method, SyntaxFactory.IdentifierName(temp), info.ElementType, local.Type, node);
                var declaration = SyntaxFactory.ParseStatement($"{Text(visited.Type)} {visited.Identifier.Text} = {value.ToFullString()}; ");
                StatementSyntax body = visited.Statement;
                var block = SyntaxFactory.Block(
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.List(new[] { declaration, body.WithoutTrailingTrivia() }),
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(body.GetTrailingTrivia()));
                Changed = true;
                return visited
                    .WithType(SyntaxFactory.ParseTypeName(TypeText(info.ElementType, node)).WithTriviaFrom(visited.Type))
                    .WithIdentifier(SyntaxFactory.Identifier(visited.Identifier.LeadingTrivia, temp, visited.Identifier.TrailingTrivia))
                    .WithStatement(block);
            }

            public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
            {
                if (Operator(node) is IMethodSymbol method)
                {
                    var operand = (ExpressionSyntax)Visit(node.Expression);
                    return Convert(method, operand, _model.GetTypeInfo(node.Expression).Type, _model.GetTypeInfo(node).Type, node).WithTriviaFrom(node);
                }
                return base.VisitCastExpression(node);
            }

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (!(Operator(node) is IMethodSymbol method))
                    return base.VisitBinaryExpression(node);
                if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    _diagnostics.Error(node.OperatorToken.GetLocation(), Loc.T($"ユーザー定義の演算子を使う {node.OperatorToken.Text} は未対応です。& / | にするか、bool にしてから比べてください", $"{node.OperatorToken.Text} with user-defined operators is not supported. Use & / |, or convert to bool before comparing"));
                    return node;
                }
                var left = (ExpressionSyntax)Visit(node.Left);
                var right = (ExpressionSyntax)Visit(node.Right);
                return Call(method, node, (left.WithoutLeadingTrivia(), null), (right.WithoutTrailingTrivia(), node.OperatorToken)).WithTriviaFrom(node);
            }

            public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            {
                if (!(Operator(node) is IMethodSymbol method))
                    return base.VisitPrefixUnaryExpression(node);
                if (node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression))
                    return Increment(node, node.Operand, method, prefix: true);
                var operand = (ExpressionSyntax)Visit(node.Operand);
                return Call(method, node, (operand.WithoutTrivia(), null)).WithTriviaFrom(node);
            }

            public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
            {
                if (Operator(node) is IMethodSymbol method && (node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)))
                    return Increment(node, node.Operand, method, prefix: false);
                return base.VisitPostfixUnaryExpression(node);
            }

            /// <summary>++x・x++ → x = Vec.op_Increment__Vec(x)。後置で値を使うもの（y = x++）は、前の値を残せないのでエラー</summary>
            private SyntaxNode Increment(ExpressionSyntax node, ExpressionSyntax operand, IMethodSymbol method, bool prefix)
            {
                bool statement = InterfaceLowering.IsStatementExpression(node, _model);
                string op = method.Name == WellKnownMemberNames.IncrementOperatorName ? "++" : "--";
                if (!prefix && !statement)
                {
                    Error(node, Loc.T($"ユーザー定義の {op} を後置（x{op}）で書いて値を使うのは未対応です。前置（{op}x）にするか、文にしてください", $"Using the value of a user-defined {op} written as postfix (x{op}) is not supported. Use prefix ({op}x), or write it as a statement"));
                    return node;
                }
                if (!IsRepeatable(operand))
                {
                    Error(node, Loc.T($"ユーザー定義の {op} の受け側に、メソッドの呼び出しなどを書くのは未対応です。変数に入れてから使ってください", $"Writing a method call or similar as the target of a user-defined {op} is not supported. Store it in a variable first"));
                    return node;
                }
                string target = Text(Visit(operand));
                string code = $"{target} = {Call(method, node, (SyntaxFactory.ParseExpression(target), null)).ToFullString()}";
                return Parse(statement ? code : $"({code})", node);
            }

            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.SimpleAssignmentExpression) || node.IsKind(SyntaxKind.CoalesceAssignmentExpression) || !(Operator(node) is IMethodSymbol method))
                    return base.VisitAssignmentExpression(node);
                // a += b → a = Vec.op_Addition__Vec__Vec(a, b)
                if (!IsRepeatable(node.Left))
                {
                    Error(node, Loc.T($"ユーザー定義の演算子を使う {node.OperatorToken.Text} の受け側に、メソッドの呼び出しなどを書くのは未対応です。変数に入れてから使ってください", $"Writing a method call or similar as the target of {node.OperatorToken.Text} with a user-defined operator is not supported. Store it in a variable first"));
                    return node;
                }
                string target = Text(Visit(node.Left));
                var right = (ExpressionSyntax)Visit(node.Right);
                string call = Call(method, node, (SyntaxFactory.ParseExpression(target), null), (right.WithoutTrivia(), null)).ToFullString();
                ITypeSymbol leftType = _model.GetTypeInfo(node.Left).Type;
                if (leftType != null && !SymbolEqualityComparer.Default.Equals(leftType, method.ReturnType))
                    call = $"({TypeText(leftType, node)})({call})";
                string code = $"{target} = {call}";
                return Parse(InterfaceLowering.IsStatementExpression(node, _model) ? code : $"({code})", node);
            }

            /// <summary>
            /// 2 回書いても、評価が 1 回のときと同じ受け側（変数・引数・フィールド・プロパティ、その要素で添字が変数か定数のもの）
            /// </summary>
            private bool IsRepeatable(ExpressionSyntax e)
            {
                switch (e)
                {
                    case IdentifierNameSyntax _:
                    case ThisExpressionSyntax _:
                    case LiteralExpressionSyntax _:
                        return true;
                    case ParenthesizedExpressionSyntax p:
                        return IsRepeatable(p.Expression);
                    case MemberAccessExpressionSyntax m:
                        return _model.GetSymbolInfo(m.Expression).Symbol is INamedTypeSymbol || IsRepeatable(m.Expression);
                    case ElementAccessExpressionSyntax a:
                        return IsRepeatable(a.Expression) && a.ArgumentList.Arguments.All(arg => IsRepeatable(arg.Expression));
                    default:
                        return false;
                }
            }
        }
    }
}
