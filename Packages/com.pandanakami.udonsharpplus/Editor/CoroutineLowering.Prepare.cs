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
        private const string WaitForSecondsRealtimeName = "UnityEngine.WaitForSecondsRealtime";
        private const string WaitUntilName = "UnityEngine.WaitUntil";
        private const string WaitWhileName = "UnityEngine.WaitWhile";

        /// <summary>
        /// コルーチンの下ごしらえ（#107）。ラムダを使う待ち方は、デリゲートの段がラムダを書き換える前に、ふつうの文にしておく。
        ///
        ///   yield return new WaitUntil(() => c);          → if (c) yield return null; else do yield return null; while (!(c));
        ///   yield return new WaitWhile(() => c);          → if (!(c)) yield return null; else do yield return null; while (c);
        ///   yield return new WaitForSecondsRealtime(t);   → { float __rt0 = 今 + (t); if (!(今 &lt; __rt0)) yield return null; else do yield return null; while (今 &lt; __rt0); }
        ///   WaitForSeconds w = new WaitForSeconds(t);     → float w = (float)(t);     yield return w;  → yield return new UnityEngine.WaitForSeconds(w);
        ///
        /// 普通の Unity と同じく、条件は yield のところで 1 回調べ、成り立っていても次のフレームまで待つ。成り立っていなければ、
        /// 次のフレームから毎フレーム調べる（Unity で測った結果。#107）
        /// </summary>
        public static List<SyntaxTree> Prepare(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            CheckUnsupported(trees, diagnostics, references);
            if (!trees.Any(t => t.GetRoot().DescendantNodes().Any(n => n is IdentifierNameSyntax id && IsWaitName(id.Identifier.ValueText))))
                return trees;
            var compilation = Compilations.For(trees, references);
            var result = new List<SyntaxTree>(trees.Count);
            int temp = 0;
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                Edits edits = null;
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(tree.GetRoot()))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    edits = edits ?? new Edits(tree.GetText());
                    new Preparer(model, cls, edits, diagnostics).Run(ref temp);
                }
                result.Add(edits != null && edits.Any
                    ? CSharpSyntaxTree.ParseText(edits.Apply(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }
            return result;
        }

        private static bool IsWaitName(string name) =>
            name == "WaitUntil" || name == "WaitWhile" || name == "WaitForSecondsRealtime" || name == "WaitForSeconds";

        private sealed class Preparer
        {
            private const string Now = "UnityEngine.Time.realtimeSinceStartup";

            private readonly SemanticModel _model;
            private readonly ClassDeclarationSyntax _cls;
            private readonly Edits _edits;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;

            public Preparer(SemanticModel model, ClassDeclarationSyntax cls, Edits edits, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _cls = cls;
                _edits = edits;
                _diagnostics = diagnostics;
            }

            private string TypeName(SyntaxNode node) => _model.GetTypeInfo(node).Type?.ToDisplayString();

            public void Run(ref int temp)
            {
                var yields = new HashSet<ExpressionSyntax>();
                foreach (SyntaxNode node in OwnNodes(_cls))
                {
                    // WaitForSeconds・WaitForSecondsRealtime の型 → float（秒）
                    if (node is TypeSyntax type && type is NameSyntax && !(type.Parent is QualifiedNameSyntax) && !(type.Parent is MemberAccessExpressionSyntax)
                        && !(type.Parent is ObjectCreationExpressionSyntax))
                    {
                        string name = (_model.GetSymbolInfo(type).Symbol as INamedTypeSymbol)?.ToDisplayString();
                        if (name == WaitForSecondsName || name == WaitForSecondsRealtimeName)
                            _edits.Add(type.Span, "float");
                        else if (name == WaitUntilName || name == WaitWhileName)
                            Error(type, Loc.T("WaitUntil・WaitWhile を変数に入れて使うのは未対応です。yield return new WaitUntil(() => 条件) の形で書いてください", "Storing WaitUntil / WaitWhile in a variable is not supported. Write yield return new WaitUntil(() => condition)"));
                    }
                }

                foreach (MethodDeclarationSyntax m in _cls.Members.OfType<MethodDeclarationSyntax>().Where(m => IsCoroutine(_model, m)))
                    foreach (YieldStatementSyntax y in Yields(m.Body).Where(y => y.IsKind(SyntaxKind.YieldReturnStatement)))
                    {
                        string code = Yield(y.Expression, ref temp);
                        if (code == null)
                            continue;
                        yields.Add(y.Expression);
                        string original = _edits.Take(y);
                        _edits.Add(y.Span, KeepLines(code, original));
                    }

                // yield return の外の new WaitForSeconds(t) → (float)(t)（変数に入れて使い回すもの）
                foreach (ObjectCreationExpressionSyntax create in OwnNodes(_cls).OfType<ObjectCreationExpressionSyntax>())
                {
                    string name = TypeName(create);
                    if ((name == WaitForSecondsName || name == WaitForSecondsRealtimeName) && !yields.Contains(create) && create.ArgumentList?.Arguments.Count == 1
                        && !(create.Parent is YieldStatementSyntax))
                        _edits.Add(create.Span, $"(float)({_edits.Take(create.ArgumentList.Arguments[0].Expression)})");
                }
            }

            private void Error(SyntaxNode at, string message) => _diagnostics.Error(at.GetLocation(), message);

            /// <summary>yield return の書き換え（書き換えないものは null）</summary>
            private string Yield(ExpressionSyntax value, ref int temp)
            {
                string type = TypeName(value);
                var create = value as ObjectCreationExpressionSyntax;
                if (type == WaitUntilName || type == WaitWhileName)
                {
                    string condition = create?.ArgumentList?.Arguments.Count == 1 ? Condition(create.ArgumentList.Arguments[0].Expression) : null;
                    if (condition == null)
                    {
                        if (create == null)
                            Error(value, Loc.T("WaitUntil・WaitWhile は yield return new WaitUntil(() => 条件) の形で書いてください", "Write WaitUntil / WaitWhile as yield return new WaitUntil(() => condition)"));
                        return null;
                    }
                    return type == WaitUntilName
                        ? $"if ({condition}) yield return null; else do yield return null; while (!({condition}));"
                        : $"if (!({condition})) yield return null; else do yield return null; while ({condition});";
                }
                if (type == WaitForSecondsRealtimeName)
                {
                    string seconds = create != null
                        ? (create.ArgumentList?.Arguments.Count == 1 ? _edits.Take(create.ArgumentList.Arguments[0].Expression) : null)
                        : _edits.Take(value);
                    if (seconds == null)
                        return null;
                    string due = $"__rt{temp++}";
                    return $"{{ float {due} = {Now} + ({seconds}); if (!({Now} < {due})) yield return null; else do yield return null; while ({Now} < {due}); }}";
                }
                if (type == WaitForSecondsName && create == null)
                    return $"yield return new UnityEngine.WaitForSeconds({_edits.Take(value)});";   // 変数に入れた WaitForSeconds（float になる）
                return null;
            }

            /// <summary>WaitUntil・WaitWhile に渡した条件の式（ラムダの式・メソッドの名前）。書けないものはエラーにして null</summary>
            private string Condition(ExpressionSyntax f)
            {
                if (f is ParenthesizedLambdaExpressionSyntax lambda && lambda.ParameterList.Parameters.Count == 0 && lambda.Body is ExpressionSyntax body)
                    return "(" + _edits.Take(body) + ")";
                if ((f is IdentifierNameSyntax || f is MemberAccessExpressionSyntax) && _model.GetSymbolInfo(f).Symbol is IMethodSymbol)
                    return _edits.Take(f) + "()";
                Error(f, Loc.T("WaitUntil・WaitWhile の条件は、() => 条件 の形のラムダか、メソッドの名前で書いてください", "Write the WaitUntil / WaitWhile condition as a lambda like () => condition, or a method name"));
                return null;
            }
        }
    }
}
