using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CustomUdonSharp
{
    /// <summary>
    /// List&lt;T&gt; / Dictionary&lt;K, V&gt; を、Udon の DataList / DataDictionary に書き換える（Redmine #56）。
    ///
    ///   型             : List&lt;int&gt; → DataList、Dictionary&lt;string, int&gt; → DataDictionary
    ///   出し入れ       : 宣言された要素の型で DataToken と変換する
    ///                    list[i] → list[i].Int、list.Add(v) → list.Add(new DataToken((int)(v)))
    ///                    （汎用クラス・インタフェース・UnityEngine.Object などは Reference として入れる）
    ///   foreach        : 添字で回す for ループにする（Dictionary は GetKeys() で回し、kv.Key / kv.Value を書き換える）
    ///   TryGetValue    : out の変換が要るので、文の前に呼び出しを出す
    ///
    /// インタフェース・汎用クラスの段の後に動く（要素の型は object[]・object になっている）。
    /// 書き換えるのは UdonSharpBehaviour の中だけ（汎用クラスはこの時点で使う側にコピーされている）。
    /// 行を増やさない（生成メンバーはクラス末尾 '}' と同じ行、ループなどは元の行に収める）。
    /// </summary>
    internal static class CollectionLowering
    {
        internal const string DataListType = "global::VRC.SDK3.Data.DataList";
        internal const string DataDictionaryType = "global::VRC.SDK3.Data.DataDictionary";
        internal const string DataTokenType = "global::VRC.SDK3.Data.DataToken";

        private const string ListName = "System.Collections.Generic.List<T>";
        private const string DictionaryName = "System.Collections.Generic.Dictionary<TKey, TValue>";
        private const string KeysName = "System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection";
        private const string ValuesName = "System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection";

        internal static bool IsList(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == ListName;
        internal static bool IsDictionary(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == DictionaryName;
        private static bool IsKeys(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == KeysName;
        private static bool IsValues(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == ValuesName;

        /// <summary>置き換え先の型（List / Dictionary でなければ null）</summary>
        internal static string ReplacementOf(INamedTypeSymbol type) =>
            IsList(type) ? DataListType : IsDictionary(type) ? DataDictionaryType : null;

        /// <summary>
        /// ジェネリック型の表記を、型引数を map で置き換えて作る（List&lt;Bullet&gt; → List&lt;object[]&gt; など）。
        /// ジェネリック型でなければ null
        /// </summary>
        internal static string GenericText(INamedTypeSymbol type, Func<ITypeSymbol, string> map)
        {
            if (!type.IsGenericType || type.ContainingType != null && type.ContainingType.IsGenericType)
                return null;
            string definition = type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            int open = definition.IndexOf('<');
            if (open < 0)
                return null;
            return definition.Substring(0, open) + "<" + string.Join(", ", type.TypeArguments.Select(map)) + ">";
        }

        /// <summary>List / Dictionary がありそうか（なければコンパイルを作らずに済ませる）</summary>
        public static bool MayHaveCollections(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().OfType<GenericNameSyntax>()
                .Any(g => g.Identifier.ValueText == "List" || g.Identifier.ValueText == "Dictionary"));

        /// <summary>書き換えた tree の一覧を返す（変わらない tree はそのまま）</summary>
        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics,
            IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "CustomUdonSharp_CollectionPrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                var rewriter = new Rewriter(compilation.GetSemanticModel(tree), diagnostics);
                SyntaxNode newRoot = rewriter.Visit(tree.GetRoot());
                result.Add(rewriter.Changed
                    ? CSharpSyntaxTree.ParseText(newRoot.ToFullString(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }
            return result;
        }

        // ------------------------------------------------------------------

        /// <summary>foreach (var kv in dict) の kv（kv.Key / kv.Value を書き換える）</summary>
        private sealed class PairLoop
        {
            public string Dictionary;   // DataDictionary の一時変数
            public string Key;          // キーの DataToken の一時変数
            public ITypeSymbol KeyType, ValueType;
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private const string HoistAnnotation = "CustomUdonSharp.CollectionHoist";

            private readonly SemanticModel _model;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly Dictionary<ISymbol, PairLoop> _pairs = new Dictionary<ISymbol, PairLoop>(SymbolEqualityComparer.Default);
            private readonly Dictionary<string, List<string>> _hoisted = new Dictionary<string, List<string>>();
            private SortedDictionary<string, string> _helpers;   // 生成する補助メソッド（名前 → コード）。UdonSharpBehaviour の中だけ
            private List<string> _pending;                        // 今の文の前に出す文（TryGetValue）
            private int _temp;

            public bool Changed { get; private set; }

            public Rewriter(SemanticModel model, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _diagnostics = diagnostics;
            }

            private void Error(SyntaxNode node, string message) => _diagnostics.Error(node.GetLocation(), message);

            private string Temp(string name) => $"__col_{name}{_temp++}";

            private static string Text(SyntaxNode node) => node.WithoutTrivia().ToFullString();

            private ExpressionSyntax Parse(string code, SyntaxNode original)
            {
                Changed = true;
                return SyntaxFactory.ParseExpression(code).WithTriviaFrom(original);
            }

            private ITypeSymbol TypeOf(SyntaxNode node) => _model.GetTypeInfo(node).Type;

            // ---- 型と DataToken の変換 -------------------------------------------

            /// <summary>書き換え後の型の表記</summary>
            private static string TypeText(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                if (type is INamedTypeSymbol named)
                {
                    if (ReplacementOf(named) is string replaced)
                        return replaced;
                    if (GenericText(named, TypeText) is string generic)
                        return generic;
                }
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            /// <summary>DataToken のアクセサー（.Int など）。Reference で扱う型なら null</summary>
            private static string Accessor(ITypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean: return "Boolean";
                    case SpecialType.System_SByte: return "SByte";
                    case SpecialType.System_Byte: return "Byte";
                    case SpecialType.System_Int16: return "Short";
                    case SpecialType.System_UInt16: return "UShort";
                    case SpecialType.System_Int32: return "Int";
                    case SpecialType.System_UInt32: return "UInt";
                    case SpecialType.System_Int64: return "Long";
                    case SpecialType.System_UInt64: return "ULong";
                    case SpecialType.System_Single: return "Float";
                    case SpecialType.System_Double: return "Double";
                    case SpecialType.System_String: return "String";
                }
                if (IsList(type) || type.ToDisplayString() == "VRC.SDK3.Data.DataList")
                    return "DataList";
                if (IsDictionary(type) || type.ToDisplayString() == "VRC.SDK3.Data.DataDictionary")
                    return "DataDictionary";
                return null;
            }

            private static bool IsToken(ITypeSymbol type) => type.ToDisplayString() == "VRC.SDK3.Data.DataToken";

            private static bool IsUnityObject(ITypeSymbol type)
            {
                for (ITypeSymbol t = type; t != null; t = t.BaseType)
                    if (t.ToDisplayString() == "UnityEngine.Object")
                        return true;
                return false;
            }

            /// <summary>値 → DataToken</summary>
            private static string Wrap(string value, ITypeSymbol type)
            {
                if (IsToken(type))
                    return value;
                if (type is INamedTypeSymbol e && e.TypeKind == TypeKind.Enum)
                    return $"new {DataTokenType}(({e.EnumUnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({value}))";
                if (Accessor(type) != null)
                    return $"new {DataTokenType}(({TypeText(type)})({value}))";
                if (IsUnityObject(type))
                    return $"new {DataTokenType}((global::UnityEngine.Object)({value}))";
                return $"new {DataTokenType}((object)({value}))";
            }

            /// <summary>DataToken → 値</summary>
            private static string Unwrap(string token, ITypeSymbol type)
            {
                if (IsToken(type))
                    return token;
                if (type is INamedTypeSymbol e && e.TypeKind == TypeKind.Enum)
                    return $"(({TypeText(type)})({token}).{Accessor(e.EnumUnderlyingType)})";
                if (Accessor(type) is string accessor)
                    return $"({token}).{accessor}";
                return $"(({TypeText(type)})({token}).Reference)";
            }

            private static ITypeSymbol Element(ITypeSymbol list) => ((INamedTypeSymbol)list).TypeArguments[0];
            private static ITypeSymbol KeyOf(ITypeSymbol dict) => ((INamedTypeSymbol)dict).TypeArguments[0];
            private static ITypeSymbol ValueOf(ITypeSymbol dict) => ((INamedTypeSymbol)dict).TypeArguments[1];

            /// <summary>Keys / Values の元の Dictionary の型</summary>
            private static ITypeSymbol DictionaryOfCollection(ITypeSymbol collection) => ((INamedTypeSymbol)collection).ContainingType;

            private static string Token(ITypeSymbol type) => InterfaceNaming.Sanitize(TypeText(type).Replace("global::", ""));

            // ---- クラス・文 ------------------------------------------------------

            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => node;

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // U# がコンパイルするのは UdonSharpBehaviour だけ（汎用クラスはコピーが UdonSharpBehaviour の中にある）
                if (_helpers != null || !(_model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol) || !InterfaceLowering.IsUdonSharpBehaviour(symbol))
                    return node;

                _helpers = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                var helpers = _helpers;
                _helpers = null;

                if (helpers.Count == 0)
                    return visited;
                Changed = true;
                // 生成メンバーは閉じ括弧 '}' と同じ行に置き、行番号を一切ずらさない
                var parsed = helpers.Values.Select(code => SyntaxFactory.ParseMemberDeclaration(code + " ")).Where(d => d != null);
                return visited.WithMembers(visited.Members.AddRange(parsed));
            }

            /// <summary>文：TryGetValue の前出しを受け取る</summary>
            public override SyntaxNode Visit(SyntaxNode node)
            {
                if (!(node is StatementSyntax) || node is BlockSyntax || _helpers == null)
                    return base.Visit(node);

                List<string> saved = _pending;
                _pending = new List<string>();
                SyntaxNode result = base.Visit(node);
                List<string> pre = _pending;
                _pending = saved;
                if (pre.Count == 0)
                    return result;

                Changed = true;
                if (node.Parent is BlockSyntax || node.Parent is SwitchSectionSyntax)
                {
                    // 親（ブロック）で、この文の前に並べる（ローカル変数の有効範囲を変えないため）
                    string id = Guid.NewGuid().ToString("N");
                    _hoisted[id] = pre;
                    return result.WithAdditionalAnnotations(new SyntaxAnnotation(HoistAnnotation, id));
                }

                // 埋め込み文（if (a) 文; など）はブロックにする
                return SyntaxFactory.ParseStatement("{ " + string.Join(" ", pre) + " " + result.WithoutTrivia().ToFullString() + " }")
                    .WithTriviaFrom(result);
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                var visited = (BlockSyntax)base.VisitBlock(node);
                return visited.WithStatements(ExpandHoisted(visited.Statements));
            }

            public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
            {
                var visited = (SwitchSectionSyntax)base.VisitSwitchSection(node);
                return visited.WithStatements(ExpandHoisted(visited.Statements));
            }

            private SyntaxList<StatementSyntax> ExpandHoisted(SyntaxList<StatementSyntax> statements)
            {
                if (!statements.Any(s => s.HasAnnotations(HoistAnnotation)))
                    return statements;

                var result = new List<StatementSyntax>();
                foreach (StatementSyntax statement in statements)
                {
                    SyntaxAnnotation annotation = statement.GetAnnotations(HoistAnnotation).FirstOrDefault();
                    if (annotation == null)
                    {
                        result.Add(statement);
                        continue;
                    }
                    // 前に出す文は、元の文の先頭（同じ行）に置く。元の文の前のトリビア（改行・#52 の印）は先頭の文に移す
                    List<string> pre = _hoisted[annotation.Data];
                    for (int i = 0; i < pre.Count; i++)
                    {
                        StatementSyntax s = SyntaxFactory.ParseStatement(pre[i] + " ");
                        result.Add(i == 0 ? s.WithLeadingTrivia(statement.GetLeadingTrivia()) : s);
                    }
                    result.Add(statement.WithoutAnnotations(HoistAnnotation).WithLeadingTrivia(SyntaxFactory.Space));
                }
                return SyntaxFactory.List(result);
            }

            // ---- 型の置き換え ------------------------------------------------------

            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && ReplacementOf(t) is string replaced)
                {
                    Changed = true;
                    return SyntaxFactory.ParseTypeName(replaced).WithTriviaFrom(node);
                }
                return base.VisitGenericName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && ReplacementOf(t) is string replaced)
                {
                    Changed = true;
                    return SyntaxFactory.ParseTypeName(replaced).WithTriviaFrom(node);
                }
                return base.VisitQualifiedName(node);
            }

            // ---- フィールド（シリアライズは未対応） --------------------------------

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                if (_helpers != null && TypeOf(node.Declaration.Type) is ITypeSymbol type && Involves(type))
                {
                    foreach (VariableDeclaratorSyntax v in node.Declaration.Variables)
                    {
                        if (!(_model.GetDeclaredSymbol(v) is IFieldSymbol field))
                            continue;
                        bool serialized = field.DeclaredAccessibility == Accessibility.Public && !HasAttribute(field, "System.NonSerializedAttribute")
                                          || HasAttribute(field, "UnityEngine.SerializeField")
                                          || HasAttribute(field, "UdonSharp.UdonSyncedAttribute");
                        if (serialized)
                            Error(v, $"'{field.Name}' : List / Dictionary 型のフィールドはシリアライズ・同期できません（public・[SerializeField]・[UdonSynced] は未対応です。private にしてください）");
                    }
                }
                return base.VisitFieldDeclaration(node);
            }

            private static bool Involves(ITypeSymbol type)
            {
                while (type is IArrayTypeSymbol array)
                    type = array.ElementType;
                return IsList(type) || IsDictionary(type);
            }

            private static bool HasAttribute(ISymbol symbol, string name) =>
                symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == name);

            // ---- 生成 ------------------------------------------------------------

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
                Creation(node, node.ArgumentList, node.Initializer) ?? base.VisitObjectCreationExpression(node);

            public override SyntaxNode VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
                Creation(node, node.ArgumentList, node.Initializer) ?? base.VisitImplicitObjectCreationExpression(node);

            private SyntaxNode Creation(ExpressionSyntax node, ArgumentListSyntax arguments, InitializerExpressionSyntax initializer)
            {
                ITypeSymbol type = TypeOf(node);
                if (_helpers == null || !(IsList(type) || IsDictionary(type)))
                    return null;

                var args = arguments?.Arguments ?? default;
                string created;
                if (args.Count == 0 || args.Count == 1 && TypeOf(args[0].Expression)?.SpecialType == SpecialType.System_Int32)
                {
                    created = IsList(type) ? $"new {DataListType}()" : $"new {DataDictionaryType}()";   // 容量の指定は使わない
                }
                else if (args.Count == 1 && IsList(type) && TypeOf(args[0].Expression) is ITypeSymbol source)
                {
                    string value = Text(Visit(args[0].Expression));
                    if (IsList(source))
                        created = $"({value}).ShallowClone()";
                    else if (source is IArrayTypeSymbol array)
                        created = $"{FromArrayHelper(array.ElementType)}({value})";
                    else
                    {
                        Error(node, $"'{Text(node)}' : List をこの形で作るのは未対応です（List・配列からのコピーだけ使えます）");
                        return node;
                    }
                }
                else if (args.Count == 1 && IsDictionary(type) && IsDictionary(TypeOf(args[0].Expression)))
                {
                    created = $"({Text(Visit(args[0].Expression))}).ShallowClone()";
                }
                else
                {
                    Error(node, $"'{Text(node)}' : List / Dictionary をこの形で作るのは未対応です");
                    return node;
                }

                if (initializer != null && initializer.Expressions.Count > 0)
                {
                    if (IsList(type))
                    {
                        ITypeSymbol element = Element(type);
                        string items = string.Join(", ", initializer.Expressions.Select(e => Wrap(Text(Visit(e)), element)));
                        created = args.Count == 0
                            ? $"new {DataListType}(new {DataTokenType}[] {{ {items} }})"
                            : $"{AddRangeHelper()}({created}, new {DataTokenType}[] {{ {items} }})";
                    }
                    else
                    {
                        var keys = new List<string>();
                        var values = new List<string>();
                        foreach (ExpressionSyntax e in initializer.Expressions)
                        {
                            switch (e)
                            {
                                case InitializerExpressionSyntax pair when pair.Expressions.Count == 2:   // { k, v }
                                    keys.Add(Wrap(Text(Visit(pair.Expressions[0])), KeyOf(type)));
                                    values.Add(Wrap(Text(Visit(pair.Expressions[1])), ValueOf(type)));
                                    break;
                                case AssignmentExpressionSyntax a when a.Left is ImplicitElementAccessSyntax index && index.ArgumentList.Arguments.Count == 1:   // [k] = v
                                    keys.Add(Wrap(Text(Visit(index.ArgumentList.Arguments[0].Expression)), KeyOf(type)));
                                    values.Add(Wrap(Text(Visit(a.Right)), ValueOf(type)));
                                    break;
                                default:
                                    Error(e, "この形の Dictionary の初期化子は未対応です（{ キー, 値 } か [キー] = 値 を使ってください）");
                                    return node;
                            }
                        }
                        created = $"{DictionaryHelper()}({created}, new {DataTokenType}[] {{ {string.Join(", ", keys)} }}, new {DataTokenType}[] {{ {string.Join(", ", values)} }})";
                    }
                }

                return Parse(created, node);
            }

            // ---- 添字 ------------------------------------------------------------

            /// <summary>list[i] / dict[k] の、DataList / DataDictionary に対する添字の式（取り出し前の DataToken）</summary>
            private string RawElement(ElementAccessExpressionSyntax node, ITypeSymbol receiverType)
            {
                string receiver = Text(Visit(node.Expression));
                ExpressionSyntax index = node.ArgumentList.Arguments[0].Expression;
                string key = IsList(receiverType) ? Text(Visit(index)) : Wrap(Text(Visit(index)), KeyOf(receiverType));
                return $"({receiver})[{key}]";
            }

            private static ITypeSymbol ElementTypeOf(ITypeSymbol receiverType) =>
                IsList(receiverType) ? Element(receiverType) : ValueOf(receiverType);

            private bool IsCollectionAccess(ExpressionSyntax node, out ITypeSymbol receiverType)
            {
                receiverType = null;
                if (_helpers == null || !(node is ElementAccessExpressionSyntax access) || access.ArgumentList.Arguments.Count != 1)
                    return false;
                receiverType = TypeOf(access.Expression);
                return IsList(receiverType) || IsDictionary(receiverType);
            }

            public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
            {
                if (!IsCollectionAccess(node, out ITypeSymbol receiverType))
                    return base.VisitElementAccessExpression(node);
                return Parse(Unwrap(RawElement(node, receiverType), ElementTypeOf(receiverType)), node);
            }

            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                if (!IsCollectionAccess(node.Left, out ITypeSymbol receiverType))
                    return base.VisitAssignmentExpression(node);

                var target = (ElementAccessExpressionSyntax)node.Left;
                if (!(node.Parent is ExpressionStatementSyntax))
                {
                    Error(node, "List / Dictionary の要素への代入は、文としてだけ使えます（代入の値を式として使うのは未対応です）");
                    return node;
                }

                ITypeSymbol element = ElementTypeOf(receiverType);
                string raw = RawElement(target, receiverType);
                if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    return Parse($"{raw} = {Wrap(Text(Visit(node.Right)), element)}", node);

                // 複合代入：受け側と添字を 2 回評価するので、副作用のない形に限る
                if (!IsSimple(target.Expression) || !IsSimple(target.ArgumentList.Arguments[0].Expression))
                {
                    Error(node, "List / Dictionary の要素への複合代入は、受け側と添字が変数・フィールド・定数のときだけ使えます（いったん変数に入れてください）");
                    return node;
                }
                string op = node.OperatorToken.ValueText.TrimEnd('=');
                return Parse($"{raw} = {Wrap($"({Unwrap(raw, element)}) {op} ({Text(Visit(node.Right))})", element)}", node);
            }

            public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node) =>
                IncrementElement(node, node.Operand, node.OperatorToken) ?? base.VisitPostfixUnaryExpression(node);

            public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node) =>
                IncrementElement(node, node.Operand, node.OperatorToken) ?? base.VisitPrefixUnaryExpression(node);

            private SyntaxNode IncrementElement(ExpressionSyntax node, ExpressionSyntax operand, SyntaxToken op)
            {
                if (!(op.IsKind(SyntaxKind.PlusPlusToken) || op.IsKind(SyntaxKind.MinusMinusToken)) || !IsCollectionAccess(operand, out ITypeSymbol receiverType))
                    return null;

                var target = (ElementAccessExpressionSyntax)operand;
                if (!(node.Parent is ExpressionStatementSyntax) || !IsSimple(target.Expression) || !IsSimple(target.ArgumentList.Arguments[0].Expression))
                {
                    Error(node, "List / Dictionary の要素の ++ / -- は、文として、受け側と添字が変数・フィールド・定数のときだけ使えます");
                    return node;
                }
                ITypeSymbol element = ElementTypeOf(receiverType);
                string raw = RawElement(target, receiverType);
                string sign = op.IsKind(SyntaxKind.PlusPlusToken) ? "+" : "-";
                return Parse($"{raw} = {Wrap($"({Unwrap(raw, element)}) {sign} 1", element)}", node);
            }

            private static bool IsSimple(ExpressionSyntax e) =>
                e is IdentifierNameSyntax || e is LiteralExpressionSyntax || e is ThisExpressionSyntax
                || e is MemberAccessExpressionSyntax m && (m.Expression is ThisExpressionSyntax || m.Expression is IdentifierNameSyntax)
                || e is ParenthesizedExpressionSyntax p && IsSimple(p.Expression);

            // ---- メンバー ----------------------------------------------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (_helpers == null || !(node.Expression is MemberAccessExpressionSyntax access) || !(_model.GetSymbolInfo(node).Symbol is IMethodSymbol method))
                    return base.VisitInvocationExpression(node);

                ITypeSymbol receiverType = TypeOf(access.Expression);
                bool list = IsList(receiverType), dict = IsDictionary(receiverType);
                if (!list && !dict)
                    return base.VisitInvocationExpression(node);

                if (method.ReducedFrom != null)
                {
                    Error(node, $"'{method.Name}' : List / Dictionary に対する拡張メソッド（LINQ など）は未対応です");
                    return node;
                }

                string receiver = Text(Visit(access.Expression));
                var args = node.ArgumentList.Arguments;
                string Arg(int i) => Text(Visit(args[i].Expression));

                if (list)
                {
                    ITypeSymbol element = Element(receiverType);
                    switch (method.Name)
                    {
                        case "Add" when args.Count == 1:
                            return Parse($"({receiver}).Add({Wrap(Arg(0), element)})", node);
                        case "Insert" when args.Count == 2:
                            return Parse($"({receiver}).Insert({Arg(0)}, {Wrap(Arg(1), element)})", node);
                        case "Remove" when args.Count == 1:
                        case "Contains" when args.Count == 1:
                            return Parse($"({receiver}).{method.Name}({Wrap(Arg(0), element)})", node);
                        case "IndexOf" when args.Count >= 1:
                        case "LastIndexOf" when args.Count >= 1:
                            return Parse($"({receiver}).{method.Name}({string.Join(", ", new[] { Wrap(Arg(0), element) }.Concat(Enumerable.Range(1, args.Count - 1).Select(Arg)))})", node);
                        case "RemoveAt" when args.Count == 1:
                        case "RemoveRange" when args.Count == 2:
                        case "GetRange" when args.Count == 2:
                        case "Clear" when args.Count == 0:
                        case "TrimExcess" when args.Count == 0:
                        case "Reverse" when args.Count == 0:
                        case "Sort" when args.Count == 0:
                            return Parse($"({receiver}).{method.Name}({string.Join(", ", Enumerable.Range(0, args.Count).Select(Arg))})", node);
                        case "AddRange" when args.Count == 1:
                            return Parse($"({receiver}).AddRange({ListFrom(args[0].Expression)})", node);
                        case "InsertRange" when args.Count == 2:
                            return Parse($"({receiver}).InsertRange({Arg(0)}, {ListFrom(args[1].Expression)})", node);
                        case "ToArray" when args.Count == 0:
                            return Parse($"{ToArrayHelper(element)}({receiver})", node);
                    }
                }
                else
                {
                    ITypeSymbol key = KeyOf(receiverType), value = ValueOf(receiverType);
                    switch (method.Name)
                    {
                        case "Add" when args.Count == 2:
                            return Parse($"({receiver}).Add({Wrap(Arg(0), key)}, {Wrap(Arg(1), value)})", node);
                        case "Remove" when args.Count == 1:
                        case "ContainsKey" when args.Count == 1:
                            return Parse($"({receiver}).{method.Name}({Wrap(Arg(0), key)})", node);
                        case "ContainsValue" when args.Count == 1:
                            return Parse($"({receiver}).ContainsValue({Wrap(Arg(0), value)})", node);
                        case "Clear" when args.Count == 0:
                            return Parse($"({receiver}).Clear()", node);
                        case "TryGetValue" when args.Count == 2:
                            return TryGetValue(node, receiver, args, key, value);
                    }
                }

                Error(node, $"'{receiverType.Name}.{method.Name}' : List / Dictionary のこのメソッド（この引数の形）は未対応です");
                return node;
            }

            /// <summary>AddRange などの引数：List はそのまま、配列は DataList にする</summary>
            private string ListFrom(ExpressionSyntax argument)
            {
                ITypeSymbol type = TypeOf(argument);
                string value = Text(Visit(argument));
                if (IsList(type))
                    return value;
                if (type is IArrayTypeSymbol array)
                    return $"{FromArrayHelper(array.ElementType)}({value})";
                Error(argument, "List / 配列以外からの追加は未対応です");
                return value;
            }

            /// <summary>
            /// dict.TryGetValue(k, out var v) → 文の前に
            ///   DataToken __t; bool __ok = d.TryGetValue(k, out __t); int v = __ok ? __t.Int : default(int);
            /// を出し、呼び出しを __ok にする（out の値を変換するため）
            /// </summary>
            private SyntaxNode TryGetValue(InvocationExpressionSyntax node, string receiver, SeparatedSyntaxList<ArgumentSyntax> args, ITypeSymbol key, ITypeSymbol value)
            {
                if (_pending == null || InLoopCondition(node))
                {
                    Error(node, "Dictionary.TryGetValue は、ループの条件やフィールドの初期化子の中では未対応です（前の文で呼んでください）");
                    return node;
                }

                ArgumentSyntax outArg = args[1];
                string token = Temp("t"), ok = Temp("ok");
                string typeText = TypeText(value);
                string result = $"{ok} ? {Unwrap(token, value)} : default({typeText})";

                _pending.Add($"{DataTokenType} {token};");
                _pending.Add($"bool {ok} = ({receiver}).TryGetValue({Wrap(Text(Visit(args[0].Expression)), key)}, out {token});");
                switch (outArg.Expression)
                {
                    case DeclarationExpressionSyntax declaration when declaration.Designation is SingleVariableDesignationSyntax name:
                        _pending.Add($"{typeText} {name.Identifier.ValueText} = {result};");
                        break;
                    case DeclarationExpressionSyntax _:
                        _pending.Add($"{typeText} {token}_discard = {result};");   // out _
                        break;
                    default:
                        _pending.Add($"{Text(Visit(outArg.Expression))} = {result};");
                        break;
                }
                return Parse(ok, node);
            }

            private static bool InLoopCondition(SyntaxNode node)
            {
                for (SyntaxNode n = node; n.Parent != null; n = n.Parent)
                {
                    switch (n.Parent)
                    {
                        case WhileStatementSyntax w when w.Condition == n:
                        case DoStatementSyntax d when d.Condition == n:
                        case ForStatementSyntax f when f.Condition == n || f.Incrementors.Contains(n):
                            return true;
                        case StatementSyntax _:
                            return false;
                    }
                }
                return false;
            }

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (_helpers == null)
                    return base.VisitMemberAccessExpression(node);

                // foreach (var kv in dict) の kv.Key / kv.Value
                if (node.Expression is IdentifierNameSyntax id && _model.GetSymbolInfo(id).Symbol is ILocalSymbol local && _pairs.TryGetValue(local, out PairLoop pair))
                {
                    switch (node.Name.Identifier.ValueText)
                    {
                        case "Key": return Parse(Unwrap(pair.Key, pair.KeyType), node);
                        case "Value": return Parse(Unwrap($"({pair.Dictionary})[{pair.Key}]", pair.ValueType), node);
                    }
                }

                ITypeSymbol receiverType = TypeOf(node.Expression);
                string name = node.Name.Identifier.ValueText;

                // dict.Keys.Count / dict.Values.Count → dict.Count
                if ((IsKeys(receiverType) || IsValues(receiverType)) && name == "Count" && node.Expression is MemberAccessExpressionSyntax inner)
                    return Parse($"({Text(Visit(inner.Expression))}).Count", node);

                if ((IsList(receiverType) || IsDictionary(receiverType)) && !(_model.GetSymbolInfo(node).Symbol is IMethodSymbol))
                {
                    if (name == "Count" || name == "Capacity" && IsList(receiverType))
                        return base.VisitMemberAccessExpression(node);
                    if ((name == "Keys" || name == "Values") && node.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name.Identifier.ValueText == "Count")
                        return base.VisitMemberAccessExpression(node);
                    if ((name == "Keys" || name == "Values") && node.Parent is ForEachStatementSyntax)
                        return base.VisitMemberAccessExpression(node);
                    Error(node, $"'{receiverType.Name}.{name}' : List / Dictionary のこのメンバーは未対応です（Keys / Values は foreach と .Count でだけ使えます）");
                    return node;
                }

                return base.VisitMemberAccessExpression(node);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                // kv を kv.Key / kv.Value 以外で使う（ループの外に持ち出すなど）のは未対応
                if (_helpers != null && _pairs.Count > 0 && _model.GetSymbolInfo(node).Symbol is ILocalSymbol local && _pairs.ContainsKey(local)
                    && !(node.Parent is MemberAccessExpressionSyntax m && m.Expression == node))
                {
                    Error(node, $"'{local.Name}' : Dictionary の foreach の要素は、.Key と .Value でだけ使えます");
                    return node;
                }
                return base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
            {
                ITypeSymbol type = TypeOf(node.Expression);
                if (_helpers != null && (IsList(type) || IsDictionary(type)))
                {
                    Error(node, "List / Dictionary への ?. は未対応です");
                    return node;
                }
                return base.VisitConditionalAccessExpression(node);
            }

            public override SyntaxNode VisitArgument(ArgumentSyntax node)
            {
                // List / Dictionary を IEnumerable<T> などとして渡すのは未対応（string.Join(",", list) など）
                if (_helpers != null && node.Expression != null)
                {
                    TypeInfo info = _model.GetTypeInfo(node.Expression);
                    if ((IsList(info.Type) || IsDictionary(info.Type)) && info.ConvertedType != null
                        && !SymbolEqualityComparer.Default.Equals(info.Type, info.ConvertedType)
                        && info.ConvertedType.SpecialType != SpecialType.System_Object)
                    {
                        Error(node, $"'{Text(node.Expression)}' : List / Dictionary を {info.ConvertedType.ToDisplayString()} として渡すのは未対応です");
                        return node;
                    }
                }
                return base.VisitArgument(node);
            }

            // ---- foreach ---------------------------------------------------------

            public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            {
                ITypeSymbol type = _helpers == null ? null : TypeOf(node.Expression);
                bool list = IsList(type), dict = IsDictionary(type), keys = IsKeys(type), values = IsValues(type);
                if (!list && !dict && !keys && !values)
                    return base.VisitForEachStatement(node);

                if (!(_model.GetDeclaredSymbol(node) is ILocalSymbol variable))
                    return base.VisitForEachStatement(node);

                // 回数は最初に 1 回だけ取る（毎回 Count を読む extern を省くため。途中で書き換えるのは C# でも例外になる書き方）
                string i = Temp("i"), n = Temp("n");
                string header, itemDeclaration;
                PairLoop pair = null;

                if (list)
                {
                    string l = Temp("l");
                    header = $"{DataListType} {l} = {Text(Visit(node.Expression))}; int {n} = {l}.Count; for (int {i} = 0; {i} < {n}; {i}++) {{ ";
                    itemDeclaration = $"{TypeText(variable.Type)} {variable.Name} = ({TypeText(variable.Type)})({Unwrap($"{l}[{i}]", Element(type))}); ";
                }
                else if (keys || values)
                {
                    // foreach (var k in dict.Keys) / dict.Values
                    ITypeSymbol dictionary = DictionaryOfCollection(type);
                    string source = node.Expression is MemberAccessExpressionSyntax access ? Text(Visit(access.Expression)) : null;
                    if (source == null)
                    {
                        Error(node.Expression, "Keys / Values は dict.Keys の形で foreach に書いてください");
                        return node;
                    }
                    string l = Temp("l");
                    header = $"{DataListType} {l} = ({source}).{(keys ? "GetKeys" : "GetValues")}(); int {n} = {l}.Count; for (int {i} = 0; {i} < {n}; {i}++) {{ ";
                    ITypeSymbol element = keys ? KeyOf(dictionary) : ValueOf(dictionary);
                    itemDeclaration = $"{TypeText(variable.Type)} {variable.Name} = ({TypeText(variable.Type)})({Unwrap($"{l}[{i}]", element)}); ";
                }
                else
                {
                    // foreach (var kv in dict)：キーで回し、kv.Key / kv.Value を書き換える
                    string d = Temp("d"), k = Temp("k"), t = Temp("t");
                    header = $"{DataDictionaryType} {d} = {Text(Visit(node.Expression))}; {DataListType} {k} = {d}.GetKeys(); int {n} = {k}.Count; for (int {i} = 0; {i} < {n}; {i}++) {{ ";
                    itemDeclaration = $"{DataTokenType} {t} = {k}[{i}]; ";
                    pair = new PairLoop { Dictionary = d, Key = t, KeyType = KeyOf(type), ValueType = ValueOf(type) };
                    _pairs[variable] = pair;
                }

                var body = (StatementSyntax)Visit(node.Statement);
                if (pair != null)
                    _pairs.Remove(variable);

                // 元の行に収める：ヘッダーは foreach の行、閉じ括弧は本体の最後の行
                Changed = true;
                string code = node.GetLeadingTrivia().ToFullString()
                              + "{ " + header + itemDeclaration
                              + node.CloseParenToken.TrailingTrivia.ToFullString()
                              + body.WithoutTrailingTrivia().ToFullString()
                              + " } }"
                              + body.GetTrailingTrivia().ToFullString();
                return SyntaxFactory.ParseStatement(code);
            }

            public override SyntaxNode VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
            {
                ITypeSymbol type = _helpers == null ? null : TypeOf(node.Expression);
                if (IsList(type) || IsDictionary(type))
                {
                    Error(node, "List / Dictionary の foreach での分解（var (k, v)）は未対応です。kv.Key / kv.Value を使ってください");
                    return node;
                }
                return base.VisitForEachVariableStatement(node);
            }

            // ---- 補助メソッド ------------------------------------------------------

            private string ToArrayHelper(ITypeSymbol element)
            {
                string name = $"__col_ToArray__{Token(element)}";
                string t = TypeText(element);
                _helpers[name] = $"private static {t}[] {name}({DataListType} __l) {{ {t}[] __a = new {t}[__l.Count]; for (int __i = 0; __i < __a.Length; __i++) __a[__i] = {Unwrap("__l[__i]", element)}; return __a; }}";
                return name;
            }

            private string FromArrayHelper(ITypeSymbol element)
            {
                string name = $"__col_FromArray__{Token(element)}";
                _helpers[name] = $"private static {DataListType} {name}({TypeText(element)}[] __a) {{ {DataListType} __l = new {DataListType}(); for (int __i = 0; __i < __a.Length; __i++) __l.Add({Wrap("__a[__i]", element)}); return __l; }}";
                return name;
            }

            private string AddRangeHelper()
            {
                const string name = "__col_AddItems";
                _helpers[name] = $"private static {DataListType} {name}({DataListType} __l, {DataTokenType}[] __items) {{ for (int __i = 0; __i < __items.Length; __i++) __l.Add(__items[__i]); return __l; }}";
                return name;
            }

            private string DictionaryHelper()
            {
                const string name = "__col_SetItems";
                _helpers[name] = $"private static {DataDictionaryType} {name}({DataDictionaryType} __d, {DataTokenType}[] __k, {DataTokenType}[] __v) {{ for (int __i = 0; __i < __k.Length; __i++) __d.SetValue(__k[__i], __v[__i]); return __d; }}";
                return name;
            }
        }
    }
}
