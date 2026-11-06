using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

namespace CustomUdonSharp
{
    /// <summary>
    /// ソース上で宣言されたインタフェースを、UdonSharp が扱える形へ書き換える。
    ///
    /// 実装側 (UdonSharpBehaviour : IFoo):
    ///   - 基底リストから IFoo を除去するだけ。
    ///   - 実装メソッドの Udon 上の名前は MethodExportLayout のコンストラクタパッチ
    ///     (<see cref="MethodExportLayoutPatch"/>) でシグネチャ由来の固定名に揃える。
    ///     そのため、ここでは対象メソッドのキー一覧 (<see cref="InterfaceNaming.ImplementationKeys"/>) を作る。
    /// 呼び出し側:
    ///   - 型 IFoo → UdonSharpBehaviour
    ///   - x.M(a)  → M__sig__call(x, a)   (SetProgramVariable / SendCustomEvent / GetProgramVariable)
    ///   - x is IFoo → __if_K__is(x),  x as IFoo → __if_K__as(x)
    ///     （U# の型ID "__refl_typeid" を、コンパイル時に列挙した実装クラスの型IDと照合）
    ///
    /// 書き換えは「行を増やさない」ようにしている（生成メンバーはクラス末尾 '}' と同じ行に置く）ので、
    /// U# のエラー行番号は元ソースと一致する。
    /// </summary>
    internal static class InterfaceLowering
    {
        internal const string UsbType = "global::UdonSharp.UdonSharpBehaviour";
        private static readonly string DumpDir = Path.Combine("Library", "CustomUdonSharp");

        internal sealed class InterfaceInfo
        {
            public INamedTypeSymbol Symbol;
            public string Key;
            public readonly List<long> ImplTypeIds = new List<long>();   // 実装クラス (具象) の U# 型ID
            public string IsHelper => $"__if_{Key}__is";
            public string AsHelper => $"__if_{Key}__as";
            public readonly List<MethodInfo> Methods = new List<MethodInfo>();
        }

        internal sealed class MethodInfo
        {
            public IMethodSymbol Symbol;
            public string Signature;            // InterfaceNaming.SignatureName
            public string Export => Signature;  // 引数なしメソッドは元の名前、それ以外はシグネチャ名
            public string CallHelper => $"__if_{Signature}__call";
            public string ParamField(int i) => InterfaceNaming.ParameterName(Signature, i);
            public string RetField => InterfaceNaming.ReturnName(Signature);
            public bool HasReturn => !Symbol.ReturnsVoid;
        }

        public static void Process(Array bindings, FieldInfo treeField, FieldInfo sourceTextField)
        {
            InterfaceNaming.ImplementationKeys = new HashSet<string>();

            var trees = new List<SyntaxTree>(bindings.Length);
            foreach (object b in bindings)
                trees.Add((SyntaxTree)treeField.GetValue(b));

            // インタフェース宣言が無ければ何もしない（通常ケースのコストをゼロに近づける）
            if (!trees.Any(t => t.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Any()))
                return;

            var compilation = CSharpCompilation.Create(
                "CustomUdonSharp_InterfacePrepass",
                trees,
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var interfaces = CollectInterfaces(compilation, trees);
            if (interfaces.Count == 0)
                return;

            var errors = new List<string>();
            CollectImplementers(compilation, trees, interfaces, errors);

            for (int i = 0; i < bindings.Length; i++)
            {
                SyntaxTree tree = trees[i];
                SemanticModel model = compilation.GetSemanticModel(tree);
                var rewriter = new Rewriter(model, interfaces, errors);
                SyntaxNode newRoot = rewriter.Visit(tree.GetRoot());

                if (!rewriter.Changed)
                    continue;

                string newText = newRoot.ToFullString();
                SyntaxTree newTree = CSharpSyntaxTree.ParseText(newText, (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8);

                treeField.SetValue(bindings.GetValue(i), newTree);
                sourceTextField?.SetValue(bindings.GetValue(i), newText);

                Dump(bindings.GetValue(i), newText);
            }

            foreach (string error in errors)
                Debug.LogError("[CustomUdonSharp] " + error);
        }

        private static Dictionary<INamedTypeSymbol, InterfaceInfo> CollectInterfaces(CSharpCompilation compilation, List<SyntaxTree> trees)
        {
            var result = new Dictionary<INamedTypeSymbol, InterfaceInfo>(SymbolEqualityComparer.Default);

            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol && !result.ContainsKey(symbol))
                    {
                        result.Add(symbol, new InterfaceInfo
                        {
                            Symbol = symbol,
                            Key = InterfaceNaming.Sanitize(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)),
                        });
                    }
                }
            }

