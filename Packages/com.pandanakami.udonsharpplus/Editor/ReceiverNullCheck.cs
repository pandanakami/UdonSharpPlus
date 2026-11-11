using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// 普通のクラスのインスタンスメンバーを null に対して呼んだときに、呼び出し側で止める必要があるかを調べる（#104）。
    ///
    /// C# では、null に対するインスタンスメソッドの呼び出しは、呼び出しの時点で NullReferenceException になる。
    /// 書き換え後は、メソッドは object[] を受け取る static なコピーなので、null のまま中に入る。
    /// 最初に必ず this のフィールドを読むメソッドなら、そこで例外になって止まるが、そうでないメソッドは
    /// this を使わないまま進んだり、ほかの処理をしてから止まったりする。
    ///
    /// そうしたメンバーだけ、呼び出し側で受け側の null を調べる（呼び出した行で止まるようにする）。
    /// 判定は保守的にする：「ほかの処理より前に、必ず this のフィールドを読む」と言えるときだけ確認を省く。
    /// </summary>
    internal sealed class ReceiverNullCheck
    {
        private enum Result
        {
            Continue,   // まだ分からない（副作用のない式だけ評価した）
            Found,      // this のフィールドを読む（書く）ので、null ならここで止まる
            Blocked,    // this を使う前に、呼び出し・分岐・return などがある
        }

        private readonly Compilation _compilation;
        private readonly Dictionary<IMethodSymbol, bool> _cache = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

        public ReceiverNullCheck(Compilation compilation)
        {
            _compilation = compilation;
        }

        /// <summary>このメソッド（アクセサー）を呼ぶ前に、受け側の null を調べる必要があるか</summary>
        public bool Needs(IMethodSymbol method)
        {
            if (method == null || method.IsStatic)
                return false;
            method = method.OriginalDefinition;
            if (_cache.TryGetValue(method, out bool needs))
                return needs;

            needs = Analyze(method) != Result.Found;
            _cache[method] = needs;
            return needs;
        }

        private Result Analyze(IMethodSymbol method)
        {
            SyntaxReference reference = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference == null)
                return Result.Blocked;

            SyntaxNode syntax = reference.GetSyntax();
            var walker = new Walker(_compilation.GetSemanticModel(syntax.SyntaxTree), method.ContainingType);
            switch (syntax)
            {
                case BaseMethodDeclarationSyntax m:
                    return walker.Body(m.Body, m.ExpressionBody);
                case AccessorDeclarationSyntax a:
                    return walker.Body(a.Body, a.ExpressionBody);
                case ArrowExpressionClauseSyntax arrow:     // int P => ...; のゲッター
                    return walker.Body(null, arrow);
                default:
                    return Result.Blocked;
            }
        }

        private sealed class Walker
        {
            private readonly SemanticModel _model;
            private readonly INamedTypeSymbol _type;

            public Walker(SemanticModel model, INamedTypeSymbol type)
            {
                _model = model;
                _type = type;
            }

            public Result Body(BlockSyntax block, ArrowExpressionClauseSyntax arrow)
            {
                Result r = block != null ? Statements(block.Statements) : arrow != null ? Expr(arrow.Expression) : Result.Continue;
                return r == Result.Continue ? Result.Blocked : r;   // 最後まで this を使わない
            }

            private Result Statements(IEnumerable<StatementSyntax> statements)
            {
                foreach (StatementSyntax s in statements)
                {
                    Result r = Statement(s);
                    if (r != Result.Continue)
                        return r;
                }
                return Result.Continue;
            }

            private Result Statement(StatementSyntax s)
            {
                switch (s)
                {
                    case BlockSyntax b:
                        return Statements(b.Statements);
                    case EmptyStatementSyntax _:
                        return Result.Continue;
                    case LocalDeclarationStatementSyntax d:
                        return Sequence(d.Declaration.Variables.Select(v => v.Initializer?.Value));
                    case ExpressionStatementSyntax e:
                        return Expr(e.Expression);
                    // 以下は、最初に評価する式で決まらなければ、this を使わずに抜けることがある
                    case ReturnStatementSyntax r:
                        return Stop(Expr(r.Expression));
                    case IfStatementSyntax i:
                        return Stop(Expr(i.Condition));
                    case SwitchStatementSyntax sw:
                        return Stop(Expr(sw.Expression));
                    case WhileStatementSyntax w:
                        return Stop(Expr(w.Condition));
                    case ForEachStatementSyntax f:
                        return Stop(Expr(f.Expression));
                    default:
                        return Result.Blocked;
                }
            }

            private static Result Stop(Result r) => r == Result.Continue ? Result.Blocked : r;

            private Result Sequence(IEnumerable<ExpressionSyntax> expressions)
            {
                foreach (ExpressionSyntax e in expressions)
                {
                    Result r = Expr(e);
                    if (r != Result.Continue)
                        return r;
                }
                return Result.Continue;
            }

            /// <summary>this のフィールド（自動実装プロパティ）を指す式か（v、this.v）</summary>
            private bool IsThisSlot(ExpressionSyntax e)
            {
                if (e is MemberAccessExpressionSyntax access)
                {
                    if (!(access.Expression is ThisExpressionSyntax) && !(access.Expression is BaseExpressionSyntax))
                        return false;
                }
                else if (!(e is IdentifierNameSyntax))
                    return false;

                ISymbol symbol = _model.GetSymbolInfo(e).Symbol;
                if (symbol == null || symbol.IsStatic)
                    return false;
                bool slot = symbol is IFieldSymbol f && !f.IsConst || symbol is IPropertySymbol p && IsAuto(p);
                return slot && IsOwnMember(symbol);
            }

            private static bool IsAuto(IPropertySymbol p) =>
                p.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, p));

            private bool IsOwnMember(ISymbol symbol)
            {
                for (INamedTypeSymbol t = _type; t != null; t = t.BaseType)
                    if (SymbolEqualityComparer.Default.Equals(t, symbol.ContainingType.OriginalDefinition))
                        return true;
                return false;
            }

            private Result Expr(ExpressionSyntax e)
            {
                switch (e)
                {
                    case null:
                        return Result.Continue;

                    case LiteralExpressionSyntax _:
                    case DefaultExpressionSyntax _:
                    case TypeOfExpressionSyntax _:
                    case ThisExpressionSyntax _:     // this を値として渡すだけでは止まらない
                    case PredefinedTypeSyntax _:
                        return Result.Continue;

                    case IdentifierNameSyntax _:
                    case MemberAccessExpressionSyntax _ when IsThisSlot(e):
                        return IsThisSlot(e) ? Result.Found : Value(e);

                    case MemberAccessExpressionSyntax m:
                    {
                        if (!(_model.GetSymbolInfo(m.Expression).Symbol is ITypeSymbol) && !(_model.GetSymbolInfo(m.Expression).Symbol is INamespaceSymbol))
                        {
                            Result r = Expr(m.Expression);
                            if (r != Result.Continue)
                                return r;
                        }
                        return Value(m);
                    }

                    case ParenthesizedExpressionSyntax p:
                        return Expr(p.Expression);
                    case CastExpressionSyntax c:
                        return Expr(c.Expression);
                    case CheckedExpressionSyntax ch:
                        return Expr(ch.Expression);

                    case PrefixUnaryExpressionSyntax pre:
                        return Unary(pre.Kind(), pre.Operand);
                    case PostfixUnaryExpressionSyntax post:
                        return Unary(post.Kind(), post.Operand);

                    case BinaryExpressionSyntax b:
                    {
                        Result left = Expr(b.Left);
                        if (left != Result.Continue)
                            return left;
                        if (b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression)
                            || b.IsKind(SyntaxKind.CoalesceExpression))
                            return Result.Blocked;   // 右は評価されないことがある
                        if (b.IsKind(SyntaxKind.IsExpression) || b.IsKind(SyntaxKind.AsExpression))
                            return Result.Continue;
                        return Expr(b.Right);
                    }

                    case ConditionalExpressionSyntax c:
                        return Stop(Expr(c.Condition));

                    case AssignmentExpressionSyntax a:
                        return Assignment(a);

                    case InvocationExpressionSyntax inv:
                    {
                        if (inv.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof")
                            return Result.Continue;
                        if (inv.Expression is MemberAccessExpressionSyntax target
                            && !(_model.GetSymbolInfo(target.Expression).Symbol is ITypeSymbol))
                        {
                            Result r = Expr(target.Expression);
                            if (r != Result.Continue)
                                return r;
                        }
                        Result args = Sequence(inv.ArgumentList.Arguments.Select(a => a.Expression));
                        return args != Result.Continue ? args : Result.Blocked;   // 呼び出しそのものは、ほかの処理
                    }

                    case ElementAccessExpressionSyntax el:
                    {
                        Result r = Expr(el.Expression);
                        if (r != Result.Continue)
                            return r;
                        r = Sequence(el.ArgumentList.Arguments.Select(a => a.Expression));
                        if (r != Result.Continue)
                            return r;
                        // 配列の読み出しは副作用がない。インデクサーは呼び出し
                        return _model.GetSymbolInfo(el).Symbol is IPropertySymbol ? Result.Blocked : Result.Continue;
                    }

                    case InterpolatedStringExpressionSyntax s:
                        return Sequence(s.Contents.OfType<InterpolationSyntax>().Select(i => i.Expression));

                    case ArrayCreationExpressionSyntax arr:
                    {
                        Result r = Sequence(arr.Type.RankSpecifiers.SelectMany(rs => rs.Sizes));
                        if (r != Result.Continue || arr.Initializer == null)
                            return r;
                        return Sequence(arr.Initializer.Expressions);
                    }

                    case BaseObjectCreationExpressionSyntax create:
                    {
                        // 引数を評価してから、コンストラクタ（ほかの処理）を呼ぶ
                        Result r = create.ArgumentList == null ? Result.Continue : Sequence(create.ArgumentList.Arguments.Select(a => a.Expression));
                        return r != Result.Continue ? r : Result.Blocked;
                    }

                    default:
                        return Result.Blocked;   // ラムダなど
                }
            }

            /// <summary>this のフィールド以外を指す名前・メンバーの読み出し</summary>
            private Result Value(ExpressionSyntax e)
            {
                switch (_model.GetSymbolInfo(e).Symbol)
                {
                    case ILocalSymbol _:
                    case IParameterSymbol _:
                    case IFieldSymbol _:      // ほかのオブジェクトのフィールド・static・const の読み出しは、副作用がない
                    case ITypeSymbol _:
                    case INamespaceSymbol _:
                        return Result.Continue;
                    case IPropertySymbol p when p.ContainingType?.SpecialType == SpecialType.System_Array
                                             || p.ContainingType?.SpecialType == SpecialType.System_String:
                        return Result.Continue;   // Length など
                    default:
                        return Result.Blocked;    // プロパティ（ゲッターの呼び出し）など
                }
            }

            private Result Unary(SyntaxKind kind, ExpressionSyntax operand)
            {
                switch (kind)
                {
                    case SyntaxKind.PreIncrementExpression:
                    case SyntaxKind.PreDecrementExpression:
                    case SyntaxKind.PostIncrementExpression:
                    case SyntaxKind.PostDecrementExpression:
                        if (IsThisSlot(operand))
                            return Result.Found;
                        return IsLocal(operand) ? Result.Continue : Result.Blocked;
                    default:
                        return Expr(operand);
                }
            }

            private Result Assignment(AssignmentExpressionSyntax a)
            {
                bool compound = !a.IsKind(SyntaxKind.SimpleAssignmentExpression);
                if (IsThisSlot(a.Left))
                {
                    if (compound)
                        return Result.Found;   // 先に読む
                    Result right = Expr(a.Right);
                    return right == Result.Continue ? Result.Found : right;   // 右辺のあと、書き込みで止まる
                }

                // 左辺の受け側（hits[0] = ... の hits、a.b = ... の a）が先に評価される
                Result r = Target(a.Left);
                if (r != Result.Continue)
                    return r;
                r = Expr(a.Right);
                if (r != Result.Continue)
                    return r;
                return IsLocal(a.Left) ? Result.Continue : Result.Blocked;   // ローカル変数への代入は、外から見えない
            }

            /// <summary>代入先の、書き込みの前に評価される部分</summary>
            private Result Target(ExpressionSyntax left)
            {
                switch (left)
                {
                    case ElementAccessExpressionSyntax el:
                    {
                        Result r = Expr(el.Expression);
                        return r != Result.Continue ? r : Sequence(el.ArgumentList.Arguments.Select(x => x.Expression));
                    }
                    case MemberAccessExpressionSyntax m when !(_model.GetSymbolInfo(m.Expression).Symbol is ITypeSymbol):
                        return Expr(m.Expression);
                    default:
                        return Result.Continue;
                }
            }

            private bool IsLocal(ExpressionSyntax e) =>
                e is IdentifierNameSyntax && _model.GetSymbolInfo(e).Symbol is ISymbol s
                && (s is ILocalSymbol || s is IParameterSymbol p && p.RefKind == RefKind.None);
        }
    }
}
