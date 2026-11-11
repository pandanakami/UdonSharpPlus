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
    internal static partial class CollectionLowering
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
        private const string KeyValuePairName = "System.Collections.Generic.KeyValuePair<TKey, TValue>";
        internal static bool IsKeyValuePair(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == KeyValuePairName;

        /// <summary>KeyValuePair を object[] にしたときの型の印（汎用クラスと同じく先頭に置く。#61）</summary>
        private const string PairTag = "System.Collections.Generic.KeyValuePair";
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

        /// <summary>List / Dictionary・LINQ がありそうか（なければコンパイルを作らずに済ませる）</summary>
        public static bool MayHaveCollections(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes().OfType<GenericNameSyntax>()
                               .Any(g => g.Identifier.ValueText == "List" || g.Identifier.ValueText == "Dictionary")
                           || t.ToString().Contains("System.Linq"));   // 配列への LINQ（#58）

        /// <summary>書き換えた tree の一覧を返す（変わらない tree はそのまま）</summary>
        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics,
            IEnumerable<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_CollectionPrepass",
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

        private sealed partial class Rewriter : CSharpSyntaxRewriter
        {
            private const string HoistAnnotation = "UdonSharpPlus.CollectionHoist";

            private readonly SemanticModel _model;
            private readonly InterfaceLowering.LoweringDiagnostics _diagnostics;
            private readonly Dictionary<ISymbol, PairLoop> _pairs = new Dictionary<ISymbol, PairLoop>(SymbolEqualityComparer.Default);
            private readonly Dictionary<string, List<string>> _hoisted = new Dictionary<string, List<string>>();
            private SortedDictionary<string, string> _helpers;   // 生成する補助メソッド（名前 → コード）。UdonSharpBehaviour の中だけ
            private List<string> _pending;                        // 今の文の前に出す文（TryGetValue）
            private int _temp;
            private int _lambdaCount;                             // 生成するラムダのメソッドの番号（クラスごと）

            public bool Changed { get; private set; }

            public Rewriter(SemanticModel model, InterfaceLowering.LoweringDiagnostics diagnostics)
            {
                _model = model;
                _diagnostics = diagnostics;
            }

            private void Error(SyntaxNode node, string message) => _diagnostics.Error(node.GetLocation(), message);

            /// <summary>
            /// C# としてコンパイルできない箇所か。前の段（ClassLowering など）がエラーにした式は書き換えずに残るので、
            /// 型が合わずにメソッドやラムダを解決できない。そこで「未対応」のエラーを出すと巻き添えになるので出さない（#75）
            /// </summary>
            private bool HasCompileError(SyntaxNode node) =>
                _model.GetDiagnostics(node.Span).Any(d => d.Severity == DiagnosticSeverity.Error);

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
                    if (IsEnumerable(named))
                        return DataListType;   // LINQ の結果（#58）
                    if (IsKeyValuePair(named))
                        return "object[]";     // [型の印, キー, 値]（#61）
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

            /// <summary>
            /// ユーザーが書いた式 → DataToken。普通のクラス・インタフェースなど object で入れる型で、式が null の定数（null、default(Item) など）なら、
            /// new DataToken((object)(object[])null) にする（#89）。new DataToken((object)(null)) は U# がコンパイル時に計算しようとして
            /// コンストラクタが決まらず、内部の例外（AmbiguousMatchException）になるため。途中の (object[]) の変換で定数扱いでなくなる。
            /// 実行時は同じく「参照型で中身が null」のトークンになる。null でない値には (object[]) を挟まない（インタフェースの中身は UdonSharpBehaviour のこともある）
            /// </summary>
            private string WrapExpr(ExpressionSyntax expression, ITypeSymbol type)
            {
                if (!IsToken(type) && type.IsReferenceType && Accessor(type) == null && IsNullConstant(expression))
                    return $"new {DataTokenType}((object)(object[])null)";
                return Wrap(Text(Visit(expression)), type);
            }

            private bool IsNullConstant(ExpressionSyntax expression)
            {
                Optional<object> constant = _model.GetConstantValue(expression);
                return constant.HasValue && constant.Value == null;
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

            /// <summary>foreach / LINQ の中でキーとして流している kv を、object[]（[型の印, キー, 値]）にする式（#61）</summary>
            private static string PairObject(PairLoop pair) =>
                $"new object[] {{ \"{PairTag}\", (object)({Unwrap(pair.Key, pair.KeyType)}), (object)({Unwrap($"({pair.Dictionary})[{pair.Key}]", pair.ValueType)}) }}";

            private static string Token(ITypeSymbol type) => InterfaceNaming.Sanitize(TypeText(type).Replace("global::", ""));

            // ---- クラス・文 ------------------------------------------------------

            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => node;

            // using エイリアス（using IntList = List<int>;）の右側には手を入れない。エイリアスを使う側（IntList x）は、名前が指す型として置き換わる（#79）
            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) => node;

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                // U# がコンパイルするのは UdonSharpBehaviour だけ（汎用クラスはコピーが UdonSharpBehaviour の中にある）
                if (_helpers != null || !(_model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol) || !InterfaceLowering.IsUdonSharpBehaviour(symbol))
                    return node;

                _helpers = new SortedDictionary<string, string>(StringComparer.Ordinal);
                _lambdaCount = 0;
                var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                var helpers = _helpers;
                _helpers = null;

                if (helpers.Count == 0)
                    return visited;
                Changed = true;
                // 生成メンバーは閉じ括弧 '}' と同じ行に置き、行番号を一切ずらさない
                return GeneratedMembers.AppendOnClosingLine(visited, helpers.Values);
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
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol pairType && IsKeyValuePair(pairType))
                {
                    Changed = true;
                    return SyntaxFactory.ParseTypeName("object[]").WithTriviaFrom(node);
                }
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && ReplacementOf(t) is string replaced)
                {
                    Changed = true;
                    return SyntaxFactory.ParseTypeName(replaced).WithTriviaFrom(node);
                }
                return base.VisitGenericName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t
                    && (ReplacementOf(t) ?? (IsKeyValuePair(t) ? "object[]" : null)) is string replaced)
                {
                    // 名前全体を置き換える（右側の KeyValuePair<K, V> だけを object[] にすると、名前の形でなくなる）
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
                if (_helpers != null && IsKeyValuePair(type) && arguments != null && arguments.Arguments.Count == 2 && initializer == null)
                {
                    // new KeyValuePair<K, V>(k, v) → [型の印, キー, 値]（#61）
                    return Parse($"new object[] {{ \"{PairTag}\", (object)({Text(Visit(arguments.Arguments[0].Expression))}), (object)({Text(Visit(arguments.Arguments[1].Expression))}) }}", node);
                }
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
                        string items = string.Join(", ", initializer.Expressions.Select(e => WrapExpr(e, element)));
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
                                    keys.Add(WrapExpr(pair.Expressions[0], KeyOf(type)));
                                    values.Add(WrapExpr(pair.Expressions[1], ValueOf(type)));
                                    break;
                                case AssignmentExpressionSyntax a when a.Left is ImplicitElementAccessSyntax index && index.ArgumentList.Arguments.Count == 1:   // [k] = v
                                    keys.Add(WrapExpr(index.ArgumentList.Arguments[0].Expression, KeyOf(type)));
                                    values.Add(WrapExpr(a.Right, ValueOf(type)));
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
                string key = IsList(receiverType) ? Text(Visit(index)) : WrapExpr(index, KeyOf(receiverType));
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
                if (node.IsKind(SyntaxKind.SimpleAssignmentExpression) && EnumerableConversion(node.Right))   // IEnumerable<int> 型の変数に List を代入（#62）
                    return node;
                if (!IsCollectionAccess(node.Left, out ITypeSymbol receiverType))
                    return base.VisitAssignmentExpression(node);

                var target = (ElementAccessExpressionSyntax)node.Left;
                if (!InterfaceLowering.IsStatementExpression(node, _model))
                {
                    Error(node, "List / Dictionary の要素への代入は、文としてだけ使えます（代入の値を式として使うのは未対応です）");
                    return node;
                }

                ITypeSymbol element = ElementTypeOf(receiverType);
                string raw = RawElement(target, receiverType);
                if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    return Parse($"{raw} = {WrapExpr(node.Right, element)}", node);

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
                if (!InterfaceLowering.IsStatementExpression(node, _model) || !IsSimple(target.Expression) || !IsSimple(target.ArgumentList.Arguments[0].Expression))
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
                || e is ParenthesizedExpressionSyntax p && IsSimple(p.Expression)
                || IsClassSlotRead(e);

            /// <summary>
            /// 普通のクラスのフィールドの読み出し（ClassLowering が u.hits を ((T)(u)[3]) にしたもの）。
            /// 中が単純なら、2 回読んでも結果と副作用が変わらない（#74）
            /// </summary>
            private static bool IsClassSlotRead(ExpressionSyntax e) =>
                e is CastExpressionSyntax cast
                && cast.Expression is ElementAccessExpressionSyntax slot
                && slot.Expression is ParenthesizedExpressionSyntax
                && slot.ArgumentList.Arguments.Count == 1
                && slot.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax
                && IsSimple(slot.Expression);

            // ---- メンバー ----------------------------------------------------------

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                // LINQ（#58）：つないだ呼び出しの一番外側でまとめて書き換える
                if (_helpers != null && _model.GetSymbolInfo(node).Symbol is IMethodSymbol linq && IsLinqMethod(linq))
                    return LinqCall(node);

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

                var args = node.ArgumentList.Arguments;
                if (list && args.Any(a => IsDelegate(a.Expression)))
                    return LambdaCall(node, method, access, receiverType);

                string receiver = Text(Visit(access.Expression));
                string Arg(int i) => Text(Visit(args[i].Expression));

                if (list)
                {
                    ITypeSymbol element = Element(receiverType);
                    switch (method.Name)
                    {
                        case "Add" when args.Count == 1:
                            return Parse($"({receiver}).Add({WrapExpr(args[0].Expression, element)})", node);
                        case "Insert" when args.Count == 2:
                            return Parse($"({receiver}).Insert({Arg(0)}, {WrapExpr(args[1].Expression, element)})", node);
                        case "Remove" when args.Count == 1:
                        case "Contains" when args.Count == 1:
                            return Parse($"({receiver}).{method.Name}({WrapExpr(args[0].Expression, element)})", node);
                        case "IndexOf" when args.Count >= 1:
                        case "LastIndexOf" when args.Count >= 1:
                            return Parse($"({receiver}).{method.Name}({string.Join(", ", new[] { WrapExpr(args[0].Expression, element) }.Concat(Enumerable.Range(1, args.Count - 1).Select(Arg)))})", node);
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
                            return Parse($"({receiver}).Add({WrapExpr(args[0].Expression, key)}, {WrapExpr(args[1].Expression, value)})", node);
                        case "Remove" when args.Count == 1:
                        case "ContainsKey" when args.Count == 1:
                            return Parse($"({receiver}).{method.Name}({WrapExpr(args[0].Expression, key)})", node);
                        case "ContainsValue" when args.Count == 1:
                            return Parse($"({receiver}).ContainsValue({WrapExpr(args[0].Expression, value)})", node);
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
                _pending.Add($"bool {ok} = ({receiver}).TryGetValue({WrapExpr(args[0].Expression, key)}, out {token});");
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
                if (node.Expression is IdentifierNameSyntax id && _model.GetSymbolInfo(id).Symbol is ISymbol local && _pairs.TryGetValue(local, out PairLoop pair))
                {
                    switch (node.Name.Identifier.ValueText)
                    {
                        case "Key": return Parse(Unwrap(pair.Key, pair.KeyType), node);
                        case "Value": return Parse(Unwrap($"({pair.Dictionary})[{pair.Key}]", pair.ValueType), node);
                    }
                }

                ITypeSymbol receiverType = TypeOf(node.Expression);
                string name = node.Name.Identifier.ValueText;

                // KeyValuePair（object[]）の .Key / .Value（#61）
                if (IsKeyValuePair(receiverType) && (name == "Key" || name == "Value"))
                {
                    var pairArgs = ((INamedTypeSymbol)receiverType).TypeArguments;
                    ITypeSymbol part = name == "Key" ? pairArgs[0] : pairArgs[1];
                    return Parse($"(({TypeText(part)})({Text(Visit(node.Expression))})[{(name == "Key" ? 1 : 2)}])", node);
                }

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
                    if (HasCompileError(node.Parent ?? node))
                        return base.VisitMemberAccessExpression(node);
                    Error(node, $"'{receiverType.Name}.{name}' : List / Dictionary のこのメンバーは未対応です（Keys / Values は foreach と .Count でだけ使えます）");
                    return node;
                }

                return base.VisitMemberAccessExpression(node);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                // kv を kv.Key / kv.Value 以外で使う（変数に入れる、メソッドに渡すなど）：その場で object[] を作る（#61）
                if (_helpers != null && _pairs.Count > 0 && _model.GetSymbolInfo(node).Symbol is ISymbol local && _pairs.TryGetValue(local, out PairLoop pair)
                    && !(node.Parent is MemberAccessExpressionSyntax m && m.Expression == node))
                    return Parse($"({PairObject(pair)})", node);
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
                    if (IsEnumerable(info.Type) && IsDataListBacked(node.Expression))
                    {
                        Error(node, $"'{Text(node.Expression)}' : LINQ の結果をそのまま渡すのは未対応です（ToList() / ToArray() にしてください）");
                        return node;
                    }
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
                if (IsEnumerable(type) && !IsDataListBacked(node.Expression))
                {
                    Error(node.Expression, $"'{Text(node.Expression)}' : {ForeignEnumerableMessage}");   // #62
                    return node;
                }
                bool list = IsList(type) || IsEnumerable(type), dict = IsDictionary(type), keys = IsKeys(type), values = IsValues(type);   // IEnumerable は LINQ の結果（DataList）
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

            // ---- ラムダ（#57） ------------------------------------------------------

            /// <summary>ラムダの外の変数（ラムダを含むメソッドのローカル変数・引数）</summary>
            private sealed class Capture
            {
                public string Name;
                public ITypeSymbol Type;
                public bool Written;   // ラムダの中で書かれる → ref で渡す
                public string Arg;     // 呼び出し側で渡す式（null なら Name）
                public string TypeName;   // 生成メソッドの引数の型（null なら TypeText(Type)）
                public ISymbol Pair;   // foreach / LINQ の中の kv（キーとして流しているもの）。object[] にして渡す（#61）
            }

            private static readonly string[] LambdaMethods =
                { "Find", "FindLast", "FindIndex", "FindLastIndex", "FindAll", "ConvertAll", "Exists", "TrueForAll", "RemoveAll", "ForEach", "Sort" };

            /// <summary>範囲（開始・個数）の引数も受け取るメソッド：FindIndex(start, match)、FindIndex(start, count, match)</summary>
            private static bool TakesRange(string op) => op == "FindIndex" || op == "FindLastIndex";

            private bool IsDelegate(ExpressionSyntax e) =>
                e is LambdaExpressionSyntax || e is AnonymousMethodExpressionSyntax
                || _model.GetTypeInfo(e).ConvertedType?.TypeKind == TypeKind.Delegate;

            public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => LambdaElsewhere(node) ?? base.VisitSimpleLambdaExpression(node);
            public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => LambdaElsewhere(node) ?? base.VisitParenthesizedLambdaExpression(node);
            public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => LambdaElsewhere(node) ?? base.VisitAnonymousMethodExpression(node);

            /// <summary>List のメソッドの引数以外のラムダ（書き換えるラムダは、本体だけを Visit するのでここには来ない）</summary>
            private SyntaxNode LambdaElsewhere(ExpressionSyntax node)
            {
                if (_helpers == null)
                    return null;
                if (HasCompileError(node))
                    return null;
                Error(node, "ラムダは List のメソッド（Find / Exists / ForEach など）と LINQ の引数としてだけ使えます");
                return node;
            }

            /// <summary>ラムダを含む本当のメソッド（ラムダの中のラムダなら外側をたどる）。フィールドの初期化子などなら null</summary>
            private IMethodSymbol EnclosingMethod(SyntaxNode node)
            {
                ISymbol s = _model.GetEnclosingSymbol(node.SpanStart);
                while (s is IMethodSymbol m && m.MethodKind == MethodKind.AnonymousFunction)
                    s = s.ContainingSymbol;
                return s as IMethodSymbol;
            }

            /// <summary>
            /// list.Find(x => 式) などを、生成するメソッドの呼び出しにする：
            ///   __lambda_Find_0(list, 読むだけの外の変数, ref 書かれる外の変数)
            /// ループとラムダの本体は生成するメソッドの中に入れる（式の本体はループの中に直接書く。
            /// return のあるブロックの本体は、要素ごとに呼ぶ本体のメソッドにする）
            /// </summary>
            private SyntaxNode LambdaCall(InvocationExpressionSyntax node, IMethodSymbol method, MemberAccessExpressionSyntax access, ITypeSymbol receiverType)
            {
                string op = method.Name;
                var args = node.ArgumentList.Arguments;
                int rangeCount = args.Count - 1;   // ラムダの前の引数（FindIndex の開始・個数）
                if (!LambdaMethods.Contains(op) || rangeCount < 0 || rangeCount > (TakesRange(op) ? 2 : 0)
                    || args.Take(rangeCount).Any(a => IsDelegate(a.Expression))
                    || !(_model.GetTypeInfo(args[rangeCount].Expression).ConvertedType is INamedTypeSymbol delegateType) || delegateType.DelegateInvokeMethod == null)
                {
                    Error(node, $"'List.{op}' : ラムダを渡すこの形は未対応です（{string.Join(" / ", LambdaMethods)} に、ラムダかメソッド名を渡す形が使えます。"
                                + "FindIndex / FindLastIndex は開始・個数の指定も使えます）");
                    return node;
                }

                IMethodSymbol enclosing = EnclosingMethod(node);
                if (enclosing == null)
                {
                    Error(node, "ラムダを渡す List のメソッドは、フィールドの初期化子の中では未対応です（メソッドの中で呼んでください）");
                    return node;
                }

                ExpressionSyntax fn = args[rangeCount].Expression;
                IMethodSymbol invoke = delegateType.DelegateInvokeMethod;
                string name = $"__lambda_{op}_{_lambdaCount++}";
                string modifiers = enclosing.IsStatic ? "private static" : "private";
                ITypeSymbol element = Element(receiverType);
                string elementType = TypeText(element);

                PreparedLambda lambda = PrepareLambda(fn, invoke, name + "_body", modifiers, false);
                if (lambda == null)
                    return node;
                string[] names = lambda.Names;
                string marker = lambda.Marker;
                string capParams = CaptureParams(lambda.Captures);
                string capArgs = CaptureArgs(lambda.Captures);
                string inlineText = lambda.Inline;
                string each = lambda.Each;
                string item(string token, int i) => $"{elementType} {names[i]} = {Unwrap(token, element)}; ";
                string forward = $"int __n = __l.Count; for (int __i = 0; __i < __n; __i++) {{ {item("__l[__i]", 0)}";
                string backward = $"for (int __i = __l.Count - 1; __i >= 0; __i--) {{ {item("__l[__i]", 0)}";
                // 範囲の指定（C# と同じ範囲を調べる。範囲外の指定は、C# の ArgumentOutOfRangeException ではなく、Udon の実行時例外か結果なしになる）
                string rangeParams = rangeCount == 0 ? "" : rangeCount == 1 ? ", int __start" : ", int __start, int __count";
                string rangeArgs = string.Concat(args.Take(rangeCount).Select(a => ", " + Text(Visit(a.Expression))));
                if (rangeCount == 1)
                {
                    forward = $"int __n = __l.Count; for (int __i = __start; __i < __n; __i++) {{ {item("__l[__i]", 0)}";
                    backward = $"for (int __i = __start; __i >= 0; __i--) {{ {item("__l[__i]", 0)}";
                }
                else if (rangeCount == 2)
                {
                    forward = $"int __n = __start + __count; for (int __i = __start; __i < __n; __i++) {{ {item("__l[__i]", 0)}";
                    backward = $"int __end = __start - __count; for (int __i = __start; __i > __end; __i--) {{ {item("__l[__i]", 0)}";
                }

                string returnType, code;
                switch (op)
                {
                    case "Find":
                    case "FindLast":
                        returnType = elementType;
                        code = (op == "Find" ? forward : backward) + $"if ({each}) return {names[0]}; }} return default({elementType});";
                        break;
                    case "FindIndex":
                    case "FindLastIndex":
                        returnType = "int";
                        code = (op == "FindIndex" ? forward : backward) + $"if ({each}) return __i; }} return -1;";
                        break;
                    case "Exists":
                        returnType = "bool";
                        code = forward + $"if ({each}) return true; }} return false;";
                        break;
                    case "TrueForAll":
                        returnType = "bool";
                        code = forward + $"if (!({each})) return false; }} return true;";
                        break;
                    case "FindAll":
                        returnType = DataListType;
                        code = $"{DataListType} __r = new {DataListType}(); " + forward + $"if ({each}) __r.Add(__l[__i]); }} return __r;";
                        break;
                    case "ConvertAll":
                        // 変換後の型（TOutput）で DataToken にして、新しい DataList に入れる
                        returnType = DataListType;
                        code = $"{DataListType} __r = new {DataListType}(); " + forward + $"__r.Add({Wrap(each, invoke.ReturnType)}); }} return __r;";
                        break;
                    case "RemoveAll":
                        // 残すものを前に詰めてから、末尾をまとめて消す（判定は C# と同じく前から 1 回ずつ）
                        returnType = "int";
                        code = forward + $"if (!({each})) {{ if (__j != __i) __l[__j] = __l[__i]; __j++; }} }} if (__j < __n) __l.RemoveRange(__j, __n - __j); return __n - __j;";
                        code = "int __j = 0; " + code;
                        break;
                    case "ForEach":
                        returnType = "void";
                        code = forward + (inlineText != null ? $"{marker}{inlineText}" : $"{each};") + " }";
                        break;
                    case "Sort":
                        // 安定なマージソート（ボトムアップ）。比較は 1 か所なので、ラムダの本体も 1 回だけ書く
                        returnType = "void";
                        code = $"int __n = __l.Count; if (__n < 2) return; "
                               + $"{DataTokenType}[] __a = new {DataTokenType}[__n]; {DataTokenType}[] __b = new {DataTokenType}[__n]; "
                               + "for (int __i = 0; __i < __n; __i++) __a[__i] = __l[__i]; "
                               + "for (int __w = 1; __w < __n; __w *= 2) { "
                               + "for (int __lo = 0; __lo < __n; __lo += 2 * __w) { "
                               + "int __mid = __lo + __w; if (__mid > __n) __mid = __n; int __hi = __lo + 2 * __w; if (__hi > __n) __hi = __n; "
                               + "int __p = __lo; int __q = __mid; "
                               + "for (int __k = __lo; __k < __hi; __k++) { bool __left; "
                               + "if (__p >= __mid) __left = false; else if (__q >= __hi) __left = true; "
                               + $"else {{ {item("__a[__p]", 0)}{item("__a[__q]", 1)}__left = ({each}) <= 0; }} "
                               + "if (__left) { __b[__k] = __a[__p]; __p++; } else { __b[__k] = __a[__q]; __q++; } } } "
                               + $"{DataTokenType}[] __s = __a; __a = __b; __b = __s; }} "
                               + "for (int __i = 0; __i < __n; __i++) __l[__i] = __a[__i];";
                        break;
                    default:
                        return node;
                }

                _helpers[name] = $"{modifiers} {returnType} {name}({DataListType} __l{rangeParams}{capParams}) {{ {marker}{code} }}";
                return Parse($"{name}({Text(Visit(access.Expression))}{rangeArgs}{capArgs})", node);
            }

            /// <summary>生成メソッドに埋め込むための、1 つのラムダ（またはメソッド名）の準備結果</summary>
            private sealed class PreparedLambda
            {
                public string[] Names;           // ラムダの引数の名前（メソッド名なら __a0 など）
                public List<Capture> Captures;   // ラムダの外の変数
                public string Marker;            // 元の位置の印（#52）
                public string Each;              // 1 回分を評価する式（印付き）。Inline のときは null
                public string Inline;            // 戻り値のない、return のないブロックの本体（そのまま埋め込む）
            }

            private static string CaptureParams(IEnumerable<Capture> captures) =>
                string.Concat(captures.Select(c => $", {(c.Written ? "ref " : "")}{c.TypeName ?? TypeText(c.Type)} {c.Name}"));

            private static string CaptureArgs(IEnumerable<Capture> captures) =>
                string.Concat(captures.Select(c => $", {(c.Written ? "ref " : "")}{c.Arg ?? c.Name}"));

            /// <summary>
            /// ラムダ（式・ブロック）かメソッド名を、生成メソッドに埋め込める形にする。だめなら null（エラーは出し済み）。
            /// return のあるブロック（と bodyMethod のとき）は、本体を bodyName のメソッドにして、その呼び出しを Each にする
            /// </summary>
            private PreparedLambda PrepareLambda(ExpressionSyntax fn, IMethodSymbol invoke, string bodyName, string modifiers, bool bodyMethod)
            {
                int arity = invoke.Parameters.Length;
                string[] names;
                CSharpSyntaxNode body;
                switch (fn)
                {
                    case SimpleLambdaExpressionSyntax s:
                        names = new[] { s.Parameter.Identifier.ValueText };
                        body = s.Body;
                        break;
                    case ParenthesizedLambdaExpressionSyntax p:
                        names = p.ParameterList.Parameters.Select(x => x.Identifier.ValueText).ToArray();
                        body = p.Body;
                        break;
                    case AnonymousMethodExpressionSyntax _:
                        Error(fn, "delegate { ... } の形は未対応です。ラムダ（x => ...）で書いてください");
                        return null;
                    default:
                        if (!(_model.GetSymbolInfo(fn).Symbol is IMethodSymbol))
                        {
                            Error(fn, "デリゲートの変数を渡すのは未対応です（ラムダかメソッド名を直接書いてください）");
                            return null;
                        }
                        names = Enumerable.Range(0, arity).Select(i => $"__a{i}").ToArray();
                        body = null;
                        break;
                }
                if (names.Length != arity)
                    return null;   // C# のエラーになる

                List<Capture> captures = Captures(fn, body ?? fn);   // メソッド名なら、受け側（other.IsGood の other）
                if (captures == null)
                    return null;

                var result = new PreparedLambda { Names = names, Captures = captures, Marker = SourceMap.MarkerFor(fn) };
                // 外の kv は object[] の引数として渡すので、本体の中では普通の KeyValuePair として書き換える
                var hidden = new List<KeyValuePair<ISymbol, PairLoop>>();
                foreach (Capture c in captures.Where(c => c.Pair != null))
                {
                    hidden.Add(new KeyValuePair<ISymbol, PairLoop>(c.Pair, _pairs[c.Pair]));
                    _pairs.Remove(c.Pair);
                }
                try
                {
                    return PrepareLambdaBody(result, fn, invoke, body, names, captures, bodyName, modifiers, bodyMethod);
                }
                finally
                {
                    foreach (var h in hidden)
                        _pairs[h.Key] = h.Value;
                }
            }

            private PreparedLambda PrepareLambdaBody(PreparedLambda result, ExpressionSyntax fn, IMethodSymbol invoke, CSharpSyntaxNode body,
                string[] names, List<Capture> captures, string bodyName, string modifiers, bool bodyMethod)
            {
                string ret = invoke.ReturnsVoid ? "void" : TypeText(invoke.ReturnType);
                string invokeText;
                string blockText;
                if (body == null)
                {
                    invokeText = $"{Text(Visit(fn))}({string.Join(", ", names)})";
                    blockText = null;
                }
                else if (body is ExpressionSyntax expression)
                {
                    List<string> saved = _pending;
                    _pending = null;   // 式の中の TryGetValue は前に出せない（エラーにする）
                    string text = Flatten(Visit(expression), expression);
                    _pending = saved;
                    if (text == null)
                        return null;
                    invokeText = text;
                    blockText = invoke.ReturnsVoid ? $"{{ {text}; }}" : $"{{ return {text}; }}";
                }
                else
                {
                    var block = (BlockSyntax)body;
                    blockText = BlockText(block);
                    if (blockText == null)
                        return null;
                    bool hasReturn = block.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax)).OfType<ReturnStatementSyntax>().Any();
                    if (invoke.ReturnsVoid && !hasReturn && !bodyMethod)
                    {
                        result.Inline = blockText;
                        return result;
                    }
                    invokeText = null;
                    bodyMethod = true;   // return があるので、本体を要素ごとに呼ぶメソッドにする
                }

                if (bodyMethod && blockText != null)
                {
                    string bodyParams = string.Join(", ", names.Select((n, i) => $"{TypeText(invoke.Parameters[i].Type)} {n}")) + CaptureParams(captures);
                    _helpers[bodyName] = $"{modifiers} {ret} {bodyName}({bodyParams}) {result.Marker}{blockText}";
                    invokeText = $"{bodyName}({string.Join(", ", names)}{CaptureArgs(captures)})";
                }
                result.Each = $"{result.Marker}{invokeText}";
                return result;
            }

            /// <summary>ラムダの外の変数を、出てくる順に集める。書かれるかは Roslyn のデータフロー解析で判定する</summary>
            private List<Capture> Captures(ExpressionSyntax lambda, CSharpSyntaxNode body)
            {
                var result = new List<Capture>();
                if (body == null)
                    return result;

                DataFlowAnalysis flow = null;
                try
                {
                    flow = body is ExpressionSyntax e ? _model.AnalyzeDataFlow(e) : _model.AnalyzeDataFlow((StatementSyntax)body);
                }
                catch (ArgumentException)
                {
                    // メソッド名など、値でない式は解析できない（書き込みはない）
                }
                var written = new HashSet<ISymbol>(flow != null && flow.Succeeded ? flow.WrittenInside : Enumerable.Empty<ISymbol>(), SymbolEqualityComparer.Default);
                var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                bool ok = true;
                foreach (IdentifierNameSyntax id in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    ISymbol symbol = _model.GetSymbolInfo(id).Symbol;
                    ITypeSymbol type = symbol is ILocalSymbol local ? local.Type
                        : symbol is IParameterSymbol parameter && !parameter.IsThis ? parameter.Type
                        : null;
                    if (type == null)
                        continue;
                    // ラムダの中で宣言したもの（ラムダの引数も）は外の変数ではない
                    if (symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(r.Span)))
                        continue;
                    if (!seen.Add(symbol))
                        continue;
                    if (_pairs.TryGetValue(symbol, out PairLoop pair))
                    {
                        // キーとして流している kv：object[] にして渡す（生成メソッドの中では KeyValuePair 型の引数として扱う）
                        result.Add(new Capture { Name = symbol.Name, Type = type, Arg = PairObject(pair), Pair = symbol });
                        continue;
                    }
                    // 外部の関数が返す IEnumerable（DataList でない）は、元の型のまま渡す（#62）
                    string typeName = IsEnumerable(type) && !(symbol is ILocalSymbol && IsDataListBacked(id))
                        ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        : null;
                    result.Add(new Capture { Name = symbol.Name, Type = type, TypeName = typeName, Written = written.Contains(symbol) });
                }
                return ok ? result : null;
            }

            /// <summary>ブロックの本体を 1 行にする。文ごとに元の位置の印を付ける（実行時のエラーの位置を元の行に戻すため）</summary>
            private string BlockText(BlockSyntax block)
            {
                var statements = new List<StatementSyntax>();
                foreach (StatementSyntax statement in block.Statements)
                {
                    var visited = (StatementSyntax)Visit(statement);
                    statements.Add(visited.WithLeadingTrivia(SyntaxFactory.Comment(SourceMap.MarkerFor(statement))));
                }
                var parts = new List<string>();
                foreach (StatementSyntax statement in ExpandHoisted(SyntaxFactory.List(statements)))
                {
                    string text = Flatten(statement, block);
                    if (text == null)
                        return null;
                    parts.Add(text);
                }
                return "{ " + string.Join(" ", parts) + " }";
            }

            /// <summary>
            /// 生成メソッドに入れるため 1 行にする（生成メンバーはクラス末尾の '}' の行に置くので、改行があると後ろの行がずれる）。
            /// 位置の印（#52）は残す。だめなときは null
            /// </summary>
            private string Flatten(SyntaxNode node, SyntaxNode original)
            {
                if (node.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective))
                {
                    Error(original, "ラムダの中のプリプロセッサ指令（#if など）は未対応です");
                    return null;
                }
                SyntaxNode flat = node.ReplaceTokens(
                    node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken) && t.Text.Contains("\n")),
                    (o, _) => SyntaxFactory.Literal(o.ValueText).WithTriviaFrom(o));
                flat = flat.ReplaceTrivia(flat.DescendantTrivia(descendIntoTrivia: false),
                    (o, _) => SourceMap.IsMarker(o) ? o : SyntaxFactory.Space);
                string text = flat.ToFullString().Trim();
                if (text.Contains("\n"))
                {
                    Error(original, "ラムダの中の、改行を含む補間文字列は未対応です");
                    return null;
                }
                return text;
            }

            // ---- 補助メソッド ------------------------------------------------------

            private string ToArrayHelper(ITypeSymbol element)
            {
                string name = $"__col_ToArray__{Token(element)}";
                string t = TypeText(element);
                _helpers[name] = $"private static {t}[] {name}({DataListType} __l) {{ {t}[] __a = {NewArray(t, "__l.Count")}; for (int __i = 0; __i < __a.Length; __i++) __a[__i] = {Unwrap("__l[__i]", element)}; return __a; }}";
                return name;
            }

            /// <summary>要素の型が t の配列を作る式（t が object[] なら new object[n][]）</summary>
            private static string NewArray(string t, string size)
            {
                int bracket = t.IndexOf('[');
                return bracket < 0 ? $"new {t}[{size}]" : $"new {t.Substring(0, bracket)}[{size}]{t.Substring(bracket)}";
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