            // メソッド名はインタフェース型→UdonSharpBehaviour の置換を反映して作る（全インタフェースが揃ってから）
            Func<INamedTypeSymbol, bool> erase = t => result.ContainsKey(t.OriginalDefinition);
            foreach (InterfaceInfo info in result.Values)
            {
                foreach (ISymbol member in info.Symbol.GetMembers())
                {
                    if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                        info.Methods.Add(new MethodInfo { Symbol = m, Signature = InterfaceNaming.SignatureName(m, erase) });
                    else if (!(member is IMethodSymbol))
                        Debug.LogWarning($"[CustomUdonSharp] '{member}' : PoC はインタフェースのメソッドのみ対応しています（プロパティ/イベントは未対応）");
                }
            }

            return result;
        }

        /// <summary>
        /// 1) is/as 判定用に、各インタフェースを実装する具象 UdonSharpBehaviour の型IDを集める。
        ///    型IDは U# が全 Behaviour のヒープに置く "__refl_typeid" と同じ計算式（TypeSymbol.GetFullTypeName → SHA256）。
        /// 2) 名前を固定化する実装メソッドのキーを集める。U# はオーバーライドを根元のメソッドの名前で呼ぶので、
        ///    オーバーライド元もたどって登録する。
        /// </summary>
        private static void CollectImplementers(CSharpCompilation compilation, List<SyntaxTree> trees,
            Dictionary<INamedTypeSymbol, InterfaceInfo> interfaces, List<string> errors)
        {
            Type typeSymbolType = AccessTools.TypeByName("UdonSharp.Compiler.Symbols.TypeSymbol");
            var fullTypeFormat = (SymbolDisplayFormat)AccessTools.Field(typeSymbolType, "_fullTypeFormat").GetValue(null);
            var getTypeId = AccessTools.Method(AccessTools.TypeByName("UdonSharp.Internal.UdonSharpInternalUtility"), "GetTypeID", new[] { typeof(string) });

            Func<INamedTypeSymbol, bool> erase = t => interfaces.ContainsKey(t.OriginalDefinition);
            var keys = new HashSet<string>();

            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol cls) || !IsUdonSharpBehaviour(cls))
                        continue;

                    long id = (long)getTypeId.Invoke(null, new object[] { cls.ToDisplayString(fullTypeFormat) });

                    foreach (INamedTypeSymbol implemented in cls.AllInterfaces)
                    {
                        if (!interfaces.TryGetValue(implemented.OriginalDefinition, out var info))
                            continue;

                        if (!cls.IsAbstract && !info.ImplTypeIds.Contains(id))
                            info.ImplTypeIds.Add(id);

                        foreach (MethodInfo m in info.Methods)
                        {
                            if (!(cls.FindImplementationForInterfaceMember(m.Symbol) is IMethodSymbol impl))
                                continue;

                            if (impl.ExplicitInterfaceImplementations.Length > 0)
                            {
                                errors.Add($"{cls.Name}.{impl.Name} : 明示的インタフェース実装は未対応です。public メソッドとして実装してください");
                                continue;
                            }

                            for (IMethodSymbol cur = impl; cur != null; cur = cur.OverriddenMethod)
                                keys.Add(InterfaceNaming.Key(cur, erase));
                        }
                    }
                }
            }

            InterfaceNaming.ImplementationKeys = keys;
        }

        internal static bool IsUdonSharpBehaviour(INamedTypeSymbol type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.ToDisplayString() == "UdonSharp.UdonSharpBehaviour")
                    return true;
            return false;
        }

        private static IEnumerable<MetadataReference> GetMetadataReferences()
        {
            Type contextType = AccessTools.TypeByName("UdonSharp.Compiler.CompilationContext");
            var method = AccessTools.Method(contextType, "GetMetadataReferences");
            return (IEnumerable<MetadataReference>)method.Invoke(null, null);
        }

        private static void Dump(object binding, string text)
        {
            try
            {
                string filePath = (string)AccessTools.Field(binding.GetType(), "filePath").GetValue(binding);
                Directory.CreateDirectory(DumpDir);
                File.WriteAllText(Path.Combine(DumpDir, Path.GetFileName(filePath)), text);
            }
            catch
            {
                // デバッグ用ダンプなので失敗は無視
            }
        }

        // ------------------------------------------------------------------

        private sealed class ClassState
        {
            public readonly HashSet<MethodInfo> CallHelpers = new HashSet<MethodInfo>();
            public readonly HashSet<InterfaceInfo> IsHelpers = new HashSet<InterfaceInfo>();
            public readonly HashSet<InterfaceInfo> AsHelpers = new HashSet<InterfaceInfo>();
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;
            private readonly Dictionary<INamedTypeSymbol, InterfaceInfo> _interfaces;
            private readonly List<string> _errors;
            private readonly Stack<ClassState> _states = new Stack<ClassState>();

            public bool Changed { get; private set; }

            public Rewriter(SemanticModel model, Dictionary<INamedTypeSymbol, InterfaceInfo> interfaces, List<string> errors)
            {
                _model = model;
                _interfaces = interfaces;
                _errors = errors;
            }

            private void Error(SyntaxNode node, string message)
            {
                var span = node.GetLocation().GetLineSpan();
                _errors.Add($"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {message}");
            }

            private InterfaceInfo AsTarget(ITypeSymbol type)
            {
                return type is INamedTypeSymbol named && _interfaces.TryGetValue(named.OriginalDefinition, out var info) ? info : null;
            }

            private string TypeName(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeName(array.ElementType) + "[]";
                if (AsTarget(type) != null)
                    return UsbType;
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            // インタフェース宣言自体には手を入れない
            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => node;

            // ---- 型参照の置換 -------------------------------------------------

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && AsTarget(t) != null)
                    return ReplaceType(node);
                return base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && AsTarget(t) != null)
                    return ReplaceType(node);
                return base.VisitQualifiedName(node);
            }

            private SyntaxNode ReplaceType(SyntaxNode node)
            {
                Changed = true;
                return SyntaxFactory.ParseTypeName(UsbType).WithTriviaFrom(node);
            }

            // ---- is / as ------------------------------------------------------

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.IsExpression) || node.IsKind(SyntaxKind.AsExpression))
                {
                    InterfaceInfo info = AsTarget(_model.GetTypeInfo(node.Right).Type);
                    if (info != null)
                    {
                        if (_states.Count == 0)
                        {
                            Error(node, "is/as はクラス内でのみ対応しています");
                            return base.VisitBinaryExpression(node);
                        }

                        bool isIs = node.IsKind(SyntaxKind.IsExpression);
                        _states.Peek().IsHelpers.Add(info);
                        if (!isIs)
                            _states.Peek().AsHelpers.Add(info);

                        var left = (ExpressionSyntax)Visit(node.Left);
                        Changed = true;
                        // (object)(expr) にして、どんな静的型からでもヘルパーに渡せるようにする
                        var arg = SyntaxFactory.Argument(SyntaxFactory.ParseExpression($"(object)({left.WithoutTrivia().ToFullString()})"));
                        return SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(isIs ? info.IsHelper : info.AsHelper),
                                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg)))
                            .WithTriviaFrom(node);
                    }
                }

                return base.VisitBinaryExpression(node);
            }

            // ---- メソッド呼び出し ---------------------------------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (!(_model.GetSymbolInfo(node).Symbol is IMethodSymbol method) || AsTarget(method.ContainingType) == null)
                    return base.VisitInvocationExpression(node);

                InterfaceInfo iface = AsTarget(method.ContainingType);
                MethodInfo info = iface.Methods.FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.Symbol, method.OriginalDefinition));

                if (info == null || !IsSupported(info.Symbol, node))
                    return base.VisitInvocationExpression(node);

                if (!(node.Expression is MemberAccessExpressionSyntax) || _states.Count == 0)
                {
                    Error(node, $"'{method.Name}' : クラス内の x.Method(...) 形式以外のインタフェース呼び出しは未対応です（?. など）");
                    return base.VisitInvocationExpression(node);
                }

                var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);
                var receiver = ((MemberAccessExpressionSyntax)visited.Expression).Expression.WithoutTrivia();

                _states.Peek().CallHelpers.Add(info);
                Changed = true;

                var args = new List<ArgumentSyntax> { SyntaxFactory.Argument(receiver) };
                args.AddRange(visited.ArgumentList.Arguments);

                return SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName(info.CallHelper),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)))
                    .WithTriviaFrom(node);
            }

            private bool IsSupported(IMethodSymbol m, SyntaxNode where)
            {
                if (m.IsGenericMethod)
                {
                    Error(where, $"'{m.Name}' : ジェネリックメソッドは未対応です");
                    return false;
                }

                if (m.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams))
                {
                    Error(where, $"'{m.Name}' : ref/out/in/params 引数は未対応です");
                    return false;
                }

                return true;
            }

            // ---- クラス ---------------------------------------------------------

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var classSymbol = _model.GetDeclaredSymbol(node);
                var state = new ClassState();

                _states.Push(state);
                var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                _states.Pop();

                if (classSymbol == null)
                    return visited;

                // 実装側：基底リストから対象インタフェースを外すだけ（名前の固定化はコンストラクタパッチ側）
                if (node.BaseList != null)
                {
                    var keep = new List<BaseTypeSyntax>();
                    bool removed = false;

                    for (int i = 0; i < node.BaseList.Types.Count; i++)
                    {
                        ITypeSymbol baseType = _model.GetTypeInfo(node.BaseList.Types[i].Type).Type;
                        if (AsTarget(baseType) != null)
                            removed = true;
                        else
                            keep.Add(visited.BaseList.Types[i]);
                    }

                    if (removed)
                    {
                        Changed = true;
                        if (!IsUdonSharpBehaviour(classSymbol))
                            Error(node, $"'{classSymbol.Name}' : UdonSharpBehaviour 以外でのインタフェース実装は未対応です");

                        // 行数を変えないよう、元の最後の型の後ろのトリビア（改行など）を引き継ぐ
                        SyntaxTriviaList trailing = node.BaseList.Types.Last().GetTrailingTrivia();
                        if (keep.Count > 0)
                        {
                            keep[keep.Count - 1] = keep[keep.Count - 1].WithTrailingTrivia(trailing);
                            visited = visited.WithBaseList(visited.BaseList.WithTypes(SyntaxFactory.SeparatedList(keep)));
                        }
                        else
                        {
                            visited = visited.WithBaseList(null).WithIdentifier(visited.Identifier.WithTrailingTrivia(trailing));
                        }
                    }
                }

                // 呼び出し側ヘルパー（同じシグネチャを持つ別インタフェースのメソッドは同名ヘルパーになるので重複除去）
                var members = new List<string>();
                var emitted = new HashSet<string>();
                foreach (MethodInfo m in state.CallHelpers)
                    if (emitted.Add(m.CallHelper))
                        members.Add(BuildCallHelper(m));
                foreach (InterfaceInfo i in state.IsHelpers)
                    members.Add(BuildIsHelper(i));
                foreach (InterfaceInfo i in state.AsHelpers)
                    members.Add($"private static {UsbType} {i.AsHelper}(object __o) {{ return {i.IsHelper}(__o) ? ({UsbType})__o : null; }}");

                if (members.Count == 0)
                    return visited;

                // 生成メンバーは閉じ括弧 '}' と同じ行に置き、行番号を一切ずらさない
                var parsed = members.Select(code => SyntaxFactory.ParseMemberDeclaration(code + " ")).Where(d => d != null);
                return visited.WithMembers(visited.Members.AddRange(parsed));
            }

            private string BuildCallHelper(MethodInfo m)
            {
                var ps = m.Symbol.Parameters;
                var sb = new StringBuilder();
                string ret = m.HasReturn ? TypeName(m.Symbol.ReturnType) : "void";

                sb.Append($"private static {ret} {m.CallHelper}({UsbType} __t");
                for (int i = 0; i < ps.Length; i++)
                {
                    sb.Append($", {TypeName(ps[i].Type)} {ps[i].Name}");
                    if (ps[i].DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ParameterSyntax p && p.Default != null)
                        sb.Append(' ').Append(p.Default.WithoutTrivia().ToFullString());
                }

                sb.Append(") { ");
                for (int i = 0; i < ps.Length; i++)
                    sb.Append($"__t.SetProgramVariable(\"{m.ParamField(i)}\", {ps[i].Name}); ");
                sb.Append($"__t.SendCustomEvent(\"{m.Export}\"); ");
                if (m.HasReturn)
                    sb.Append($"return ({ret})__t.GetProgramVariable(\"{m.RetField}\"); ");
                sb.Append('}');
                return sb.ToString();
            }

            // U# は is 演算子非対応のため GetType() で UdonBehaviour か確認し、U# の型ID (__refl_typeid) を実装クラス一覧と照合
            private static string BuildIsHelper(InterfaceInfo i)
            {
                string cond = i.ImplTypeIds.Count == 0
                    ? "false"
                    : string.Join(" || ", i.ImplTypeIds.Select(id => $"__id == {id}L"));
                return $"private static bool {i.IsHelper}(object __o) {{ if (__o == null) return false; if (!__o.GetType().Equals(typeof(global::VRC.Udon.UdonBehaviour))) return false; object __v = (({UsbType})__o).GetProgramVariable(\"__refl_typeid\"); if (__v == null) return false; long __id = (long)__v; return {cond}; }}";
            }
        }
    }
}
