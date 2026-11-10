using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    /// <summary>
    /// ユーザー定義のジェネリクス（Redmine #59）。使われる型引数ごとにコピーを作る（モノモーフィゼーション）。
    ///
    /// - ジェネリッククラス Pool&lt;T&gt; は、Pool&lt;int&gt; なら Pool__int という普通のクラスのコピーを、元の宣言と同じ場所に作る
    ///   （あとは汎用クラスの段 #49 が普通のクラスとして扱う）
    /// - ジェネリックメソッド Max&lt;T&gt; は、Max&lt;int&gt; なら Max__int というメソッドのコピーを、元の宣言と同じクラスに作る
    /// - コピーの中では T を具体的な型（完全修飾）に置き換えるので、後の段（List / LINQ など）はそのまま使える
    /// - 使う側の Pool&lt;int&gt;・Max(list) などは、コピーの名前に置き換える
    ///
    /// 元の宣言は消し（改行は残し、ほかの文字は空白にする）、コピーは 1 行にして宣言の最後の行に置くので、行数は変わらない。
    /// コピーの各メンバー・各文の前には元の位置の印（SourceMap、#52）を置く。
    /// 書き換えの段の最初に動かし、後の段はジェネリクスのないコードを見る。
    /// </summary>
    internal static class GenericLowering
    {
        /// <summary>型の入れ子の深さの上限（Nest&lt;List&lt;T&gt;&gt; を呼び続けるような、際限なく増える再帰を止める）</summary>
        private const int MaxDepth = 8;

        public static bool MayHaveGenerics(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes(n => !(n is BlockSyntax)).OfType<TypeParameterListSyntax>()
                .Any(p => p.Parent is TypeDeclarationSyntax || p.Parent is MethodDeclarationSyntax));

        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics,
            IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_GenericPrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return new Pass(compilation, trees, diagnostics).Run();
        }

        private sealed class Map : Dictionary<ITypeParameterSymbol, ITypeSymbol>
        {
            public Map() : base(SymbolEqualityComparer.Default) { }
        }

        /// <summary>ジェネリックメソッドのコピー 1 つ（Owner は、ジェネリッククラスの中のメソッドなら型引数の決まったクラス）</summary>
        private sealed class MethodInstance
        {
            public IMethodSymbol Definition;
            public INamedTypeSymbol Owner;
            public ITypeSymbol[] Arguments;
        }

        private struct Edit
        {
            public int Start;
            public int Length;
            public string Text;
        }

        private sealed class Pass
        {
            private readonly CSharpCompilation _compilation;
            private readonly List<SyntaxTree> _trees;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;

            // 対象のジェネリッククラス・ジェネリックメソッドの宣言（元の定義 → 宣言）
            private readonly Dictionary<INamedTypeSymbol, ClassDeclarationSyntax> _classes =
                new Dictionary<INamedTypeSymbol, ClassDeclarationSyntax>(SymbolEqualityComparer.Default);
            private readonly Dictionary<IMethodSymbol, MethodDeclarationSyntax> _methods =
                new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);

            // 使われるコピー（決まった順にするため、見つけた順の一覧と、重複を見るキー）
            private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _classInstances =
                new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            private readonly Dictionary<IMethodSymbol, List<MethodInstance>> _methodInstances =
                new Dictionary<IMethodSymbol, List<MethodInstance>>(SymbolEqualityComparer.Default);
            private readonly HashSet<string> _seen = new HashSet<string>();
            private readonly Queue<Action> _work = new Queue<Action>();
            private readonly HashSet<string> _deepReported = new HashSet<string>();

            public Pass(CSharpCompilation compilation, List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _compilation = compilation;
                _trees = trees;
                _diagnostics = diagnostics;
            }

            private SemanticModel Model(SyntaxTree tree) => _compilation.GetSemanticModel(tree);

            private void Error(SyntaxNode node, string message) => _diagnostics.Error(node.GetLocation(), message);

            public List<SyntaxTree> Run()
            {
                CollectDeclarations();
                if (_classes.Count == 0 && _methods.Count == 0)
                    return _trees;

                // 使われる型引数を集める（ジェネリックの宣言の外から始め、コピーの中も連鎖してたどる）
                foreach (SyntaxTree tree in _trees)
                {
                    SyntaxNode root = tree.GetRoot();
                    _work.Enqueue(() => Scan(root, null, root));
                }
                while (_work.Count > 0)
                    _work.Dequeue()();

                var result = new List<SyntaxTree>(_trees.Count);
                foreach (SyntaxTree tree in _trees)
                {
                    SyntaxNode root = tree.GetRoot();
                    var edits = new List<Edit>();
                    Walk(root, Model(tree), null, edits, null);
                    if (edits.Count == 0)
                    {
                        result.Add(tree);
                        continue;
                    }
                    string text = Apply(root.ToFullString(), 0, edits);
                    result.Add(CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8));
                }
                return result;
            }

            // ---- 宣言 ------------------------------------------------------------

            private void CollectDeclarations()
            {
                foreach (SyntaxTree tree in _trees)
                {
                    SemanticModel model = Model(tree);
                    foreach (SyntaxNode node in tree.GetRoot().DescendantNodes(n => !(n is BlockSyntax)))
                    {
                        switch (node)
                        {
                            case InterfaceDeclarationSyntax i when i.TypeParameterList != null:
                                Error(i.TypeParameterList, $"'{i.Identifier.ValueText}' : ジェネリックなインタフェースは未対応です");
                                break;

                            case ClassDeclarationSyntax c when c.TypeParameterList != null:
                                if (!(model.GetDeclaredSymbol(c) is INamedTypeSymbol type))
                                    break;
                                if (InterfaceLowering.IsUdonSharpBehaviour(type))
                                    Error(c.TypeParameterList, $"'{type.Name}' : ジェネリックな UdonSharpBehaviour は未対応です");
                                else if (type.ContainingType != null)
                                    Error(c.TypeParameterList, $"'{type.Name}' : 入れ子のクラスは未対応です。名前空間の直下に宣言してください");
                                else if (type.DeclaringSyntaxReferences.Length > 1)
                                    Error(c.TypeParameterList, $"'{type.Name}' : ジェネリッククラスの partial は未対応です");
                                else if (ClassLowering.IsPlainClass(type))
                                    _classes[type] = c;
                                break;

                            case MethodDeclarationSyntax m when m.TypeParameterList != null:
                                // UdonSharpBehaviour・汎用クラス・static クラスのもの（ジェネリッククラスの中なら、対象のクラスのものだけ）
                                if (model.GetDeclaredSymbol(m) is IMethodSymbol method && method.ContainingType.TypeKind == TypeKind.Class
                                    && (InterfaceLowering.IsUdonSharpBehaviour(method.ContainingType) || ClassLowering.IsPlainClass(method.ContainingType))
                                    && (!method.ContainingType.IsGenericType || _classes.ContainsKey(method.ContainingType)))
                                {
                                    _methods[method] = m;
                                    WarnIfVisible(method, m);
                                }
                                break;
                        }
                    }
                }
            }

            /// <summary>
            /// UdonSharpBehaviour のジェネリックメソッドで、別のクラスから見えるもの（public・internal・protected internal）に警告を出す（#64）。
            /// 別の UdonSharpBehaviour からの呼び出しはエラーにしている（CheckCaller）ので、見えても呼べない。
            /// private・protected・private protected は、同じクラスと継承したクラスの中からだけ呼べるので、対応範囲と一致する
            /// </summary>
            private void WarnIfVisible(IMethodSymbol method, MethodDeclarationSyntax declaration)
            {
                if (!InterfaceLowering.IsUdonSharpBehaviour(method.ContainingType))
                    return;
                switch (method.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                    case Accessibility.Internal:
                    case Accessibility.ProtectedOrInternal:
                        _diagnostics.Warning(declaration.Identifier.GetLocation(),
                            $"'{method.Name}' : UdonSharpBehaviour のジェネリックメソッドは、別の UdonSharpBehaviour からは呼べません" +
                            "（同じクラスと継承したクラスの中からだけ呼べます）。private か protected にしてください");
                        break;
                }
            }

            /// <summary>ジェネリックメソッドの、ジェネリッククラスの中のもの（クラスのコピーの中に作る）か</summary>
            private bool InGenericClass(IMethodSymbol definition) => _classes.ContainsKey(definition.ContainingType.OriginalDefinition);

            // ---- 型の置き換え -------------------------------------------------------

            private ITypeSymbol Subst(ITypeSymbol type, Map map)
            {
                switch (type)
                {
                    case null:
                        return null;
                    case ITypeParameterSymbol p:
                        return map != null && map.TryGetValue(p, out ITypeSymbol value) ? value : p;
                    case IArrayTypeSymbol array:
                        return _compilation.CreateArrayTypeSymbol(Subst(array.ElementType, map), array.Rank);
                    case INamedTypeSymbol named when named.IsUnboundGenericType:
                        return named;
                    case INamedTypeSymbol named when named.TypeArguments.Length > 0 || (named.ContainingType?.IsGenericType ?? false):
                    {
                        INamedTypeSymbol definition = named.OriginalDefinition;
                        if (named.ContainingType != null && named.ContainingType.IsGenericType)
                        {
                            var container = (INamedTypeSymbol)Subst(named.ContainingType, map);
                            definition = container.GetTypeMembers(named.Name, named.Arity).FirstOrDefault() ?? definition;
                        }
                        return named.TypeArguments.Length == 0
                            ? definition
                            : definition.Construct(named.TypeArguments.Select(a => Subst(a, map)).ToArray());
                    }
                    default:
                        return type;
                }
            }

            /// <summary>型パラメーターを含むか</summary>
            private static bool Open(ITypeSymbol type)
            {
                switch (type)
                {
                    case ITypeParameterSymbol _:
                        return true;
                    case IArrayTypeSymbol array:
                        return Open(array.ElementType);
                    case INamedTypeSymbol named:
                        return named.IsUnboundGenericType   // typeof(Pool<>)
                               || named.TypeArguments.Any(Open) || (named.ContainingType != null && Open(named.ContainingType));
                    default:
                        return false;
                }
            }

            private static int Depth(ITypeSymbol type)
            {
                switch (type)
                {
                    case IArrayTypeSymbol array:
                        return 1 + Depth(array.ElementType);
                    case INamedTypeSymbol named when named.TypeArguments.Length > 0:
                        return 1 + named.TypeArguments.Max(Depth);
                    default:
                        return 0;
                }
            }

            private static Map ClassMap(INamedTypeSymbol instance)
            {
                var map = new Map();
                if (instance == null)
                    return map;
                var parameters = instance.OriginalDefinition.TypeParameters;
                for (int i = 0; i < parameters.Length; i++)
                    map[parameters[i]] = instance.TypeArguments[i];
                return map;
            }

            private static Map MethodMap(MethodInstance instance)
            {
                Map map = instance.Owner.IsGenericType ? ClassMap(instance.Owner) : new Map();
                var parameters = instance.Definition.TypeParameters;
                for (int i = 0; i < parameters.Length; i++)
                    map[parameters[i]] = instance.Arguments[i];
                return map;
            }

            // ---- 名前 -----------------------------------------------------------

            private static string Token(string typeText) => InterfaceNaming.Sanitize(typeText.Replace("global::", ""));

            private bool IsClassInstance(ITypeSymbol type) =>
                type is INamedTypeSymbol named && _classes.ContainsKey(named.OriginalDefinition) && !Open(named);

            private string ClassCopyName(INamedTypeSymbol instance) =>
                instance.Name + "__" + string.Join("__", instance.TypeArguments.Select(a => Token(TypeText(a))));

            private string MethodCopyName(MethodInstance instance)
            {
                IMethodSymbol definition = instance.Definition;
                string name = definition.Name + "__" + string.Join("__", instance.Arguments.Select(a => Token(TypeText(a))));
                // 同じ名前のジェネリックメソッドのオーバーロードは、宣言の順番で区別する
                var overloads = definition.ContainingType.GetMembers(definition.Name).OfType<IMethodSymbol>()
                    .Where(m => m.IsGenericMethod).ToList();
                if (overloads.Count > 1)
                    name += "_" + overloads.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, definition));
                return name;
            }

            /// <summary>具体的な型の、完全修飾の表記（ジェネリッククラスはコピーの名前）</summary>
            private string TypeText(ITypeSymbol type)
            {
                switch (type)
                {
                    case IArrayTypeSymbol array:
                        return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                    case INamedTypeSymbol named when IsClassInstance(named):
                        return Namespace(named.ContainingNamespace) + ClassCopyName(named);
                    case INamedTypeSymbol named when named.TypeArguments.Length > 0 && !named.IsTupleType:
                    {
                        string prefix = named.ContainingType != null ? TypeText(named.ContainingType) + "." : Namespace(named.ContainingNamespace);
                        return $"{prefix}{named.Name}<{string.Join(", ", named.TypeArguments.Select(TypeText))}>";
                    }
                    default:
                        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            private static string Namespace(INamespaceSymbol ns) =>
                ns == null || ns.IsGlobalNamespace ? "global::" : ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".";

            private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // ---- 使われる型引数を集める ---------------------------------------------

            /// <summary>ジェネリックの宣言か（その中は、型引数ごとのコピーとしてたどる）</summary>
            private bool IsTemplate(SyntaxNode node) =>
                node is ClassDeclarationSyntax c && c.TypeParameterList != null
                || node is MethodDeclarationSyntax m && m.TypeParameterList != null;

            private void Scan(SyntaxNode scope, Map map, SyntaxNode self)
            {
                SemanticModel model = Model(scope.SyntaxTree);
                foreach (SyntaxNode node in scope.DescendantNodes(n => n == self || !IsTemplate(n)))
                {
                    if (!(node is ExpressionSyntax))
                        continue;
                    switch (model.GetSymbolInfo(node).Symbol)
                    {
                        case ITypeSymbol type:
                            UseType(Subst(type, map), node);
                            break;
                        case IMethodSymbol method:
                            UseMethod(method, map, node);
                            break;
                        case ISymbol member when member.ContainingType != null:
                            UseType(Subst(member.ContainingType, map), node);
                            break;
                    }
                    TypeInfo info = model.GetTypeInfo(node);
                    UseType(Subst(info.Type, map), node);
                    UseType(Subst(info.ConvertedType, map), node);
                }
            }

            private bool TooDeep(ITypeSymbol type, SyntaxNode at)
            {
                if (Depth(type) <= MaxDepth)
                    return false;
                // 同じ行の複数の式から見つかるので、行ごとに 1 回
                var line = at.GetLocation().GetLineSpan();
                if (_deepReported.Add(line.Path + "|" + at.SyntaxTree.GetHashCode() + "|" + line.StartLinePosition.Line))
                    Error(at, "ジェネリックの型引数の入れ子が深すぎます（型が際限なく増える再帰は未対応です）");
                return true;
            }

            private void UseType(ITypeSymbol type, SyntaxNode at)
            {
                switch (type)
                {
                    case IArrayTypeSymbol array:
                        UseType(array.ElementType, at);
                        return;
                    case INamedTypeSymbol named when named.TypeArguments.Length > 0:
                        if (Open(named) || TooDeep(named, at))
                            return;
                        foreach (ITypeSymbol argument in named.TypeArguments)
                            UseType(argument, at);
                        if (_classes.TryGetValue(named.OriginalDefinition, out ClassDeclarationSyntax decl) && _seen.Add("C|" + TypeText(named)))
                        {
                            Instances(_classInstances, named.OriginalDefinition).Add(named);
                            Map map = ClassMap(named);
                            _work.Enqueue(() => Scan(decl, map, decl));
                        }
                        return;
                }
            }

            private void UseMethod(IMethodSymbol method, Map map, SyntaxNode at)
            {
                UseType(Subst(method.ContainingType, map), at);
                IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
                if (!_methods.TryGetValue(definition, out MethodDeclarationSyntax decl) || method.TypeArguments.Length == 0)
                    return;

                var instance = new MethodInstance
                {
                    Definition = definition,
                    Owner = definition.ContainingType.IsGenericType
                        ? (INamedTypeSymbol)Subst(method.ContainingType, map)
                        : definition.ContainingType,
                    Arguments = method.TypeArguments.Select(a => Subst(a, map)).ToArray(),
                };
                if (Open(instance.Owner) || instance.Arguments.Any(Open) || instance.Arguments.Any(a => TooDeep(a, at)))
                    return;
                foreach (ITypeSymbol argument in instance.Arguments)
                    UseType(argument, at);
                if (!_seen.Add("M|" + TypeText(instance.Owner) + "|" + definition.ToDisplayString() + "|" + string.Join(",", instance.Arguments.Select(TypeText))))
                    return;
                Instances(_methodInstances, definition).Add(instance);
                Map methodMap = MethodMap(instance);
                _work.Enqueue(() => Scan(decl, methodMap, decl));
            }

            private static List<T> Instances<TKey, T>(Dictionary<TKey, List<T>> table, TKey key)
            {
                if (!table.TryGetValue(key, out List<T> list))
                    table[key] = list = new List<T>();
                return list;
            }

            private MethodInstance ToInstance(IMethodSymbol method, Map map)
            {
                IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
                return new MethodInstance
                {
                    Definition = definition,
                    Owner = definition.ContainingType.IsGenericType ? (INamedTypeSymbol)Subst(method.ContainingType, map) : definition.ContainingType,
                    Arguments = method.TypeArguments.Select(a => Subst(a, map)).ToArray(),
                };
            }

            // ---- 書き換え ---------------------------------------------------------

            /// <summary>コピーを作っているところ（null なら、ジェネリックの宣言の外）</summary>
            private sealed class Copy
            {
                public Map Map;
                public string ClassName;   // 印に記録する（エラーの補足「汎用クラス Pool&lt;int&gt; の…」）
                public string Member;
            }

            /// <summary>node の中の置き換えを edits に集める</summary>
            private void Walk(SyntaxNode node, SemanticModel model, Copy copy, List<Edit> edits, SyntaxNode self)
            {
                // ジェネリックの宣言：消して、使われる型引数ごとのコピーを置く
                if (node != self && IsTemplate(node))
                {
                    string copies = node is ClassDeclarationSyntax cls ? ClassCopies(cls, model) : MethodCopies((MethodDeclarationSyntax)node, model, copy);
                    if (copies != null)
                        edits.Add(new Edit { Start = node.SpanStart, Length = node.Span.Length, Text = Blank(node) + copies });
                    return;
                }

                if (copy != null)
                {
                    if (node is StatementSyntax && !(node is BlockSyntax))
                        edits.Add(Insert(node.SpanStart, SourceMap.Marker(node, copy.ClassName, copy.Member)));
                    else if (node is ArrowExpressionClauseSyntax arrow)
                        edits.Add(Insert(arrow.Expression.SpanStart, SourceMap.Marker(arrow.Expression, copy.ClassName, copy.Member)));
                }

                if (node is ExpressionSyntax expression && Replace(expression, model, copy) is string replacement)
                {
                    edits.Add(new Edit { Start = node.SpanStart, Length = node.Span.Length, Text = replacement });
                    return;
                }

                foreach (SyntaxNode child in node.ChildNodes())
                    Walk(child, model, copy, edits, self);
            }

            private static Edit Insert(int position, string text) => new Edit { Start = position, Length = 0, Text = text };

            /// <summary>置き換えた表記（置き換えないなら null）</summary>
            private string Replace(ExpressionSyntax node, SemanticModel model, Copy copy)
            {
                if (!(node is SimpleNameSyntax || node is MemberAccessExpressionSyntax || node is TypeOfExpressionSyntax))
                    return null;

                Map map = copy?.Map;
                ISymbol symbol = model.GetSymbolInfo(node).Symbol;

                switch (node)
                {
                    case TypeOfExpressionSyntax typeOf when typeOf.Type.DescendantNodesAndSelf().OfType<OmittedTypeArgumentSyntax>().Any():
                        Error(node, "型引数を省いた typeof（typeof(Pool<>) など）は未対応です");
                        return null;

                    case MemberAccessExpressionSyntax access:
                        if (symbol is IPropertySymbol p && p.Name == "Default" && p.ContainingType.OriginalDefinition is INamedTypeSymbol owner
                            && (owner.ToDisplayString() == "System.Collections.Generic.EqualityComparer<T>" || owner.ToDisplayString() == "System.Collections.Generic.Comparer<T>"))
                            Error(node, $"'{access}' : EqualityComparer<T>.Default / Comparer<T>.Default は未対応です（CompareTo・Equals を直接呼んでください）");
                        return null;

                    case IdentifierNameSyntax _ when symbol is ITypeParameterSymbol parameter:
                        return map != null && map.TryGetValue(parameter, out ITypeSymbol value) ? TypeText(value) : null;

                    case GenericNameSyntax _ when symbol is INamedTypeSymbol type && _classes.ContainsKey(type.OriginalDefinition):
                    {
                        var instance = (INamedTypeSymbol)Subst(type, map);
                        return Open(instance) ? null : ClassCopyName(instance);
                    }

                    case SimpleNameSyntax name when symbol is IMethodSymbol method && method.TypeArguments.Length > 0
                                                   && _methods.ContainsKey((method.ReducedFrom ?? method).OriginalDefinition):
                    {
                        MethodInstance instance = ToInstance(method, map);
                        if (Open(instance.Owner) || instance.Arguments.Any(Open))
                            return null;
                        if (!CheckCaller(name, instance.Definition, model))
                            return null;
                        return MethodCopyName(instance);
                    }
                }
                return null;
            }

            /// <summary>UdonSharpBehaviour のジェネリックメソッドは、同じクラスの中からだけ呼べる（決めたこと 5）</summary>
            private bool CheckCaller(SimpleNameSyntax name, IMethodSymbol definition, SemanticModel model)
            {
                if (!InterfaceLowering.IsUdonSharpBehaviour(definition.ContainingType))
                    return true;
                bool sameReceiver = !(name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
                                    || ((MemberAccessExpressionSyntax)name.Parent).Expression is ThisExpressionSyntax
                                    || ((MemberAccessExpressionSyntax)name.Parent).Expression is BaseExpressionSyntax;
                INamedTypeSymbol caller = model.GetEnclosingSymbol(name.SpanStart)?.ContainingType;
                bool sameClass = false;
                for (INamedTypeSymbol t = caller; t != null; t = t.BaseType)
                    if (SymbolEqualityComparer.Default.Equals(t, definition.ContainingType))
                        sameClass = true;
                if (sameReceiver && sameClass)
                    return true;
                Error(name, $"'{definition.Name}' : 別の UdonSharpBehaviour のジェネリックメソッドを呼ぶのは未対応です（static クラスに移すか、型を決めたメソッドを用意してください）");
                return false;
            }

            /// <summary>
            /// 宣言を消した表記。改行はそのまま、ほかの文字は 1 文字ずつ空白にして、行数と各行の長さを変えない
            /// （U# のエラーの位置を元の位置に戻すとき、列が行の長さで切り詰められないように。#88）
            /// </summary>
            private static string Blank(SyntaxNode node)
            {
                string text = node.ToString();
                var sb = new StringBuilder(text.Length);
                foreach (char c in text)
                    sb.Append(c == '\r' || c == '\n' ? c : ' ');
                return sb.ToString();
            }

            /// <summary>ジェネリッククラスの、型引数ごとのコピー（1 行ずつ空白でつなぐ）。対象でなければ null</summary>
            private string ClassCopies(ClassDeclarationSyntax decl, SemanticModel model)
            {
                if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol type) || !_classes.ContainsKey(type))
                    return null;
                if (!_classInstances.TryGetValue(type, out var instances))
                    return "";
                return string.Concat(instances.Select(instance => " " + CopyClass(decl, model, instance)));
            }

            private string CopyClass(ClassDeclarationSyntax decl, SemanticModel model, INamedTypeSymbol instance)
            {
                var copy = new Copy { Map = ClassMap(instance), ClassName = Display(instance) };
                string name = ClassCopyName(instance);
                var edits = new List<Edit>
                {
                    new Edit { Start = decl.Identifier.SpanStart, Length = decl.Identifier.Span.Length, Text = name },
                    new Edit { Start = decl.TypeParameterList.SpanStart, Length = decl.TypeParameterList.Span.Length, Text = "" },
                };
                foreach (TypeParameterConstraintClauseSyntax clause in decl.ConstraintClauses)
                    edits.Add(new Edit { Start = clause.SpanStart, Length = clause.Span.Length, Text = "" });
                if (decl.BaseList != null)
                    Walk(decl.BaseList, model, copy, edits, null);

                foreach (MemberDeclarationSyntax member in decl.Members)
                {
                    if (member is MethodDeclarationSyntax generic && generic.TypeParameterList != null)
                    {
                        // クラスの中のジェネリックメソッド：このクラスのコピーで使われる型引数ごとにコピー
                        string copies = MethodCopies(generic, model, copy, instance) ?? "";
                        edits.Add(new Edit { Start = member.SpanStart, Length = member.Span.Length, Text = copies });
                        continue;
                    }
                    if (member is ConstructorDeclarationSyntax ctor)
                        edits.Add(new Edit { Start = ctor.Identifier.SpanStart, Length = ctor.Identifier.Span.Length, Text = name });

                    copy.Member = MemberLabel(member, model);
                    edits.Add(Insert(member.SpanStart, SourceMap.Marker(member, copy.ClassName, copy.Member)));
                    WalkMember(member, model, copy, edits);
                }
                return Flatten(decl, Apply(decl.ToString(), decl.SpanStart, edits));
            }

            /// <summary>メンバーの中（コンストラクタの名前など、置き換え済みのトークンは見ない）</summary>
            private void WalkMember(MemberDeclarationSyntax member, SemanticModel model, Copy copy, List<Edit> edits)
            {
                foreach (SyntaxNode child in member.ChildNodes())
                    Walk(child, model, copy, edits, null);
            }

            private static string MemberLabel(MemberDeclarationSyntax member, SemanticModel model)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax m: return $"メソッド {m.Identifier.ValueText}";
                    case ConstructorDeclarationSyntax _: return "コンストラクタ";
                    case PropertyDeclarationSyntax p: return $"プロパティ {p.Identifier.ValueText}";
                    case FieldDeclarationSyntax f: return $"フィールド {string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText))}";
                    default: return null;
                }
            }

            /// <summary>
            /// ジェネリックメソッドの、型引数ごとのコピー（空白でつなぐ）。対象でなければ null。
            /// owner はジェネリッククラスのコピーの中なら、そのクラスの型引数の決まったもの
            /// </summary>
            private string MethodCopies(MethodDeclarationSyntax decl, SemanticModel model, Copy outer, INamedTypeSymbol owner = null)
            {
                if (!(model.GetDeclaredSymbol(decl) is IMethodSymbol definition) || !_methods.ContainsKey(definition))
                    return null;
                if (owner == null && InGenericClass(definition))
                    return null;   // ジェネリッククラスの中のもの（クラスのコピーの中で作る）
                if (!_methodInstances.TryGetValue(definition, out var instances))
                    return "";
                return string.Concat(instances
                    .Where(i => owner == null || SymbolEqualityComparer.Default.Equals(i.Owner, owner))
                    .Select(i => " " + CopyMethod(decl, model, i, outer)));
            }

            private string CopyMethod(MethodDeclarationSyntax decl, SemanticModel model, MethodInstance instance, Copy outer)
            {
                var copy = new Copy
                {
                    Map = MethodMap(instance),
                    ClassName = outer?.ClassName,
                    Member = $"メソッド {decl.Identifier.ValueText}<{string.Join(", ", instance.Arguments.Select(Display))}>",
                };
                var edits = new List<Edit>
                {
                    Insert(decl.SpanStart, SourceMap.Marker(decl, copy.ClassName, copy.Member)),
                    new Edit { Start = decl.Identifier.SpanStart, Length = decl.Identifier.Span.Length, Text = MethodCopyName(instance) },
                    new Edit { Start = decl.TypeParameterList.SpanStart, Length = decl.TypeParameterList.Span.Length, Text = "" },
                };
                foreach (TypeParameterConstraintClauseSyntax clause in decl.ConstraintClauses)
                    edits.Add(new Edit { Start = clause.SpanStart, Length = clause.Span.Length, Text = "" });
                foreach (SyntaxNode child in decl.ChildNodes())
                    if (!(child is TypeParameterListSyntax) && !(child is TypeParameterConstraintClauseSyntax))
                        Walk(child, model, copy, edits, null);
                return Flatten(decl, Apply(decl.ToString(), decl.SpanStart, edits));
            }

            // ---- テキスト ---------------------------------------------------------

            /// <summary>text（元の位置 offset から）に edits を当てる。同じ位置では挿入を先にする</summary>
            private static string Apply(string text, int offset, List<Edit> edits)
            {
                var sb = new StringBuilder();
                int position = 0;
                foreach (Edit edit in edits.OrderBy(e => e.Start).ThenBy(e => e.Length))
                {
                    int start = edit.Start - offset;
                    if (start < position)
                        continue;   // 重なり（起きない想定）
                    sb.Append(text, position, start - position).Append(edit.Text);
                    position = start + edit.Length;
                }
                sb.Append(text, position, text.Length - position);
                return sb.ToString();
            }

            /// <summary>
            /// メンバーの宣言を 1 行にする（元の宣言の最後の行に置くため）。コメントと改行は空白にし（印は残す）、
            /// 改行を含む文字列リテラルはエスケープした表記にする
            /// </summary>
            private string Flatten(SyntaxNode original, string text)
            {
                MemberDeclarationSyntax member = SyntaxFactory.ParseMemberDeclaration(text);
                if (member == null)
                {
                    Error(original, "ジェネリックのコピーを作れませんでした");
                    return "";
                }
                if (member.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective))
                {
                    Error(original, "ジェネリックなクラス・メソッドの中のプリプロセッサ指令（#if など）は未対応です");
                    return "";
                }

                SyntaxNode flat = member.ReplaceTokens(
                    member.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken) && t.Text.Contains("\n")),
                    (o, _) => SyntaxFactory.Literal(o.ValueText).WithTriviaFrom(o));
                flat = flat.ReplaceTrivia(flat.DescendantTrivia(descendIntoTrivia: false),
                    (o, _) => SourceMap.IsMarker(o) ? o : SyntaxFactory.Space);

                string result = flat.ToFullString().Trim();
                if (result.Contains("\n"))
                {
                    Error(original, "ジェネリックなクラス・メソッドの中の、改行を含む補間文字列は未対応です");
                    return "";
                }
                return result;
            }
        }
    }
}
