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
    /// UdonSharpBehaviour 以外のユーザー定義クラス（汎用クラス）を、UdonSharp が扱える形へ書き換える（Redmine #49）。
    ///
    ///   インスタンス   : object[]。0 番目に型タグ（型の完全名）、1 番目に型番号（int。インタフェースの呼び出しの振り分けに使う。#65）、
    ///                    2 番目以降にフィールド（自動実装プロパティを含む）
    ///   クラスの配列   : Bullet[] → object[][]
    ///   new Bullet(a)  → __cls_Ns_Bullet__ctor__T(a)
    ///   b.Move(d)      → __cls_Ns_Bullet__Move__T(b, d)
    ///   b.damage       → ((int)(b)[3])、 b.damage = v → (b)[3] = (int)(v)
    ///   b is Bullet    → __cls_Ns_Bullet__is((object)(b))
    ///
    /// コンストラクタ・メソッド・プロパティのアクセサーは、使う UdonSharpBehaviour ごとに private static メソッドとして
    /// コピーを生成する（クラス末尾の '}' と同じ行に置き、行番号をずらさない）。
    /// 状態はすべて object[] の中にあり、どのプログラムも同じソースから同じ並びを作るので、
    /// インスタンスを別の Behaviour に渡しても、渡した先のコピーで扱える。
    ///
    /// 検査（継承・static フィールドなどのエラー）は、UdonSharpBehaviour から実際に使われるクラスにだけ行う。
    /// （同じアセンブリにある、Udon と関係のない普通のクラスにエラーを出さないため）
    /// </summary>
    internal static class ClassLowering
    {
        internal const string InstanceType = "object[]";
        internal const string ThisName = "__this";

        /// <summary>インスタンスの 0 番目は型タグ、1 番目は型番号（#65）。フィールドはその後ろから</summary>
        private const int FirstSlot = 2;

        internal sealed class ClassInfo
        {
            public INamedTypeSymbol Symbol;
            public string Key;                 // 生成名に使う（完全名を識別子に使える文字へ）
            public string Tag;                 // 型タグ（完全名）
            public readonly List<ISymbol> SlotOrder = new List<ISymbol>();
            public readonly Dictionary<ISymbol, int> Slots = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
            public bool Validated;
            public bool Valid = true;

            public int Size => SlotOrder.Count + FirstSlot;
            public string IsHelper => $"__cls_{Key}__is";
            public string AsHelper => $"__cls_{Key}__as";
        }

        /// <summary>生成するヘルパーの使用状況（UdonSharpBehaviour ごと）</summary>
        private sealed class Sink
        {
            public readonly HashSet<ClassInfo> IsHelpers = new HashSet<ClassInfo>();
            public readonly HashSet<ClassInfo> AsHelpers = new HashSet<ClassInfo>();
        }

        /// <summary>
        /// 汎用クラス（とエラーにする構造体・外部のクラスの派生。#54, #55）がありそうか（なければコンパイルを作らずに済ませる）。
        /// 基底が UdonSharpBehaviour と書かれていないクラス（ScriptableObject の派生や、自前の基底 Behaviour の派生など）があれば調べる
        /// </summary>
        public static bool MayHaveClasses(IEnumerable<SyntaxTree> trees) =>
            trees.Any(t => t.GetRoot().DescendantNodes(n => !(n is BlockSyntax))
                .Any(n => n is StructDeclarationSyntax
                          || n is ClassDeclarationSyntax c && (c.BaseList == null || c.Modifiers.Any(SyntaxKind.StaticKeyword)
                                                               || !c.BaseList.Types.Any(b => b.Type.ToString().EndsWith("UdonSharpBehaviour", StringComparison.Ordinal)))));

        /// <summary>型タグ（完全名）。ClassInfo.Tag と同じ表記</summary>
        internal static string TagOf(INamedTypeSymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        /// <summary>書き換えた tree の一覧を返す（変わらない tree はそのまま）</summary>
        /// <param name="usedClasses">UdonSharpBehaviour から使われる汎用クラスの型タグを入れて返す</param>
        public static List<SyntaxTree> Run(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics,
            IEnumerable<MetadataReference> references, HashSet<string> usedClasses = null, IReadOnlyDictionary<string, int> classNumbers = null)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_ClassPrepass",
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var classes = CollectClasses(compilation, trees);
            var context = new Context(compilation, classes, diagnostics, classNumbers);

            // UdonSharpBehaviour ごとに、使う汎用クラス（推移的に）を求める
            var hosts = new Dictionary<INamedTypeSymbol, (ClassDeclarationSyntax Host, List<ClassInfo> Used)>(SymbolEqualityComparer.Default);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol usb) || !InterfaceLowering.IsUdonSharpBehaviour(usb))
                        continue;

                    var used = context.Closure(context.Referenced(decl, model));
                    if (hosts.TryGetValue(usb, out var existing))
                    {
                        foreach (ClassInfo c in used)
                            if (!existing.Used.Contains(c))
                                existing.Used.Add(c);
                    }
                    else
                    {
                        hosts[usb] = (decl, used);
                    }
                }
            }

            foreach (ClassInfo c in hosts.Values.SelectMany(h => h.Used))
                usedClasses?.Add(c.Tag);

            // ユーザー定義の構造体は未対応（U# も対応しておらず、使うと内部の例外になる）。使われるものだけエラーにする（#54）
            foreach (INamedTypeSymbol st in context.Structs.OrderBy(t => TagOf(t), StringComparer.Ordinal))
                diagnostics.Error(st.Locations.FirstOrDefault(l => l.IsInSource), $"'{st.Name}' : ユーザー定義の構造体は未対応です");

            // 外部のクラス（ScriptableObject・MonoBehaviour・Exception など）を継承したクラスも、U# では内部の例外になるのでエラーにする（#55）。
            // ソース上の普通のクラスを継承したものは、汎用クラスの検査（Validate）でエラーにしている
            foreach (INamedTypeSymbol cls in context.ExternalDerived.OrderBy(t => TagOf(t), StringComparer.Ordinal))
                diagnostics.Error(cls.Locations.FirstOrDefault(l => l.IsInSource),
                    $"'{cls.Name}' : {cls.BaseType?.Name} を継承したクラスは、UdonSharpBehaviour の中で使えません");

            if (hosts.Values.All(h => h.Used.Count == 0))
                return trees;

            foreach (ClassInfo c in hosts.Values.SelectMany(h => h.Used).Distinct())
                context.Validate(c);

            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                var rewriter = new Rewriter(context, model, null, null, hosts);
                SyntaxNode newRoot = rewriter.Visit(tree.GetRoot());
                result.Add(rewriter.Changed
                    ? CSharpSyntaxTree.ParseText(newRoot.ToFullString(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }

            return result;
        }

        // ------------------------------------------------------------------

        private static Dictionary<INamedTypeSymbol, ClassInfo> CollectClasses(CSharpCompilation compilation, List<SyntaxTree> trees)
        {
            var result = new Dictionary<INamedTypeSymbol, ClassInfo>(SymbolEqualityComparer.Default);

            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol) || result.ContainsKey(symbol))
                        continue;
                    if (!IsPlainClass(symbol))
                        continue;

                    string fullName = TagOf(symbol);
                    result.Add(symbol, new ClassInfo
                    {
                        Symbol = symbol,
                        Key = InterfaceNaming.Sanitize(fullName),
                        Tag = fullName,
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 汎用クラスの候補か：基底をたどって、object までソース上の普通のクラスだけが続くもの。
        /// （UdonSharpBehaviour・MonoBehaviour・Attribute などの派生は対象外）
        /// </summary>
        internal static bool IsPlainClass(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind != TypeKind.Class || InterfaceLowering.IsUdonSharpBehaviour(symbol))
                return false;

            for (INamedTypeSymbol t = symbol.BaseType; t != null; t = t.BaseType)
            {
                if (t.SpecialType == SpecialType.System_Object)
                    return true;
                if (!t.Locations.Any(l => l.IsInSource))
                    return false;
            }
            return true;
        }

        /// <summary>自動実装プロパティか（{ get; set; } など本体のないアクセサーだけのもの）</summary>
        private static bool IsAutoProperty(IPropertySymbol p) =>
            p.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().Any(d =>
                d.ExpressionBody == null && d.AccessorList != null
                && d.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));

        // ------------------------------------------------------------------

        /// <summary>書き換え全体で共有する情報</summary>
        private sealed class Context
        {
            public readonly CSharpCompilation Compilation;
            public readonly Dictionary<INamedTypeSymbol, ClassInfo> Classes;
            public readonly InterfaceLowering.LoweringDiagnostics Diagnostics;

            /// <summary>UdonSharpBehaviour（と、そこから使われる汎用クラス）が使うユーザー定義の構造体（#54）</summary>
            public readonly HashSet<INamedTypeSymbol> Structs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            /// <summary>同じく、外部のクラスを継承したユーザー定義クラス（#55）</summary>
            public readonly HashSet<INamedTypeSymbol> ExternalDerived = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            /// <summary>型タグ → 型番号（インタフェースの段が振る。#65）</summary>
            private readonly IReadOnlyDictionary<string, int> _numbers;

            public Context(CSharpCompilation compilation, Dictionary<INamedTypeSymbol, ClassInfo> classes, InterfaceLowering.LoweringDiagnostics diagnostics,
                IReadOnlyDictionary<string, int> numbers)
            {
                Compilation = compilation;
                Classes = classes;
                Diagnostics = diagnostics;
                _numbers = numbers;
            }

            /// <summary>型番号。インタフェースの呼び出しで振り分けないクラスは 0</summary>
            public int NumberOf(ClassInfo c) => _numbers != null && _numbers.TryGetValue(c.Tag, out int n) ? n : 0;

            public ClassInfo Target(ITypeSymbol type) =>
                type is INamedTypeSymbol named && Classes.TryGetValue(named.OriginalDefinition, out var info) ? info : null;

            /// <summary>型が汎用クラス（の配列）を含むか</summary>
            public bool InvolvesTarget(ITypeSymbol type) =>
                type is IArrayTypeSymbol array ? InvolvesTarget(array.ElementType) : Target(type) != null;

            /// <summary>scope の中で参照している汎用クラス（入れ子の型宣言の中は見ない）</summary>
            public HashSet<ClassInfo> Referenced(SyntaxNode scope, SemanticModel model)
            {
                var found = new HashSet<ClassInfo>();
                foreach (SyntaxNode node in scope.DescendantNodes(n => n == scope || !(n is BaseTypeDeclarationSyntax)))
                {
                    if (!(node is ExpressionSyntax || node is TypeSyntax))
                        continue;

                    SymbolInfo info = model.GetSymbolInfo(node);
                    foreach (ISymbol symbol in info.Symbol != null ? new[] { info.Symbol } : info.CandidateSymbols.ToArray())
                        AddFromSymbol(symbol, found);

                    if (node is ExpressionSyntax)
                        AddFromType(model.GetTypeInfo(node).Type, found);
                }
                return found;
            }

            private void AddFromSymbol(ISymbol symbol, HashSet<ClassInfo> found)
            {
                switch (symbol)
                {
                    case ITypeSymbol type:
                        AddFromType(type, found);
                        break;
                    case IMethodSymbol method:
                        AddFromType(method.ContainingType, found);
                        if (method.ReducedFrom != null)
                            AddFromType(method.ReducedFrom.ContainingType, found);
                        break;
                    case IFieldSymbol _:
                    case IPropertySymbol _:
                        AddFromType(symbol.ContainingType, found);
                        break;
                }
            }

            private void AddFromType(ITypeSymbol type, HashSet<ClassInfo> found)
            {
                while (type is IArrayTypeSymbol array)
                    type = array.ElementType;
                AddUnsupported(type);
                ClassInfo c = Target(type);
                if (c != null)
                    found.Add(c);
            }

            /// <summary>
            /// 未対応のユーザー定義の型（構造体、外部のクラスを継承したクラス）を記録する（Point? や配列、型引数の中も見る）
            /// </summary>
            private void AddUnsupported(ITypeSymbol type)
            {
                while (type is IArrayTypeSymbol array)
                    type = array.ElementType;
                if (!(type is INamedTypeSymbol named))
                    return;
                if (named.Locations.Any(l => l.IsInSource))
                {
                    if (named.TypeKind == TypeKind.Struct)
                        Structs.Add(named.OriginalDefinition);
                    else if (named.TypeKind == TypeKind.Class && !named.IsStatic && !IsPlainClass(named)
                             && !InterfaceLowering.IsUdonSharpBehaviour(named) && !IsAttribute(named))
                        ExternalDerived.Add(named.OriginalDefinition);
                }
                foreach (ITypeSymbol argument in named.TypeArguments)
                    AddUnsupported(argument);
            }

            /// <summary>属性（System.Attribute の派生）。属性として付けるだけなので対象外</summary>
            private static bool IsAttribute(INamedTypeSymbol type)
            {
                for (INamedTypeSymbol t = type.BaseType; t != null; t = t.BaseType)
                    if (t.ToDisplayString() == "System.Attribute")
                        return true;
                return false;
            }

            /// <summary>使うクラスから推移的にたどった一覧（決まった順）</summary>
            public List<ClassInfo> Closure(IEnumerable<ClassInfo> roots)
            {
                var seen = new HashSet<ClassInfo>();
                var queue = new Queue<ClassInfo>(roots);
                while (queue.Count > 0)
                {
                    ClassInfo c = queue.Dequeue();
                    if (!seen.Add(c))
                        continue;
                    foreach (SyntaxReference reference in c.Symbol.DeclaringSyntaxReferences)
                    {
                        SyntaxNode decl = reference.GetSyntax();
                        foreach (ClassInfo next in Referenced(decl, Compilation.GetSemanticModel(decl.SyntaxTree)))
                            if (!seen.Contains(next))
                                queue.Enqueue(next);
                    }
                }
                return seen.OrderBy(c => c.Tag, StringComparer.Ordinal).ToList();
            }

            /// <summary>未対応の書き方を調べ、フィールドの並びを決める（使われるクラスだけ、1 回だけ）</summary>
            public void Validate(ClassInfo c)
            {
                if (c.Validated)
                    return;
                c.Validated = true;

                INamedTypeSymbol s = c.Symbol;
                Location at = s.Locations.FirstOrDefault();

                void Fail(Location location, string message)
                {
                    Diagnostics.Error(location ?? at, message);
                    c.Valid = false;
                }

                if (s.BaseType != null && s.BaseType.SpecialType != SpecialType.System_Object)
                    Fail(at, $"'{s.Name}' : 普通のクラスの継承は未対応です");
                if (s.IsGenericType)
                    Fail(at, $"'{s.Name}' : ジェネリッククラスは未対応です");
                if (s.ContainingType != null)
                    Fail(at, $"'{s.Name}' : 入れ子のクラスは未対応です。名前空間の直下に宣言してください");
                if (s.IsAbstract && !s.IsStatic)
                    Fail(at, $"'{s.Name}' : abstract クラスは未対応です");

                foreach (ISymbol member in s.GetMembers())
                {
                    Location loc = member.Locations.FirstOrDefault();
                    switch (member)
                    {
                        case IFieldSymbol f when f.IsImplicitlyDeclared:
                            break;   // 自動実装プロパティのバッキングフィールド（プロパティ側で扱う）
                        case IFieldSymbol f when f.IsConst:
                            break;
                        case IFieldSymbol f when f.IsStatic:
                            Fail(loc, $"'{s.Name}.{f.Name}' : static フィールドは非対応です（UdonSharp と同じ）");
                            break;
                        case IFieldSymbol f:
                            AddSlot(c, f);
                            break;

                        case IPropertySymbol p when p.IsIndexer:
                            Fail(loc, $"'{s.Name}' : インデクサーは未対応です");
                            break;
                        case IPropertySymbol p when p.ExplicitInterfaceImplementations.Length > 0:
                            Fail(loc, $"'{s.Name}.{p.Name}' : 明示的インタフェース実装は未対応です");
                            break;
                        case IPropertySymbol p when IsAutoProperty(p) && p.IsStatic:
                            Fail(loc, $"'{s.Name}.{p.Name}' : static の自動実装プロパティは非対応です（static フィールドと同じ）");
                            break;
                        case IPropertySymbol p when IsAutoProperty(p):
                            AddSlot(c, p);
                            break;
                        case IPropertySymbol _:
                            break;

                        case IMethodSymbol m when m.IsImplicitlyDeclared:
                            break;
                        case IMethodSymbol m when m.MethodKind == MethodKind.StaticConstructor:
                            Fail(loc, $"'{s.Name}' : static コンストラクタは未対応です");
                            break;
                        case IMethodSymbol m when m.MethodKind == MethodKind.UserDefinedOperator || m.MethodKind == MethodKind.Conversion:
                            Fail(loc, $"'{s.Name}' : 演算子のオーバーロードは未対応です");
                            break;
                        case IMethodSymbol m when m.MethodKind == MethodKind.Destructor:
                            Fail(loc, $"'{s.Name}' : ファイナライザーは未対応です");
                            break;
                        case IMethodSymbol m when m.MethodKind == MethodKind.ExplicitInterfaceImplementation:
                            Fail(loc, $"'{s.Name}.{m.Name}' : 明示的インタフェース実装は未対応です");
                            break;
                        case IMethodSymbol m when m.IsGenericMethod:
                            Fail(loc, $"'{s.Name}.{m.Name}' : ジェネリックメソッドは未対応です");
                            break;
                        case IMethodSymbol m when m.IsOverride && m.Name != "ToString" && m.Name != "Equals" && m.Name != "GetHashCode":
                            Fail(loc, $"'{s.Name}.{m.Name}' : override は未対応です");
                            break;
                        case IMethodSymbol m when m.IsOverride:
                            Fail(loc, $"'{s.Name}.{m.Name}' : {m.Name} の override は未対応です（インスタンスは object[] なので呼ばれません）");
                            break;
                        case IMethodSymbol _:
                            break;

                        case IEventSymbol e:
                            Fail(loc, $"'{s.Name}.{e.Name}' : イベントは未対応です");
                            break;
                        case INamedTypeSymbol t:
                            Fail(t.Locations.FirstOrDefault(), $"'{s.Name}.{t.Name}' : クラスの中の型宣言は未対応です");
                            break;
                    }
                }
            }

            private static void AddSlot(ClassInfo c, ISymbol symbol)
            {
                c.Slots[symbol] = c.SlotOrder.Count + FirstSlot;
                c.SlotOrder.Add(symbol);
            }
        }

        // ------------------------------------------------------------------

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly Context _context;
            private readonly SemanticModel _model;
            private readonly ClassInfo _self;   // コピーを作っている汎用クラス（UdonSharpBehaviour の中では null）
            private readonly Sink _sink;
            private readonly Dictionary<INamedTypeSymbol, (ClassDeclarationSyntax Host, List<ClassInfo> Used)> _hosts;
            private int _temp;
            private string _member;   // コピー中のメンバー（「メソッド Exists」など。SourceMap に記録し、エラーの補足に使う）

            public bool Changed { get; private set; }

            public Rewriter(Context context, SemanticModel model, ClassInfo self, Sink sink,
                Dictionary<INamedTypeSymbol, (ClassDeclarationSyntax Host, List<ClassInfo> Used)> hosts)
            {
                _context = context;
                _model = model;
                _self = self;
                _sink = sink;
                _hosts = hosts;
            }

            private void Error(SyntaxNode node, string message) => _context.Diagnostics.Error(node.GetLocation(), message);

            private ClassInfo Target(ITypeSymbol type) => _context.Target(type);

            /// <summary>元の位置の印（#52）</summary>
            private string Marker(SyntaxNode original, string member = null) => SourceMap.Marker(original, _self?.Symbol.Name, member ?? _member);

            /// <summary>コピー中の文には、先頭に元の位置の印のコメントを付ける（#52）</summary>
            public override SyntaxNode Visit(SyntaxNode node)
            {
                SyntaxNode result = base.Visit(node);
                if (_self != null && node is StatementSyntax && !(node is BlockSyntax) && result is StatementSyntax statement)
                    result = statement.WithLeadingTrivia(statement.GetLeadingTrivia().Insert(0, SyntaxFactory.Comment(Marker(node))));
                return result;
            }

            // ---- 型の表記 --------------------------------------------------------

            /// <summary>汎用クラスを object[] に置き換えた型の表記</summary>
            private string TypeText(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeText(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
                if (Target(type) != null)
                    return InstanceType;
                // List<Bullet> → List<object[]>（型引数の中も置き換える。#56）
                if (type is INamedTypeSymbol named && CollectionLowering.GenericText(named, TypeText) is string generic)
                    return generic;
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            private static string Text(SyntaxNode node) => node.WithoutTrivia().ToFullString();

            private ExpressionSyntax Parse(string code, SyntaxNode original)
            {
                Changed = true;
                return SyntaxFactory.ParseExpression(code).WithTriviaFrom(original);
            }

            /// <summary>型の位置に置く object[]（TypeSyntax でないと親ノードに入らない）</summary>
            private TypeSyntax ParseInstanceType(SyntaxNode original)
            {
                Changed = true;
                return SyntaxFactory.ParseTypeName(InstanceType).WithTriviaFrom(original);
            }

            // ---- クラス宣言 ------------------------------------------------------

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                if (_self != null)
                    return node;

                if (!(_model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol) || !InterfaceLowering.IsUdonSharpBehaviour(symbol))
                    return node;   // 汎用クラスの宣言や、UdonSharp と関係ないクラスはそのまま

                var sink = new Sink();
                var inner = new Rewriter(_context, _model, null, sink, null);
                var visited = (ClassDeclarationSyntax)inner.VisitUdonSharpBehaviour(node);
                if (inner.Changed)
                    Changed = true;

                if (_hosts == null || !_hosts.TryGetValue(symbol, out var host) || host.Host != node || host.Used.Count == 0)
                    return visited;

                var members = new List<string>();
                foreach (ClassInfo c in host.Used.Where(c => c.Valid))
                    members.AddRange(GenerateMembers(c, sink));
                foreach (ClassInfo c in host.Used.Where(c => c.Valid))
                {
                    if (sink.IsHelpers.Contains(c) || sink.AsHelpers.Contains(c))
                        members.Add(BuildIsHelper(c));
                    if (sink.AsHelpers.Contains(c))
                        members.Add($"private static {InstanceType} {c.AsHelper}(object __o) {{ return {c.IsHelper}(__o) ? ({InstanceType})__o : null; }}");
                }

                if (members.Count == 0)
                    return visited;

                Changed = true;
                // 生成メンバーは閉じ括弧 '}' と同じ行に置き、行番号をずらさない
                var parsed = members.Select(code => SyntaxFactory.ParseMemberDeclaration(code + " ")).Where(d => d != null);
                return visited.WithMembers(visited.Members.AddRange(parsed));
            }

            private SyntaxNode VisitUdonSharpBehaviour(ClassDeclarationSyntax node) => base.VisitClassDeclaration(node);

            // UdonSharpBehaviour のフィールドに汎用クラス型があれば、シリアライズ・同期の対象から外す
            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                ITypeSymbol type = _model.GetTypeInfo(node.Declaration.Type).Type;
                if (_self != null || type == null || !_context.InvolvesTarget(type))
                    return base.VisitFieldDeclaration(node);

                var attributes = node.AttributeLists.SelectMany(l => l.Attributes)
                    .Select(a => _model.GetSymbolInfo(a).Symbol?.ContainingType?.Name).ToList();
                if (attributes.Contains("SerializeField") || attributes.Contains("UdonSyncedAttribute") || attributes.Contains("SerializeReference"))
                {
                    Error(node, "普通のクラスの型のフィールドは、シリアライズ・同期できません（[SerializeField] / [UdonSynced] は付けられません）");
                    return base.VisitFieldDeclaration(node);
                }

                var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
                bool isPublic = node.Modifiers.Any(SyntaxKind.PublicKeyword);
                if (!isPublic || attributes.Contains("NonSerializedAttribute"))
                    return visited;

                // public でも Unity にシリアライズさせない（同じ行に属性を足す）
                Changed = true;
                var attribute = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.NonSerialized"))))
                    .WithTrailingTrivia(SyntaxFactory.Space);
                SyntaxTriviaList leading = visited.GetLeadingTrivia();
                return visited.WithoutLeadingTrivia().WithAttributeLists(visited.AttributeLists.Insert(0, attribute)).WithLeadingTrivia(leading);
            }

            // ---- 型参照の置換 ---------------------------------------------------

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == node)
                    return base.VisitIdentifierName(node);

                ISymbol symbol = _model.GetSymbolInfo(node).Symbol;

                if (TryConstant(node, symbol, out ExpressionSyntax constant))
                    return constant;

                if (symbol is INamedTypeSymbol type && Target(type) != null)
                    return ParseInstanceType(node);

                if (MethodGroup(node, symbol) is SyntaxNode group)
                    return group;

                if (_self != null)
                {
                    // コピー先のファイルには元のファイルの using がないので、型名は完全修飾にする
                    if (symbol is INamedTypeSymbol other && !node.IsVar)
                        return QualifiedType(other, node);
                    if (TrySlot(node, out ClassInfo c, out int index, out ITypeSymbol slotType, out _))
                        return Parse(SlotRead(ThisName, index, slotType), node);
                    if (TryBodiedProperty(node, out ClassInfo pc, out IPropertySymbol p, out _))
                        return Parse($"{GetterName(pc, p)}({(p.IsStatic ? "" : ThisName)})", node);
                }

                return base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type)
                {
                    if (Target(type) != null)
                        return ParseInstanceType(node);
                    if (_self != null)
                        return QualifiedType(type, node);
                }
                return base.VisitQualifiedName(node);
            }

            public override SyntaxNode VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
            {
                if (_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type && Target(type) != null)
                    return ParseInstanceType(node);
                return base.VisitAliasQualifiedName(node);
            }

            // List<int> など：コピーの中では完全修飾にする（型引数の汎用クラスは object[] にする）
            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                if (_self != null && _model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type)
                    return QualifiedType(type, node);
                return base.VisitGenericName(node);
            }

            private SyntaxNode QualifiedType(INamedTypeSymbol type, SyntaxNode original)
            {
                if (type.IsTupleType || type.IsAnonymousType)
                    return original;
                Changed = true;
                return SyntaxFactory.ParseTypeName(TypeText(type)).WithTriviaFrom(original);
            }

            /// <summary>
            /// 汎用クラスのメソッドを、呼び出さずにメソッド名だけで使う（list.Find(IsOdd) など。#57）。
            /// static ならコピーのメソッド名にする。インスタンスのメソッドは受け側を渡せないのでエラー。対象でなければ null
            /// </summary>
            private SyntaxNode MethodGroup(ExpressionSyntax node, ISymbol symbol)
            {
                if (!(symbol is IMethodSymbol method) || method.MethodKind != MethodKind.Ordinary)
                    return null;
                IMethodSymbol definition = method.OriginalDefinition;
                ClassInfo c = Target(definition.ContainingType);
                if (c == null)
                    return null;
                if (!definition.IsStatic)
                {
                    Error(node, $"'{method.Name}' : 普通のクラスのインスタンスのメソッドを、メソッド名だけで渡すのは未対応です（ラムダで書いてください）");
                    return node;
                }
                return Parse(MethodName(c, definition), node);
            }

            public override SyntaxNode VisitThisExpression(ThisExpressionSyntax node)
            {
                if (_self != null)
                    return Parse(ThisName, node);
                return base.VisitThisExpression(node);
            }

            public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
            {
                if (_context.InvolvesTarget(_model.GetTypeInfo(node.Type).Type))
                {
                    Error(node, "普通のクラスの typeof は未対応です");
                    return node;
                }
                return base.VisitTypeOfExpression(node);
            }

            // new Bullet[n] → new object[n][]
            public override SyntaxNode VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
            {
                ITypeSymbol element = node.Type.ElementType is TypeSyntax t ? _model.GetTypeInfo(t).Type : null;
                if (element == null || !_context.InvolvesTarget(element))
                    return base.VisitArrayCreationExpression(node);

                var ranks = string.Concat(node.Type.RankSpecifiers.Select(r => Text(Visit(r))));
                string initializer = node.Initializer != null ? " " + Text(Visit(node.Initializer)) : "";
                // 要素型の表記（object[] や object[][]）の [] を、作る配列の次元の後ろに付ける
                string elementText = TypeText(element);
                int bracket = elementText.IndexOf('[');
                string head = elementText.Substring(0, bracket);
                string tail = elementText.Substring(bracket);
                return Parse($"new {head}{ranks}{tail}{initializer}", node);
            }

            // ---- 定数（const・nameof）--------------------------------------------

            private bool TryConstant(ExpressionSyntax node, ISymbol symbol, out ExpressionSyntax literal)
            {
                literal = null;
                bool targetConst = symbol is IFieldSymbol f && f.IsConst && Target(f.ContainingType) != null;
                if (!targetConst)
                    return false;

                Optional<object> value = _model.GetConstantValue(node);
                if (!value.HasValue)
                    return false;

                literal = Parse(ConstantText(value.Value, ((IFieldSymbol)symbol).Type), node);
                return true;
            }

            private string ConstantText(object value, ITypeSymbol type)
            {
                switch (value)
                {
                    case null: return "null";
                    case string s: return SyntaxFactory.Literal(s).ToString();
                    case bool b: return b ? "true" : "false";
                    case char ch: return SyntaxFactory.Literal(ch).ToString();
                    case float fl: return $"(({TypeText(type)}){SyntaxFactory.Literal(fl)})";
                    case double d: return $"(({TypeText(type)}){SyntaxFactory.Literal(d)})";
                    case decimal m: return $"(({TypeText(type)}){SyntaxFactory.Literal(m)})";
                    case ulong ul: return $"(({TypeText(type)}){SyntaxFactory.Literal(ul)})";
                    default: return $"(({TypeText(type)})({Convert.ToInt64(value)}L))";
                }
            }

            private bool ReferencesTarget(SyntaxNode node) =>
                node.DescendantNodesAndSelf().Any(n =>
                {
                    ISymbol s = _model.GetSymbolInfo(n).Symbol;
                    return (s is ITypeSymbol t && _context.InvolvesTarget(t)) || (s != null && !(s is ITypeSymbol) && Target(s.ContainingType) != null);
                });

            // ---- フィールド・プロパティ -----------------------------------------

            /// <summary>汎用クラスのインスタンスのフィールド（自動実装プロパティを含む）を指す式か</summary>
            private bool TrySlot(ExpressionSyntax expr, out ClassInfo c, out int index, out ITypeSymbol type, out ExpressionSyntax receiver)
            {
                c = null;
                index = 0;
                type = null;
                receiver = null;

                if (expr is MemberAccessExpressionSyntax access && access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    receiver = access.Expression;
                else if (!(expr is IdentifierNameSyntax) || _self == null)
                    return false;

                ISymbol symbol = _model.GetSymbolInfo(expr).Symbol;
                if (symbol == null || symbol.IsStatic || !(symbol is IFieldSymbol || symbol is IPropertySymbol))
                    return false;

                c = Target(symbol.ContainingType);
                if (c == null || !c.Slots.TryGetValue(symbol.OriginalDefinition, out index))
                    return false;

                // 暗黙の this は、コピー中のクラスのメンバーのときだけ
                if (receiver == null && !SymbolEqualityComparer.Default.Equals(c.Symbol, _self.Symbol))
                    return false;

                type = symbol is IFieldSymbol f ? f.Type : ((IPropertySymbol)symbol).Type;
                return true;
            }

            /// <summary>汎用クラスの本体付きプロパティを指す式か</summary>
            private bool TryBodiedProperty(ExpressionSyntax expr, out ClassInfo c, out IPropertySymbol property, out ExpressionSyntax receiver)
            {
                c = null;
                property = null;
                receiver = null;

                if (expr is MemberAccessExpressionSyntax access && access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    receiver = access.Expression;
                else if (!(expr is IdentifierNameSyntax) || _self == null)
                    return false;

                if (!(_model.GetSymbolInfo(expr).Symbol is IPropertySymbol p) || p.IsIndexer)
                    return false;

                c = Target(p.ContainingType);
                if (c == null || c.Slots.ContainsKey(p.OriginalDefinition) || IsAutoProperty(p.OriginalDefinition))
                    return false;

                property = p.OriginalDefinition;
                return true;
            }

            private string ReceiverText(ExpressionSyntax receiver)
            {
                if (receiver == null)
                    return ThisName;
                return Text(Visit(receiver));
            }

            private string SlotRead(string receiver, int index, ITypeSymbol type) =>
                $"(({TypeText(type)})({receiver})[{index}])";

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                ISymbol symbol = _model.GetSymbolInfo(node).Symbol;

                if (TryConstant(node, symbol, out ExpressionSyntax constant))
                    return constant;

                if (TrySlot(node, out _, out int index, out ITypeSymbol type, out ExpressionSyntax receiver))
                    return Parse(SlotRead(ReceiverText(receiver), index, type), node);

                if (TryBodiedProperty(node, out ClassInfo c, out IPropertySymbol p, out ExpressionSyntax propReceiver))
                    return Parse(p.IsStatic ? $"{GetterName(c, p)}()" : $"{GetterName(c, p)}({ReceiverText(propReceiver)})", node);

                if (symbol is INamedTypeSymbol t)
                {
                    if (Target(t) != null)
                        return ParseInstanceType(node);
                    if (_self != null)
                        return QualifiedType(t, node);   // System.Collections.Generic.List など（元のファイルの名前空間からの相対かもしれない）
                }

                if (MethodGroup(node, symbol) is SyntaxNode group)
                    return group;

                return base.VisitMemberAccessExpression(node);
            }

            public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
            {
                if (_context.InvolvesTarget(_model.GetTypeInfo(node.Expression).Type))
                {
                    Error(node, "普通のクラスへの ?. は未対応です");
                    return node;
                }
                return base.VisitConditionalAccessExpression(node);
            }

            // ---- 代入・インクリメント -------------------------------------------

            private bool IsStatementContext(ExpressionSyntax node) => InterfaceLowering.IsStatementExpression(node, _model);

            /// <summary>何度評価しても結果と副作用が変わらない式か（複合代入で受け側を 2 回書くため）</summary>
            private static bool IsSimple(ExpressionSyntax e)
            {
                switch (e)
                {
                    case null: return true;
                    case IdentifierNameSyntax _:
                    case ThisExpressionSyntax _:
                    case LiteralExpressionSyntax _:
                        return true;
                    case MemberAccessExpressionSyntax m:
                        return m.IsKind(SyntaxKind.SimpleMemberAccessExpression) && IsSimple(m.Expression);
                    case ParenthesizedExpressionSyntax p:
                        return IsSimple(p.Expression);
                    case CastExpressionSyntax c:
                        return IsSimple(c.Expression);
                    case ElementAccessExpressionSyntax el:
                        return IsSimple(el.Expression) && el.ArgumentList.Arguments.All(a => IsSimple(a.Expression));
                    default:
                        return false;
                }
            }

            private static string BinaryOperator(SyntaxKind kind)
            {
                switch (kind)
                {
                    case SyntaxKind.AddAssignmentExpression: return "+";
                    case SyntaxKind.SubtractAssignmentExpression: return "-";
                    case SyntaxKind.MultiplyAssignmentExpression: return "*";
                    case SyntaxKind.DivideAssignmentExpression: return "/";
                    case SyntaxKind.ModuloAssignmentExpression: return "%";
                    case SyntaxKind.AndAssignmentExpression: return "&";
                    case SyntaxKind.OrAssignmentExpression: return "|";
                    case SyntaxKind.ExclusiveOrAssignmentExpression: return "^";
                    case SyntaxKind.LeftShiftAssignmentExpression: return "<<";
                    case SyntaxKind.RightShiftAssignmentExpression: return ">>";
                    case SyntaxKind.CoalesceAssignmentExpression: return "??";
                    case SyntaxKind.PostIncrementExpression:
                    case SyntaxKind.PreIncrementExpression: return "+";
                    case SyntaxKind.PostDecrementExpression:
                    case SyntaxKind.PreDecrementExpression: return "-";
                    default: return null;
                }
            }

            public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                bool compound = !node.IsKind(SyntaxKind.SimpleAssignmentExpression);

                if (TrySlot(node.Left, out _, out int index, out ITypeSymbol type, out ExpressionSyntax receiver))
                {
                    if (!CheckWrite(node, receiver, compound))
                        return node;
                    string recv = ReceiverText(receiver);
                    string right = Text(Visit(node.Right));
                    string value = compound
                        ? $"({SlotRead(recv, index, type)} {BinaryOperator(node.Kind())} ({right}))"
                        : $"({right})";
                    return Parse($"({recv})[{index}] = ({TypeText(type)}){value}", node);
                }

                if (TryBodiedProperty(node.Left, out ClassInfo c, out IPropertySymbol p, out ExpressionSyntax propReceiver))
                {
                    if (!CheckWrite(node, propReceiver, compound))
                        return node;
                    if (p.SetMethod == null)
                        return base.VisitAssignmentExpression(node);
                    string recv = p.IsStatic ? null : ReceiverText(propReceiver);
                    string right = Text(Visit(node.Right));
                    string current = recv == null ? $"{GetterName(c, p)}()" : $"{GetterName(c, p)}({recv})";
                    string value = compound ? $"({current} {BinaryOperator(node.Kind())} ({right}))" : $"({right})";
                    string args = recv == null ? $"({TypeText(p.Type)}){value}" : $"{recv}, ({TypeText(p.Type)}){value}";
                    return Parse($"{SetterName(c, p)}({args})", node);
                }

                return base.VisitAssignmentExpression(node);
            }

            public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node) =>
                RewriteIncrement(node, node.Operand) ?? base.VisitPostfixUnaryExpression(node);

            public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node) =>
                RewriteIncrement(node, node.Operand) ?? base.VisitPrefixUnaryExpression(node);

            private SyntaxNode RewriteIncrement(ExpressionSyntax node, ExpressionSyntax operand)
            {
                string op = BinaryOperator(node.Kind());
                if (op == null)
                    return null;

                if (TrySlot(operand, out _, out int index, out ITypeSymbol type, out ExpressionSyntax receiver))
                {
                    if (!CheckWrite(node, receiver, true))
                        return node;
                    string recv = ReceiverText(receiver);
                    return Parse($"({recv})[{index}] = ({TypeText(type)})({SlotRead(recv, index, type)} {op} 1)", node);
                }

                if (TryBodiedProperty(operand, out ClassInfo c, out IPropertySymbol p, out ExpressionSyntax propReceiver))
                {
                    if (!CheckWrite(node, propReceiver, true))
                        return node;
                    string recv = p.IsStatic ? null : ReceiverText(propReceiver);
                    string current = recv == null ? $"{GetterName(c, p)}()" : $"{GetterName(c, p)}({recv})";
                    string value = $"({TypeText(p.Type)})({current} {op} 1)";
                    return Parse(recv == null ? $"{SetterName(c, p)}({value})" : $"{SetterName(c, p)}({recv}, {value})", node);
                }

                return null;
            }

            private bool CheckWrite(ExpressionSyntax node, ExpressionSyntax receiver, bool readsReceiverTwice)
            {
                if (!IsStatementContext(node))
                {
                    Error(node, "普通のクラスのフィールド・プロパティへの代入やインクリメントは、文としてだけ使えます（式の値としては使えません）");
                    return false;
                }
                if (readsReceiverTwice && !IsSimple(receiver))
                {
                    Error(node, "普通のクラスのフィールド・プロパティへの複合代入・インクリメントは、受け側が変数やフィールドのときだけ使えます（いったん変数に入れてください）");
                    return false;
                }
                return true;
            }

            /// <summary>
            /// 値型のフィールドの一部を書き換える文（b.pos.x = 1; など）は、取り出して書き換えて戻す。
            ///   { T __cls_tmp0 = ((T)(b)[2]); __cls_tmp0.x = 1; (b)[2] = __cls_tmp0; }
            /// </summary>
            public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
            {
                ExpressionSyntax target = null;
                switch (node.Expression)
                {
                    case AssignmentExpressionSyntax a: target = a.Left; break;
                    case PostfixUnaryExpressionSyntax post: target = post.Operand; break;
                    case PrefixUnaryExpressionSyntax pre when BinaryOperator(pre.Kind()) != null: target = pre.Operand; break;
                }

                ExpressionSyntax slotExpr = null;
                for (ExpressionSyntax e = target; e is MemberAccessExpressionSyntax m; e = m.Expression)
                {
                    if (TrySlot(m.Expression, out _, out _, out ITypeSymbol t, out _) && t.IsValueType)
                    {
                        slotExpr = m.Expression;
                        break;
                    }
                }
                if (slotExpr == null)
                    return base.VisitExpressionStatement(node);

                TrySlot(slotExpr, out _, out int index, out ITypeSymbol type, out ExpressionSyntax receiver);
                if (!IsSimple(receiver))
                {
                    Error(node, "値型のフィールドの一部の書き換えは、受け側が変数やフィールドのときだけ使えます（いったん変数に入れてください）");
                    return node;
                }

                string recv = ReceiverText(receiver);
                string temp = $"__cls_tmp{_temp++}";
                // slotExpr より後ろ（.x など）の部分。代入の右辺は書き換えてから付ける
                string suffix = target.ToString().Substring(slotExpr.Span.End - target.SpanStart);
                string statement;
                switch (node.Expression)
                {
                    case AssignmentExpressionSyntax a:
                        statement = $"{temp}{suffix} {a.OperatorToken.Text} {Text(Visit(a.Right))};";
                        break;
                    case PostfixUnaryExpressionSyntax post:
                        statement = $"{temp}{suffix}{post.OperatorToken.Text};";
                        break;
                    default:
                        statement = $"{((PrefixUnaryExpressionSyntax)node.Expression).OperatorToken.Text}{temp}{suffix};";
                        break;
                }

                Changed = true;
                string block = $"{{ {TypeText(type)} {temp} = {SlotRead(recv, index, type)}; {statement} ({recv})[{index}] = {temp}; }}";
                return SyntaxFactory.ParseStatement(block).WithTriviaFrom(node);
            }

            // ---- 生成・呼び出し --------------------------------------------------

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
                RewriteCreation(node, node.ArgumentList, node.Initializer) ?? base.VisitObjectCreationExpression(node);

            public override SyntaxNode VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
                RewriteCreation(node, node.ArgumentList, node.Initializer) ?? base.VisitImplicitObjectCreationExpression(node);

            private SyntaxNode RewriteCreation(ExpressionSyntax node, ArgumentListSyntax arguments, InitializerExpressionSyntax initializer)
            {
                if (!(_model.GetSymbolInfo(node).Symbol is IMethodSymbol ctor) || Target(ctor.ContainingType) == null)
                    return null;

                if (initializer != null)
                {
                    Error(node, "普通のクラスのオブジェクト初期化子（new X { a = 1 }）は未対応です");
                    return node;
                }

                ClassInfo c = Target(ctor.ContainingType);
                string args = arguments == null ? "" : string.Join(", ", arguments.Arguments.Select(a => Text(Visit(a))));
                return Parse($"{CtorName(c, ctor)}({args})", node);
            }

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (node.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof"
                    && _model.GetConstantValue(node).Value is string name && ReferencesTarget(node.ArgumentList))
                    return Parse(SyntaxFactory.Literal(name).ToString(), node);

                if (!(_model.GetSymbolInfo(node).Symbol is IMethodSymbol method))
                    return base.VisitInvocationExpression(node);

                IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
                ClassInfo c = Target(definition.ContainingType);
                if (c == null)
                {
                    // コピーの中の、外部の拡張メソッド（LINQ など）：コピー先のファイルには元の using がないので、static の形にする
                    // list.Where(f) → global::System.Linq.Enumerable.Where(list, f)（#58）
                    if (_self != null && method.ReducedFrom != null && node.Expression is MemberAccessExpressionSyntax ext
                        && ext.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        string receiver = Text(Visit(ext.Expression));
                        string methodName = Text(Visit(ext.Name));   // 型引数（Cast<int> など）も完全修飾にする
                        var rest = node.ArgumentList.Arguments.Select(a => Text(Visit(a)));
                        string container = definition.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        return Parse($"{container}.{methodName}({string.Join(", ", new[] { receiver }.Concat(rest))})", node);
                    }
                    return base.VisitInvocationExpression(node);
                }

                var args = new List<string>();
                if (method.ReducedFrom != null)
                {
                    // 拡張メソッド：受け側を最初の引数にする
                    if (node.Expression is MemberAccessExpressionSyntax extensionAccess)
                        args.Add(Text(Visit(extensionAccess.Expression)));
                }
                else if (!definition.IsStatic)
                {
                    switch (node.Expression)
                    {
                        case MemberAccessExpressionSyntax access:
                            args.Add(Text(Visit(access.Expression)));
                            break;
                        case IdentifierNameSyntax _ when _self != null:
                            args.Add(ThisName);
                            break;
                        default:
                            Error(node, $"'{method.Name}' : この形の普通のクラスのメソッド呼び出しは未対応です（?. など）");
                            return node;
                    }
                }

                args.AddRange(node.ArgumentList.Arguments.Select(a => Text(Visit(a))));
                return Parse($"{MethodName(c, definition)}({string.Join(", ", args)})", node);
            }

            // ---- is / as -------------------------------------------------------

            public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if ((node.IsKind(SyntaxKind.IsExpression) || node.IsKind(SyntaxKind.AsExpression))
                    && Target(_model.GetTypeInfo(node.Right).Type) is ClassInfo c)
                {
                    bool isIs = node.IsKind(SyntaxKind.IsExpression);
                    if (_sink != null)
                    {
                        _sink.IsHelpers.Add(c);
                        if (!isIs)
                            _sink.AsHelpers.Add(c);
                    }
                    return Parse($"{(isIs ? c.IsHelper : c.AsHelper)}((object)({Text(Visit(node.Left))}))", node);
                }
                return base.VisitBinaryExpression(node);
            }

            public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
            {
                if (ReferencesTarget(node.Pattern))
                {
                    Error(node, "普通のクラスの型パターン（x is Bullet b など）は未対応です。is と キャストに分けてください");
                    return node;
                }
                return base.VisitIsPatternExpression(node);
            }

            private static string BuildIsHelper(ClassInfo c) =>
                $"private static bool {c.IsHelper}(object __o) {{ if (__o == null || !__o.GetType().Equals(typeof(object[]))) return false; " +
                $"object[] __a = (object[])__o; if (__a.Length == 0 || __a[0] == null || !__a[0].GetType().Equals(typeof(string))) return false; " +
                $"return (string)__a[0] == {SyntaxFactory.Literal(c.Tag)}; }}";

            // ---- 名前 -----------------------------------------------------------

            private static string Signature(IMethodSymbol m)
            {
                if (m.Parameters.Length == 0)
                    return "";
                return "__" + string.Join("__", m.Parameters.Select(p =>
                    (p.RefKind == RefKind.None ? "" : p.RefKind.ToString().ToLowerInvariant() + "_") + TypeToken(p.Type)));
            }

            private static string TypeToken(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeToken(array.ElementType) + "Array" + (array.Rank > 1 ? array.Rank.ToString() : "");
                return InterfaceNaming.Sanitize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""));
            }

            private static string MethodName(ClassInfo c, IMethodSymbol m) => $"__cls_{c.Key}__{m.Name}{Signature(m)}";
            private static string CtorName(ClassInfo c, IMethodSymbol ctor) => $"__cls_{c.Key}__ctor{Signature(ctor)}";
            private static string InitName(ClassInfo c, IMethodSymbol ctor) => $"__cls_{c.Key}__init{Signature(ctor)}";
            private static string GetterName(ClassInfo c, IPropertySymbol p) => $"__cls_{c.Key}__get_{p.Name}";
            private static string SetterName(ClassInfo c, IPropertySymbol p) => $"__cls_{c.Key}__set_{p.Name}";

            // ---- コピーの生成 ---------------------------------------------------

            /// <summary>汎用クラス c のコンストラクタ・メソッド・アクセサーを、private static メソッドの 1 行テキストとして作る</summary>
            private List<string> GenerateMembers(ClassInfo c, Sink sink)
            {
                var result = new List<string>();
                INamedTypeSymbol s = c.Symbol;

                foreach (SyntaxReference reference in s.DeclaringSyntaxReferences)
                {
                    var decl = (ClassDeclarationSyntax)reference.GetSyntax();
                    SemanticModel model = _context.Compilation.GetSemanticModel(decl.SyntaxTree);
                    var body = new Rewriter(_context, model, c, sink, null);

                    foreach (MemberDeclarationSyntax member in decl.Members)
                    {
                        switch (member)
                        {
                            case MethodDeclarationSyntax method:
                                result.Add(body.CopyMethod(c, method));
                                break;
                            case ConstructorDeclarationSyntax ctor when !ctor.Modifiers.Any(SyntaxKind.StaticKeyword):
                                result.AddRange(body.CopyConstructor(c, ctor));
                                break;
                            case PropertyDeclarationSyntax property:
                                result.AddRange(body.CopyProperty(c, property));
                                break;
                        }
                    }

                    if (body.Changed)
                        Changed = true;
                }

                // コンストラクタの宣言がなければ既定のもの
                IMethodSymbol implicitCtor = s.InstanceConstructors.FirstOrDefault(m => m.IsImplicitlyDeclared);
                if (implicitCtor != null && !s.IsStatic)
                {
                    var decl = (ClassDeclarationSyntax)s.DeclaringSyntaxReferences.First().GetSyntax();
                    var body = new Rewriter(_context, _context.Compilation.GetSemanticModel(decl.SyntaxTree), c, sink, null) { _member = "コンストラクタ" };
                    result.Add(body.BuildInit(c, implicitCtor, "", null, null));
                    result.Add(body.BuildCtor(c, implicitCtor, ""));
                }

                return result;
            }

            private string CopyMethod(ClassInfo c, MethodDeclarationSyntax method)
            {
                IMethodSymbol symbol = _model.GetDeclaredSymbol(method);
                _member = $"メソッド {symbol.Name}";
                var sb = new StringBuilder();
                if (symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "RecursiveMethodAttribute"))
                    sb.Append("[global::UdonSharp.RecursiveMethod] ");

                sb.Append("private static ").Append(symbol.ReturnsVoid ? "void" : TypeText(symbol.ReturnType)).Append(' ')
                    .Append(MethodName(c, symbol)).Append('(');
                var parameters = new List<string>();
                if (!symbol.IsStatic)
                    parameters.Add($"{InstanceType} {ThisName}");
                parameters.Add(ParameterList(symbol, method.ParameterList, true));
                parameters.RemoveAll(x => x.Length == 0);
                sb.Append(string.Join(", ", parameters)).Append(") ");
                sb.Append(Body(method.Body, method.ExpressionBody, !symbol.ReturnsVoid));
                return sb.ToString();
            }

            /// <summary>引数の並び（型は object[] に置き換え、拡張メソッドの this は外す）</summary>
            private string ParameterList(IMethodSymbol method, ParameterListSyntax syntax, bool withDefaults)
            {
                var list = new List<string>();
                for (int i = 0; i < method.Parameters.Length; i++)
                {
                    IParameterSymbol p = method.Parameters[i];
                    ParameterSyntax ps = syntax != null && i < syntax.Parameters.Count ? syntax.Parameters[i] : null;
                    string text = (p.IsParams ? "params " : "") + RefPrefix(p) + TypeText(p.Type) + " " + p.Name;
                    if (withDefaults && ps?.Default != null)
                        text += " = " + Flatten(Visit(ps.Default.Value));
                    list.Add(text);
                }
                return string.Join(", ", list);
            }

            private IEnumerable<string> CopyConstructor(ClassInfo c, ConstructorDeclarationSyntax ctor)
            {
                IMethodSymbol symbol = _model.GetDeclaredSymbol(ctor);
                _member = "コンストラクタ";
                string parameters = ParameterList(symbol, ctor.ParameterList, true);
                yield return BuildInit(c, symbol, parameters, ctor.Initializer, ctor.Body != null ? (SyntaxNode)ctor.Body : ctor.ExpressionBody);
                yield return BuildCtor(c, symbol, parameters);
            }

            /// <summary>__init：(this(...) の呼び出し | フィールドの初期化子) + 本体</summary>
            private string BuildInit(ClassInfo c, IMethodSymbol ctor, string parameters, ConstructorInitializerSyntax initializer, SyntaxNode body)
            {
                var sb = new StringBuilder();
                string plain = ParameterList(ctor, null, false);
                sb.Append($"private static void {InitName(c, ctor)}({InstanceType} {ThisName}{(plain.Length > 0 ? ", " + plain : "")}) {{ ");

                if (initializer != null && initializer.IsKind(SyntaxKind.ThisConstructorInitializer)
                    && _model.GetSymbolInfo(initializer).Symbol is IMethodSymbol other)
                {
                    var args = new List<string> { ThisName };
                    args.AddRange(initializer.ArgumentList.Arguments.Select(a => Flatten(Visit(a))));
                    sb.Append($"{InitName(c, other)}({string.Join(", ", args)}); ");
                }
                else
                {
                    sb.Append(FieldInitializers(c));
                }

                switch (body)
                {
                    case BlockSyntax block:
                        sb.Append(string.Join(" ", block.Statements.Select(st => Flatten(Visit(st)))));
                        break;
                    case ArrowExpressionClauseSyntax arrow:
                        sb.Append(Marker(arrow.Expression)).Append(Flatten(Visit(arrow.Expression))).Append(';');
                        break;
                }

                sb.Append(" }");
                return sb.ToString();
            }

            /// <summary>
            /// __ctor：配列を作り、型タグ・型番号と既定値を入れて __init を呼ぶ。
            /// 型番号は new したクラスのもの（基底クラスの初期化は __init だけを呼ぶので触らない）
            /// </summary>
            private string BuildCtor(ClassInfo c, IMethodSymbol ctor, string parameters)
            {
                var sb = new StringBuilder();
                sb.Append($"private static {InstanceType} {CtorName(c, ctor)}({parameters}) {{ ");
                sb.Append($"{InstanceType} {ThisName} = new object[{c.Size}]; {ThisName}[0] = {SyntaxFactory.Literal(c.Tag)}; ");
                sb.Append($"{ThisName}[1] = {_context.NumberOf(c)}; ");
                foreach (ISymbol slot in c.SlotOrder)
                {
                    ITypeSymbol type = slot is IFieldSymbol f ? f.Type : ((IPropertySymbol)slot).Type;
                    if (type.IsValueType)
                        sb.Append($"{ThisName}[{c.Slots[slot]}] = default({TypeText(type)}); ");
                }
                var args = new List<string> { ThisName };
                args.AddRange(ctor.Parameters.Select(p => RefPrefix(p) + p.Name));
                sb.Append($"{InitName(c, ctor)}({string.Join(", ", args)}); return {ThisName}; }}");
                return sb.ToString();
            }

            private static string RefPrefix(IParameterSymbol p) =>
                p.RefKind == RefKind.Ref ? "ref " : p.RefKind == RefKind.Out ? "out " : p.RefKind == RefKind.In ? "in " : "";

            private string FieldInitializers(ClassInfo c)
            {
                var sb = new StringBuilder();
                foreach (ISymbol slot in c.SlotOrder)
                {
                    ExpressionSyntax value = null;
                    foreach (SyntaxReference r in slot.DeclaringSyntaxReferences)
                    {
                        SyntaxNode syntax = r.GetSyntax();
                        if (syntax is VariableDeclaratorSyntax v && v.Initializer != null)
                            value = v.Initializer.Value;
                        else if (syntax is PropertyDeclarationSyntax p && p.Initializer != null)
                            value = p.Initializer.Value;
                    }
                    if (value == null)
                        continue;

                    ITypeSymbol type = slot is IFieldSymbol f ? f.Type : ((IPropertySymbol)slot).Type;
                    var rewriter = value.SyntaxTree == _model.SyntaxTree
                        ? this
                        : new Rewriter(_context, _context.Compilation.GetSemanticModel(value.SyntaxTree), c, _sink, null);
                    string member = $"{(slot is IFieldSymbol ? "フィールド" : "プロパティ")} {slot.Name} の初期化子";
                    sb.Append($"{rewriter.Marker(value, member)}{ThisName}[{c.Slots[slot]}] = ({TypeText(type)})({rewriter.Flatten(rewriter.Visit(value))}); ");
                }
                return sb.ToString();
            }

            private IEnumerable<string> CopyProperty(ClassInfo c, PropertyDeclarationSyntax property)
            {
                IPropertySymbol symbol = _model.GetDeclaredSymbol(property);
                if (symbol == null || c.Slots.ContainsKey(symbol) || IsAutoProperty(symbol))
                    yield break;
                _member = $"プロパティ {symbol.Name}";

                string type = TypeText(symbol.Type);
                string self = symbol.IsStatic ? "" : $"{InstanceType} {ThisName}";

                if (property.ExpressionBody != null)
                {
                    yield return $"private static {type} {GetterName(c, symbol)}({self}) {{ {Marker(property.ExpressionBody.Expression)}return {Flatten(Visit(property.ExpressionBody.Expression))}; }}";
                    yield break;
                }

                foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
                {
                    if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                        yield return $"private static {type} {GetterName(c, symbol)}({self}) {Body(accessor.Body, accessor.ExpressionBody, true)}";
                    else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                        yield return $"private static void {SetterName(c, symbol)}({(self.Length > 0 ? self + ", " : "")}{type} value) {Body(accessor.Body, accessor.ExpressionBody, false)}";
                }
            }

            private string Body(BlockSyntax block, ArrowExpressionClauseSyntax arrow, bool returnsValue)
            {
                if (block != null)
                    return Flatten(Visit(block));
                if (arrow != null)
                    return returnsValue
                        ? $"{{ {Marker(arrow.Expression)}return {Flatten(Visit(arrow.Expression))}; }}"
                        : $"{{ {Marker(arrow.Expression)}{Flatten(Visit(arrow.Expression))}; }}";
                return "{ }";
            }

            /// <summary>
            /// 1 行にする（生成メンバーを '}' と同じ行に置くため）。コメントと改行は空白にし、
            /// 改行を含む文字列リテラルはエスケープした表記にする。
            /// </summary>
            private string Flatten(SyntaxNode node)
            {
                if (node.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective))
                {
                    Error(node, "普通のクラスのメソッドの中のプリプロセッサ指令（#if など）は未対応です");
                    return "";
                }

                SyntaxNode flat = node.ReplaceTokens(
                    node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken) && t.Text.Contains("\n")),
                    (o, _) => SyntaxFactory.Literal(o.ValueText).WithTriviaFrom(o));
                flat = flat.ReplaceTrivia(flat.DescendantTrivia(descendIntoTrivia: false),
                    (o, _) => SourceMap.IsMarker(o) ? o : SyntaxFactory.Space);

                string text = flat.ToFullString().Trim();
                if (text.Contains("\n"))
                {
                    Error(node, "普通のクラスのメソッドの中の、改行を含む補間文字列は未対応です");
                    return "";
                }
                return text;
            }
        }
    }
}
