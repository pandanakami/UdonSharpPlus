using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    internal static partial class CollectionLowering
    {
        /// <summary>
        /// LINQ（Redmine #58）。遅延評価にせず、呼んだ時点で結果を作る。
        ///
        ///   list.Where(x => x &gt; a).Select(x => x * 2).Sum()
        ///     → __linq_Sum_0(list, a)  （1 つのループに、各段のラムダの本体を埋め込んだ生成メソッド）
        ///
        /// - Where / Select / Distinct / Skip / Take は要素を 1 つずつ次の段に渡すので、途中のリストを作らない。First / Any / Take などは必要な分で止まる
        /// - OrderBy / ThenBy は、そこまでの値とキーをテンポラリにためて番号の並びを安定なマージソートで並べ替え、次のループで続きを処理する。
        ///   テンポラリは呼び出し箇所ごとのフィールドを Clear して使い回す（static のメソッドの中は毎回作る。決めたこと 5）
        /// - 結果がシーケンスのまま（変数に入れる、ToList()）なら、新しい DataList を作って返す。ToArray() は配列
        /// - 元は List・配列・LINQ の結果（DataList）。Dictionary は後で（決めたこと 4）
        /// - 汎用クラスのコピーの中では、拡張メソッドの呼び出しは static の形（Enumerable.Where(list, f)）になっているので、両方の形を受け付ける
        /// </summary>
        private sealed partial class Rewriter
        {
            private const string EnumerableName = "System.Linq.Enumerable";
            private const string IEnumerableName = "System.Collections.Generic.IEnumerable<T>";
            private const string IOrderedEnumerableName = "System.Linq.IOrderedEnumerable<TElement>";

            /// <summary>LINQ の結果の型（IEnumerable&lt;T&gt;・OrderBy の IOrderedEnumerable&lt;T&gt;）。書き換え後は DataList</summary>
            private static bool IsEnumerable(ITypeSymbol type) =>
                type is INamedTypeSymbol n && (n.OriginalDefinition.ToDisplayString() == IEnumerableName || n.OriginalDefinition.ToDisplayString() == IOrderedEnumerableName);

            private static bool IsLinqMethod(IMethodSymbol method) =>
                method != null && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == EnumerableName;

            private static readonly string[] LinqTerminals =
                { "ToList", "ToArray", "ToDictionary", "Any", "All", "Count", "First", "FirstOrDefault", "Last", "LastOrDefault", "Sum", "Min", "Max", "Average", "Contains",
                  "ElementAt", "ElementAtOrDefault", "Single", "SingleOrDefault", "Aggregate" };

            /// <summary>結果がシーケンスのままのメソッド（最後なら、新しい DataList に入れて返す）</summary>
            private static readonly string[] LinqSequences =
                { "Where", "Select", "SelectMany", "ToList", "Skip", "Take", "SkipWhile", "TakeWhile", "Distinct", "Reverse", "Concat", "Append", "Prepend",
                  "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy" };

            /// <summary>LINQ の 1 回の呼び出し（受け側と、受け側を除いた引数）</summary>
            private sealed class LinqCallInfo
            {
                public InvocationExpressionSyntax Node;
                public IMethodSymbol Method;   // 拡張メソッドの形（ReducedFrom）なら、そのまま
                public ExpressionSyntax Receiver;
                public List<ExpressionSyntax> Args;
                public string Name => Method.Name;
            }

            private LinqCallInfo AsLinqCall(ExpressionSyntax expression)
            {
                if (!(expression is InvocationExpressionSyntax node) || !(_model.SymbolInfoOf(node).Symbol is IMethodSymbol method) || !IsLinqMethod(method))
                    return null;
                var args = node.ArgumentList.Arguments.Select(a => a.Expression).ToList();
                if (method.ReducedFrom != null)
                    return node.Expression is MemberAccessExpressionSyntax access
                        ? new LinqCallInfo { Node = node, Method = method, Receiver = access.Expression, Args = args }
                        : null;
                // static の形：Enumerable.Where(list, f)。拡張メソッドだけ（Enumerable.Range など、元を取らない生成のメソッドは違う。#99）
                if (args.Count == 0 || !method.IsExtensionMethod)
                    return null;
                return new LinqCallInfo { Node = node, Method = method, Receiver = args[0], Args = args.Skip(1).ToList() };
            }

            /// <summary>
            /// 中身が DataList / DataDictionary になる値か（List・Dictionary・LINQ の呼び出し・LINQ の結果を var で受けた変数）。
            /// 外部の関数が返す IEnumerable（PlayerData.GetKeys など）は、Udon では本物の IEnumerable のまま（#62）
            /// </summary>
            private bool IsDataListBacked(ExpressionSyntax expression)
            {
                switch (expression)
                {
                    case null:
                        return false;
                    case ParenthesizedExpressionSyntax p:
                        return IsDataListBacked(p.Expression);
                    case InvocationExpressionSyntax invocation when _model.SymbolInfoOf(invocation).Symbol is IMethodSymbol m && IsLinqMethod(m):
                        return !IsLinqGenerator(m) || IsSupportedGenerator(m);
                }
                ITypeSymbol type = TypeOf(expression);
                if (IsList(type) || IsDictionary(type))
                    return true;
                // LINQ の結果を var で受けた変数
                if (IsEnumerable(type) && _model.SymbolInfoOf(expression).Symbol is ILocalSymbol local
                    && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator)
                    return IsDataListBacked(declarator.Initializer?.Value);
                return false;
            }

            private static string ForeignEnumerableMessage =>
                Loc.T("外部の関数が返す IEnumerable は、Udon では中を回せません（foreach・LINQ は使えません）", "An IEnumerable returned by an external function cannot be iterated in Udon (foreach and LINQ cannot be used)");

            private static string QueryMessage =>
                Loc.T("クエリ構文（from … select）は未対応です。メソッドの形（list.Where(x => …).Select(x => …)）で書いてください", "Query syntax (from … select) is not supported. Use the method form (list.Where(x => …).Select(x => …))");

            /// <summary>Enumerable.Range・Repeat・Empty など、元（List・配列）を取らない生成のメソッド</summary>
            private static bool IsLinqGenerator(IMethodSymbol method) =>
                IsLinqMethod(method) && method.ReducedFrom == null && !method.IsExtensionMethod;

            /// <summary>対応している生成のメソッド（#145）：Enumerable.Range(start, count)・Repeat(value, count)・Empty&lt;T&gt;()</summary>
            private static bool IsSupportedGenerator(IMethodSymbol method) =>
                IsLinqGenerator(method) && (method.Name == "Range" && method.Parameters.Length == 2 || method.Name == "Repeat" && method.Parameters.Length == 2
                                            || method.Name == "Empty" && method.Parameters.Length == 0);

            private static string GeneratorMessage(IMethodSymbol method) =>
                Loc.T($"'{method.Name}' : Enumerable.{method.Name} は未対応です（LINQ は List・配列・Dictionary と、Enumerable.Range・Repeat・Empty から始めてください）", $"'{method.Name}' : Enumerable.{method.Name} is not supported (start LINQ from a List, an array, a Dictionary, or Enumerable.Range / Repeat / Empty)");

            /// <summary>
            /// 中を回せない IEnumerable（DataList でない）を foreach・LINQ に渡したときのエラー。書き方に合わせて文言を変える（#99）。
            /// 変数の宣言の場所でエラーにしているもの（クエリ構文・Enumerable.Range などで初期化した変数）なら null
            /// </summary>
            private string NotIterableMessage(ExpressionSyntax source)
            {
                ExpressionSyntax origin = source;
                while (origin is ParenthesizedExpressionSyntax p)
                    origin = p.Expression;
                // 変数なら、宣言の初期化子までたどる
                if (_model.SymbolInfoOf(origin).Symbol is ILocalSymbol local
                    && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declarator)
                {
                    ExpressionSyntax value = declarator.Initializer?.Value;
                    while (value is ParenthesizedExpressionSyntax p)
                        value = p.Expression;
                    // クエリ構文・生成のメソッドは、宣言の場所でエラーにしているので重ねない
                    if (value is QueryExpressionSyntax)
                        return null;
                    if (value is InvocationExpressionSyntax generated && _model.SymbolInfoOf(generated).Symbol is IMethodSymbol g && IsLinqGenerator(g))
                        return null;
                    // 外部の関数の戻り値でなければ、自分で IEnumerable 型にした変数
                    if (!(value is InvocationExpressionSyntax))
                        return Loc.T($"'{local.Name}' : IEnumerable 型の変数は未対応です。List・配列の型のまま（または var で）使ってください", $"'{local.Name}' : Variables of type IEnumerable are not supported. Keep the List or array type (or use var)");
                    return $"'{Text(source)}' : {ForeignEnumerableMessage}";
                }
                // LINQ・foreach に直接書いたクエリ構文は、ここで止めるので中は見ない。ここでクエリ構文のエラーにする
                if (origin is QueryExpressionSyntax)
                    return QueryMessage;
                if (origin is InvocationExpressionSyntax invocation && _model.SymbolInfoOf(invocation).Symbol is IMethodSymbol m && IsLinqGenerator(m))
                    return GeneratorMessage(m);
                return $"'{Text(source)}' : {ForeignEnumerableMessage}";
            }

            public override SyntaxNode VisitQueryExpression(QueryExpressionSyntax node)
            {
                if (_helpers == null)
                    return base.VisitQueryExpression(node);
                Error(node, QueryMessage);
                return node;
            }

            private static string EnumerableConversionMessage =>
                Loc.T("List・Dictionary・LINQ の結果を IEnumerable<T> などの型として扱うのは未対応です（var で受けるか、List・配列にしてください）", "Using a List, Dictionary or LINQ result as IEnumerable<T> or a similar type is not supported (use var, or make it a List or array)");

            /// <summary>SelectMany の集まりを返すラムダの return の式（戻り値の型は IEnumerable&lt;T&gt; だが、DataList のまま返してよい。#143）</summary>
            private readonly HashSet<SyntaxNode> _collectionReturns = new HashSet<SyntaxNode>();

            /// <summary>中身が DataList の値を、IEnumerable&lt;T&gt; などのインタフェース型の場所に入れていればエラー（#62）</summary>
            private bool EnumerableConversion(ExpressionSyntax value)
            {
                if (_helpers == null || value == null || _collectionReturns.Contains(value))
                    return false;
                // GroupBy のグループ（First() などで取り出したもの）は、IGrouping・object の場所には object[] のまま入れてよい。
                // IEnumerable<T> などの場所はエラー（中身は object[] で、そのままでは回せない。#140）
                if (IsGrouping(TypeOf(value)))
                    return GroupConversion(value);
                if (!IsDataListBacked(value))
                    return false;
                ITypeSymbol target = _model.GetTypeInfo(value).ConvertedType;
                if (target == null || target.TypeKind != TypeKind.Interface)
                    return false;
                Error(value, $"'{Text(value)}' : {EnumerableConversionMessage}");
                return true;
            }

            /// <summary>グループ（object[]）を、IGrouping 以外のインタフェース型（IEnumerable&lt;T&gt; など）の場所に入れていればエラー（#140）</summary>
            private bool GroupConversion(ExpressionSyntax value)
            {
                ITypeSymbol target = _model.GetTypeInfo(value).ConvertedType;
                if (target == null || target.TypeKind != TypeKind.Interface || IsGrouping(target))
                    return false;
                Error(value, Loc.T($"'{Text(value)}' : グループを {target.ToDisplayString()} の型として扱うのは未対応です（var・IGrouping<K, T> で受けるか、ToList() にしてください）",
                    $"'{Text(value)}' : Using a group as {target.ToDisplayString()} is not supported (use var or IGrouping<K, T>, or call ToList())"));
                return true;
            }

            public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
            {
                // IEnumerable<int> a = new List<int>(); など（var は DataList のまま受けるので対象外）
                if (node.Initializer != null && node.Parent is VariableDeclarationSyntax declaration && !declaration.Type.IsVar
                    && EnumerableConversion(node.Initializer.Value))
                    return node;
                return base.VisitVariableDeclarator(node);
            }

            public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node) =>
                EnumerableConversion(node.Expression) ? node : base.VisitReturnStatement(node);

            public override SyntaxNode VisitArrowExpressionClause(ArrowExpressionClauseSyntax node) =>
                EnumerableConversion(node.Expression) ? node : base.VisitArrowExpressionClause(node);

            /// <summary>
            /// LINQ のつないだ呼び出し（一番外側）を、生成するメソッドの呼び出しにする。
            /// 一番外側でない呼び出しは、外側の呼び出しの中でまとめて扱うので、ここには来ない
            /// </summary>
            private SyntaxNode LinqCall(InvocationExpressionSyntax node)
            {
                // つないだ呼び出しを、内側から順に並べる
                var calls = new List<LinqCallInfo>();
                ExpressionSyntax current = node;
                for (LinqCallInfo call = AsLinqCall(current); call != null; call = AsLinqCall(current))
                {
                    calls.Insert(0, call);
                    current = call.Receiver;
                    // First() などで取り出したグループ（#140）：そこまでを別の LINQ にして、グループを元にする
                    if (IsGrouping(TypeOf(current)))
                        break;
                }
                ExpressionSyntax source = current;
                // 元が Enumerable.Range・Repeat・Empty（#145）
                ExpressionSyntax bare = source;
                while (bare is ParenthesizedExpressionSyntax paren)
                    bare = paren.Expression;
                IMethodSymbol generator = bare is InvocationExpressionSyntax && _model.SymbolInfoOf(bare).Symbol is IMethodSymbol gm && IsLinqGenerator(gm) ? gm : null;
                if (generator != null && !IsSupportedGenerator(generator))
                {
                    Error(bare, GeneratorMessage(generator));
                    return node;
                }
                if (calls.Count == 0 && generator == null)
                {
                    Error(node, Loc.T("この形の LINQ の呼び出しは未対応です", "This form of LINQ call is not supported"));
                    return node;
                }

                // 元：List・配列・LINQ の結果（var で受けたもの。中身は DataList）・Dictionary・dict.Keys / dict.Values（#61）
                ITypeSymbol sourceType = TypeOf(source);
                ITypeSymbol element;
                bool isArray = false;
                bool pairMode = false;                   // Dictionary：kv をキーの DataToken として流している（#61 決めたこと 1）
                ITypeSymbol pairKey = null, pairValue = null;
                string sourceText = null, sourceParam = DataListType;
                if (IsEnumerable(sourceType) && !IsDataListBacked(source))
                {
                    string message = NotIterableMessage(source);
                    if (message != null)
                        Error(source, message);
                    return node;
                }
                if (IsList(sourceType) || IsEnumerable(sourceType))
                    element = ((INamedTypeSymbol)sourceType).TypeArguments[0];
                else if (IsGrouping(sourceType))
                {
                    // GroupBy のグループ（g.Count()・g.Sum(...)・g.Select(...)。#140）：要素の DataList を元にする
                    element = ((INamedTypeSymbol)sourceType).TypeArguments[1];
                    sourceText = $"(({DataListType})({Text(Visit(source))})[2])";
                }
                else if (IsDictionary(sourceType))
                {
                    pairMode = true;
                    pairKey = KeyOf(sourceType);
                    pairValue = ValueOf(sourceType);
                    element = calls[0].Method.TypeArguments[0];   // KeyValuePair<K, V>
                    sourceParam = DataDictionaryType;
                }
                else if ((IsKeys(sourceType) || IsValues(sourceType)) && source is MemberAccessExpressionSyntax keysAccess)
                {
                    // dict.Keys / dict.Values → GetKeys() / GetValues() の DataList
                    ITypeSymbol dictionary = DictionaryOfCollection(sourceType);
                    element = IsKeys(sourceType) ? KeyOf(dictionary) : ValueOf(dictionary);
                    sourceText = $"({Text(Visit(keysAccess.Expression))}).{(IsKeys(sourceType) ? "GetKeys" : "GetValues")}()";
                }
                else if (sourceType is IArrayTypeSymbol array && array.Rank == 1)
                {
                    element = array.ElementType;
                    isArray = true;
                }
                else
                {
                    Error(node, Loc.T($"'{calls[0].Name}' : LINQ は List・配列・Dictionary にだけ使えます（{sourceType?.ToDisplayString() ?? "この型"} は未対応です）", $"'{calls[0].Name}' : LINQ can only be used on a List, an array or a Dictionary ({sourceType?.ToDisplayString() ?? "this type"} is not supported)"));
                    return node;
                }

                IMethodSymbol enclosing = EnclosingMethod(node);
                if (enclosing == null)
                {
                    Error(node, Loc.T("LINQ は、フィールドの初期化子の中では未対応です（メソッドの中で呼んでください）", "LINQ is not supported inside a field initializer (call it inside a method)"));
                    return node;
                }

                string terminalName = calls.Count == 0 ? generator.Name : calls[calls.Count - 1].Name;
                string name = $"__linq_{terminalName}_{_lambdaCount++}";
                bool isStatic = enclosing.IsStatic;
                string modifiers = isStatic ? "private static" : "private";
                string marker = SourceMap.MarkerFor(node);

                // ラムダの引数の名前が、別のラムダの外の変数と同じなら（C# 8 以降は書ける）、そのラムダは本体のメソッドにする
                // （ループの中のブロックで宣言すると、生成メソッドの引数と名前がぶつかるため）
                var lambdaArgs = calls.SelectMany(c => c.Args).Where(a => a is LambdaExpressionSyntax).ToList();
                var captureNames = new HashSet<string>(lambdaArgs.SelectMany(a => CaptureNames((LambdaExpressionSyntax)a)));

                var captures = new List<Capture>();
                var extraParams = new StringBuilder();   // 生成メソッドの引数（Contains の値、Skip / Take の数）
                var extraArgs = new StringBuilder();
                var preamble = new StringBuilder();      // 最初のループの前（テンポラリの Clear、数の初期化）
                var code = new StringBuilder();          // 閉じたループと並べ替え
                var body = new StringBuilder();          // 今のループの、要素ごとの処理
                var stops = new List<string>();          // 今のループを止める印（Take）
                string header;                           // 今のループの頭（{STOP} に止める条件が入る）
                string returnType, post = "";
                string aggregateType = null;             // Aggregate の結果の型
                ITypeSymbol type = element;              // 今の段の要素の型
                int v = 0;                               // 今の段の値の変数 __v{v}
                int stage = 0;
                int segment = 0;

                // 元を回すループ
                header = isArray
                    ? $"int __n0 = __src.Length; for (int __i0 = 0; __i0 < __n0{{STOP}}; __i0++) {{ {TypeText(element)} __v0 = __src[__i0]; "
                    : pairMode
                        ? $"{DataListType} __keys = __src.GetKeys(); int __n0 = __keys.Count; for (int __i0 = 0; __i0 < __n0{{STOP}}; __i0++) {{ {DataTokenType} __v0 = __keys[__i0]; "
                        : $"int __n0 = __src.Count; for (int __i0 = 0; __i0 < __n0{{STOP}}; __i0++) {{ {TypeText(element)} __v0 = {Unwrap("__src[__i0]", element)}; ";

                // 生成のメソッド（#145）：__src は Range の最初の値・Repeat の値（Empty は使わない）。回数は __gcount
                if (generator != null)
                {
                    var generatorArgs = ((InvocationExpressionSyntax)bare).ArgumentList.Arguments;
                    string elementText = TypeText(element);
                    if (generator.Name == "Empty")
                    {
                        sourceParam = "int";
                        sourceText = "0";
                        header = $"for (int __i0 = 0; __i0 < 0{{STOP}}; __i0++) {{ {elementText} __v0 = default({elementText}); ";
                    }
                    else
                    {
                        sourceParam = generator.Name == "Range" ? "int" : elementText;
                        sourceText = Text(Visit(generatorArgs[0].Expression));
                        extraParams.Append(", int __gcount");
                        extraArgs.Append(", ").Append(Text(Visit(generatorArgs[1].Expression)));
                        preamble.Append(Loc.T($"if (__gcount < 0) {{ {Halt(generator.Name, "count が負です（C# では ArgumentOutOfRangeException）")}}} ", $"if (__gcount < 0) {{ {Halt(generator.Name, "count is negative (C#: ArgumentOutOfRangeException)")}}} "));
                        if (generator.Name == "Range")   // C# と同じく、最後の値が int の範囲を超えるなら、作る時点で止める
                            preamble.Append(Loc.T($"if (__gcount > 0 && __src > {int.MaxValue} - (__gcount - 1)) {{ {Halt("Range", "start + count - 1 が int の範囲を超えます（C# では ArgumentOutOfRangeException）")}}} ", $"if (__gcount > 0 && __src > {int.MaxValue} - (__gcount - 1)) {{ {Halt("Range", "start + count - 1 is out of the range of int (C#: ArgumentOutOfRangeException)")}}} "));
                        header = $"for (int __i0 = 0; __i0 < __gcount{{STOP}}; __i0++) {{ {elementText} __v0 = {(generator.Name == "Range" ? "__src + __i0" : "__src")}; ";
                    }
                }

                // Dictionary の kv（キーとして流している今の値）
                PairLoop CurrentPair() => new PairLoop { Dictionary = "__src", Key = $"__v{v}", KeyType = pairKey, ValueType = pairValue };

                // 今の値（kv をキーとして流しているときは、外に出すために object[] にする。#61 決めたこと 2）
                string ValueExpr() => pairMode ? PairObject(CurrentPair()) : $"__v{v}";

                // SelectMany の内側のループ（今のループの本体の中で開いたもの）。From より前の止める印（Take）は、外側のループだけを止める
                var nested = new List<(int Id, int From)>();

                void CloseLoop(string tail)
                {
                    string StopsFrom(int from) => string.Concat(stops.Skip(from).Select(s => $" && !{s}"));
                    string text = body.ToString();
                    foreach (var (id, from) in nested)
                        text = text.Replace($"{{STOP:{id}}}", StopsFrom(from));
                    code.Append(header.Replace("{STOP}", StopsFrom(0))).Append(text).Append(tail).Append("} ");
                    for (int i = 0; i < nested.Count; i++)
                        code.Append("} ");
                    body.Clear();
                    stops.Clear();
                    nested.Clear();
                }

                // 呼び出し箇所ごとのテンポラリ（決めたこと 5）。static のメソッドの中はフィールドを使えないので、毎回作る
                string Temp(string role, string typeName)
                {
                    if (isStatic)
                    {
                        preamble.Append($"{typeName} __{role} = new {typeName}(); ");
                        return $"__{role}";
                    }
                    string field = $"{name}_{role}";
                    _helpers[field] = $"private {typeName} {field} = new {typeName}();";
                    preamble.Append($"{field}.Clear(); ");
                    return field;
                }

                // 生成メソッドの引数を足す（呼び出し側の式は、元の位置で評価する）
                string Extra(string typeName, string paramName, ExpressionSyntax argument)
                {
                    extraParams.Append($", {typeName} {paramName}");
                    extraArgs.Append(", ").Append(Text(Visit(argument)));
                    return paramName;
                }

                // ラムダを 1 つ準備して、外の変数をまとめる
                // arity：ラムダの引数の数。pairIndex：要素（kv）を受ける引数の番号（kv.Key / kv.Value の書き換えをしないなら -1）
                PreparedLambda Lambda(LinqCallInfo call, ExpressionSyntax fn, out ITypeSymbol resultType, out bool pairBound, int arity, int pairIndex)
                {
                    pairBound = false;
                    resultType = null;
                    if (!(_model.GetTypeInfo(fn).ConvertedType is INamedTypeSymbol delegateType) || delegateType.DelegateInvokeMethod == null
                        || delegateType.DelegateInvokeMethod.Parameters.Length != arity)
                    {
                        Error(fn, Loc.T($"'{call.Name}' : この形のラムダは未対応です（ラムダかメソッド名を渡してください）", $"'{call.Name}' : This form of lambda is not supported (pass a lambda or a method name)"));
                        return null;
                    }
                    resultType = delegateType.DelegateInvokeMethod.ReturnType;
                    bool conflict = fn is LambdaExpressionSyntax l && LambdaParameterNames(l).Any(captureNames.Contains);

                    // kv をキーとして流しているときは、式の本体のラムダなら kv.Key / kv.Value をキーと dict[キー] に書き換える
                    // （本体のメソッドにするものやメソッド名は、object[] にして渡す）
                    ISymbol pairParameter = null;
                    if (pairMode && pairIndex >= 0 && !conflict && fn is LambdaExpressionSyntax pairLambda && !(pairLambda.Body is BlockSyntax))
                    {
                        ParameterSyntax parameter = pairLambda is SimpleLambdaExpressionSyntax simple
                            ? (pairIndex == 0 ? simple.Parameter : null)
                            : ((ParenthesizedLambdaExpressionSyntax)pairLambda).ParameterList.Parameters.ElementAtOrDefault(pairIndex);
                        pairParameter = parameter == null ? null : _model.GetDeclaredSymbol(parameter);
                    }
                    if (pairParameter != null)
                        _pairs[pairParameter] = CurrentPair();
                    PreparedLambda prepared;
                    try
                    {
                        prepared = PrepareLambda(fn, delegateType.DelegateInvokeMethod, $"{name}_body{stage}", modifiers, conflict);
                    }
                    finally
                    {
                        if (pairParameter != null)
                            _pairs.Remove(pairParameter);
                    }
                    pairBound = pairParameter != null;
                    if (prepared == null)
                        return null;
                    foreach (Capture c in prepared.Captures)
                    {
                        Capture existing = captures.FirstOrDefault(x => x.Name == c.Name);
                        if (existing == null)
                            captures.Add(c);
                        else
                            existing.Written |= c.Written;
                    }
                    return prepared;
                }

                // ラムダを今の値に当てはめて、結果を変数に入れる（ラムダの引数は、兄弟のブロックの中だけで宣言する）。結果の変数名を返す
                // 添字付きのラムダ（(x, i) => ...。Where・Select・SelectMany・TakeWhile・SkipWhile）なら、その段に来た要素を数えて 2 つ目の引数にする（#142）
                string Apply(LinqCallInfo call, ExpressionSyntax fn, out ITypeSymbol resultType, System.Func<string, string> convert = null)
                {
                    bool indexed = _model.GetTypeInfo(fn).ConvertedType is INamedTypeSymbol d && d.DelegateInvokeMethod != null && d.DelegateInvokeMethod.Parameters.Length == 2;
                    if (!indexed)
                        return ApplyWith(call, fn, out resultType, 0, convert, (TypeText(type), ValueExpr()));
                    string counter = $"__ix{stage}";
                    preamble.Append($"int {counter} = -1; ");
                    body.Append($"{counter}++; ");
                    return ApplyWith(call, fn, out resultType, 0, convert, (TypeText(type), ValueExpr()), ("int", counter));
                }

                // ラムダの引数を binds の値にして評価し、結果を変数に入れる。結果の変数名を返す。convert は、評価した式を包む（配列を DataList にするなど）
                string ApplyWith(LinqCallInfo call, ExpressionSyntax fn, out ITypeSymbol resultType, int pairIndex, System.Func<string, string> convert,
                    params (string Type, string Value)[] binds)
                {
                    PreparedLambda lambda = Lambda(call, fn, out resultType, out bool pairBound, binds.Length, pairIndex);
                    if (lambda == null)
                        return null;
                    string result = $"__r{stage++}";
                    var bind = new StringBuilder();
                    for (int i = 0; i < binds.Length; i++)
                        if (!(pairBound && i == pairIndex))
                            bind.Append($"{binds[i].Type} {lambda.Names[i]} = {binds[i].Value}; ");
                    string value = convert == null ? lambda.Each : convert(lambda.Each);
                    body.Append($"{TypeText(resultType)} {result}; {{ {bind}{result} = ({value}); }} ");
                    return result;
                }

                // ここまでの値をテンポラリにためてループを閉じる（Reverse・Concat・Append・Prepend）。次のループは、ためたものを回す
                string BufferSegment(bool backward, System.Action<string> beforeLoop, System.Action<string> afterLoop)
                {
                    int s = segment;
                    string buffer = Temp($"b{s}", DataListType);
                    beforeLoop?.Invoke(buffer);
                    body.Append($"{buffer}.Add({(pairMode ? $"__v{v}" : Wrap($"__v{v}", type))}); ");   // kv はキーのまま
                    CloseLoop("");
                    afterLoop?.Invoke(buffer);
                    segment++;
                    int next = segment;
                    v++;
                    string item = pairMode ? $"{DataTokenType} __v{v} = {buffer}[__i{next}]; " : $"{TypeText(type)} __v{v} = {Unwrap($"{buffer}[__i{next}]", type)}; ";
                    header = backward
                        ? $"for (int __i{next} = {buffer}.Count - 1; __i{next} >= 0{{STOP}}; __i{next}--) {{ {item}"
                        : $"int __m{s} = {buffer}.Count; for (int __i{next} = 0; __i{next} < __m{s}{{STOP}}; __i{next}++) {{ {item}";
                    return buffer;
                }

                // Where：条件に合わなければ次の要素へ
                bool Filter(LinqCallInfo call, ExpressionSyntax fn)
                {
                    string ok = Apply(call, fn, out _);
                    if (ok == null)
                        return false;
                    body.Append($"if (!{ok}) continue; ");
                    return true;
                }

                // Select：値を変換する
                bool Map(LinqCallInfo call, ExpressionSyntax fn)
                {
                    string mapped = Apply(call, fn, out ITypeSymbol resultType);
                    if (mapped == null)
                        return false;
                    body.Append($"{TypeText(resultType)} __v{v + 1} = {mapped}; ");
                    v++;
                    type = resultType;
                    pairMode = false;
                    return true;
                }

                // OrderBy / ThenBy：ここまでの値とキーをテンポラリにため、番号の並びを安定なマージソートで並べ替えてから、次のループで続きを処理する
                bool Order(List<(LinqCallInfo Call, bool Descending)> keys)
                {
                    int s = segment;
                    string buffer = Temp($"b{s}", DataListType);
                    var keyLists = new List<(string List, ITypeSymbol Type, bool Descending)>();
                    foreach (var (call, descending) in keys)
                    {
                        if (call.Args.Count != 1)
                        {
                            Error(call.Node, Loc.T($"'{call.Name}' : 比較子を渡す形は未対応です（キーを返すラムダだけ渡してください）", $"'{call.Name}' : Passing a comparer is not supported (pass only a lambda that returns the key)"));
                            return false;
                        }
                        string key = Apply(call, call.Args[0], out ITypeSymbol keyType);
                        if (key == null)
                            return false;
                        if (CompareText("a", "b", keyType) == null)
                        {
                            Error(call.Args[0], Loc.T($"'{call.Name}' : この型のキー（{keyType.ToDisplayString()}）での並べ替えは未対応です（数値・string・bool・char・enum が使えます）", $"'{call.Name}' : Sorting by a key of this type ({keyType.ToDisplayString()}) is not supported (numbers, string, bool, char and enum are supported)"));
                            return false;
                        }
                        string list = Temp($"k{s}_{keyLists.Count}", DataListType);
                        body.Append($"{list}.Add({Wrap(key, keyType)}); ");
                        keyLists.Add((list, keyType, descending));
                    }
                    body.Append($"{buffer}.Add({(pairMode ? $"__v{v}" : Wrap($"__v{v}", type))}); ");   // kv はキーのまま
                    CloseLoop("");

                    // キーを順に比べる（同じなら次のキー）
                    var compare = new StringBuilder("int __c = 0; ");
                    for (int k = 0; k < keyLists.Count; k++)
                    {
                        var (list, keyType, descending) = keyLists[k];
                        string kt = TypeText(keyType);
                        compare.Append(k == 0 ? "{ " : "if (__c == 0) { ")
                            .Append($"{kt} __a = {Unwrap($"{list}[__x]", keyType)}; {kt} __b = {Unwrap($"{list}[__y]", keyType)}; ")
                            .Append($"__c = {(descending ? "-" : "")}({CompareText("__a", "__b", keyType)}); }} ");
                    }

                    string order = Temp($"o{s}", DataListType), work = Temp($"w{s}", DataListType);
                    code.Append($"int __m{s} = {buffer}.Count; for (int __j = 0; __j < __m{s}; __j++) {{ {order}.Add(new {DataTokenType}(__j)); {work}.Add(new {DataTokenType}(0)); }} ")
                        .Append($"{DataListType} __p{s} = {order}; {DataListType} __q{s} = {work}; ")
                        .Append($"for (int __w = 1; __w < __m{s}; __w *= 2) {{ for (int __lo = 0; __lo < __m{s}; __lo += 2 * __w) {{ ")
                        .Append($"int __mid = __lo + __w; if (__mid > __m{s}) __mid = __m{s}; int __hi = __lo + 2 * __w; if (__hi > __m{s}) __hi = __m{s}; ")
                        .Append("int __l = __lo; int __r = __mid; ")
                        .Append("for (int __k = __lo; __k < __hi; __k++) { bool __left; ")
                        .Append("if (__l >= __mid) __left = false; else if (__r >= __hi) __left = true; ")
                        .Append($"else {{ int __x = __p{s}[__l].Int; int __y = __p{s}[__r].Int; {compare}__left = __c <= 0; }} ")
                        .Append($"if (__left) {{ __q{s}[__k] = __p{s}[__l]; __l++; }} else {{ __q{s}[__k] = __p{s}[__r]; __r++; }} }} }} ")
                        .Append($"{DataListType} __t = __p{s}; __p{s} = __q{s}; __q{s} = __t; }} ");

                    // 並べ替えた順に回す次のループ
                    segment++;
                    int next = segment;
                    v++;
                    header = pairMode
                        ? $"for (int __i{next} = 0; __i{next} < __m{s}{{STOP}}; __i{next}++) {{ {DataTokenType} __v{v} = {buffer}[__p{s}[__i{next}].Int]; "
                        : $"for (int __i{next} = 0; __i{next} < __m{s}{{STOP}}; __i{next}++) {{ {TypeText(type)} __v{v} = {Unwrap($"{buffer}[__p{s}[__i{next}].Int]", type)}; ";
                    return true;
                }

                // GroupBy（#140）：キーごとに、要素をグループ（object[]：[型の印, キー, 要素の DataList]）にためてループを閉じ、
                // 次のループでグループを回す。グループの順番・グループの中の順番は、C# と同じく最初に出た順
                bool Group(LinqCallInfo call)
                {
                    var groupArgs = call.Args;
                    string key = Apply(call, groupArgs[0], out ITypeSymbol keyType);
                    if (key == null)
                        return false;
                    // キーを引く方法：数値・string・bool・enum は DataDictionary、char は int にして DataDictionary、
                    // 参照（普通のクラス・UdonSharpBehaviour など）は C# と同じく参照で比べる（DataList の IndexOf）
                    bool byDictionary = keyType.TypeKind == TypeKind.Enum || keyType.SpecialType == SpecialType.System_Char
                                        || Accessor(keyType) is string keyAccessor && keyAccessor != "DataList" && keyAccessor != "DataDictionary";
                    if (!byDictionary && !keyType.IsReferenceType)
                    {
                        // タプルのキーは、TupleLowering がもうエラーにしている（1 件だけにする）
                        if (keyType.IsTupleType)
                            return false;
                        Error(groupArgs[0],Loc.T($"'GroupBy' : この型のキー（{keyType.ToDisplayString()}）は未対応です（数値・string・bool・char・enum と、クラスの参照が使えます）",
                            $"'GroupBy' : Keys of this type ({keyType.ToDisplayString()}) are not supported (numbers, string, bool, char, enum and class references are supported)"));
                        return false;
                    }
                    string elementValue = ValueExpr();
                    ITypeSymbol elementType = type;
                    if (groupArgs.Count == 2)
                    {
                        elementValue = Apply(call, groupArgs[1], out elementType);
                        if (elementValue == null)
                            return false;
                    }

                    int s = segment;
                    int id = stage++;
                    string groups = Temp($"g{s}", DataListType);
                    string grp = $"__grp{id}", token = $"__gk{id}", found = $"__gx{id}";
                    string make = $"{grp} = new object[] {{ \"{GroupTag}\", (object)({key}), new {DataListType}() }}; {groups}.Add(new {DataTokenType}((object){grp})); ";
                    body.Append($"object[] {grp}; ");
                    string lookup;
                    if (byDictionary)
                    {
                        string index = Temp($"gi{s}", DataDictionaryType);
                        string tokenValue = keyType.SpecialType == SpecialType.System_Char ? $"new {DataTokenType}((int)({key}))" : Wrap(key, keyType);
                        lookup = $"{DataTokenType} {token} = {tokenValue}; "
                                 + $"if ({index}.TryGetValue({token}, out {DataTokenType} {found})) {grp} = (object[])({groups}[{found}.Int]).Reference; "
                                 + $"else {{ {index}.SetValue({token}, new {DataTokenType}({groups}.Count)); {make}}} ";
                    }
                    else
                    {
                        string keys = Temp($"gk{s}", DataListType);
                        lookup = $"{DataTokenType} {token} = {Wrap(key, keyType)}; int {found} = {keys}.IndexOf({token}); "
                                 + $"if ({found} >= 0) {grp} = (object[])({groups}[{found}]).Reference; else {{ {keys}.Add({token}); {make}}} ";
                    }
                    if (keyType.IsReferenceType)
                    {
                        // null のキー（C# では 1 つのグループになる）
                        string nullIndex = $"__gnull{id}";
                        preamble.Append($"int {nullIndex} = -1; ");
                        body.Append($"if ((object)({key}) == null) {{ if ({nullIndex} >= 0) {grp} = (object[])({groups}[{nullIndex}]).Reference; else {{ {nullIndex} = {groups}.Count; {make}}} }} else {{ {lookup}}} ");
                    }
                    else
                        body.Append(lookup);
                    body.Append($"(({DataListType}){grp}[2]).Add({Wrap(elementValue, elementType)}); ");
                    CloseLoop("");

                    segment++;
                    int next = segment;
                    v++;
                    type = ((INamedTypeSymbol)call.Method.ReturnType).TypeArguments[0];   // IGrouping<TKey, TElement>
                    pairMode = false;
                    header = $"int __m{s} = {groups}.Count; for (int __i{next} = 0; __i{next} < __m{s}{{STOP}}; __i{next}++) {{ object[] __v{v} = (object[])({groups}[__i{next}]).Reference; ";
                    return true;
                }

                for (int index = 0; index < calls.Count; index++)
                {
                    LinqCallInfo call = calls[index];
                    bool last = index == calls.Count - 1;
                    var args = call.Args;

                    switch (call.Name)
                    {
                        case "Where" when args.Count == 1 && DistinctEquality(args[0]) is string equality:
                        {
                            // 匿名型の Distinct()（#138）：見たことのある値と、値の比較（__Eq）で比べる。最初に出たものを残す
                            string seen = Temp($"seen{stage}", DataListType);
                            body.Append($"bool __dup{stage} = false; int __sn{stage} = {seen}.Count; for (int __sj{stage} = 0; __sj{stage} < __sn{stage}; __sj{stage}++) {{ ")
                                .Append($"if ({equality}({Unwrap($"{seen}[__sj{stage}]", type)}, __v{v})) {{ __dup{stage} = true; break; }} }} ")
                                .Append($"if (__dup{stage}) continue; {seen}.Add({Wrap($"__v{v}", type)}); ");
                            stage++;
                            continue;
                        }
                        case "Where" when args.Count == 1:
                            if (!Filter(call, args[0]))
                                return node;
                            continue;
                        case "Select" when args.Count == 1:
                            if (!Map(call, args[0]))
                                return node;
                            continue;
                        case "ToList" when args.Count == 0 && !last:
                        case "ToArray" when args.Count == 0 && !last:
                            continue;   // 途中の ToList / ToArray は、結果が外に見えないので何もしない
                        case "Skip" when args.Count == 1:
                        {
                            string count = Extra("int", $"__skip{stage}", args[0]);
                            preamble.Append($"int __skipped{stage} = 0; ");
                            body.Append($"if (__skipped{stage} < {count}) {{ __skipped{stage}++; continue; }} ");
                            stage++;
                            continue;
                        }
                        case "Take" when args.Count == 1:
                        {
                            // C# と同じく、数を取り終えたら次の要素を取りに行かない（ループの条件で止める）
                            string count = Extra("int", $"__take{stage}", args[0]);
                            preamble.Append($"int __taken{stage} = 0; bool __stop{stage} = {count} <= 0; ");
                            body.Append($"__taken{stage}++; if (__taken{stage} >= {count}) __stop{stage} = true; ");
                            stops.Add($"__stop{stage}");
                            stage++;
                            continue;
                        }
                        case "TakeWhile" when args.Count == 1:
                        {
                            // 条件に合わない要素が来たら、そこで止める（その先の要素は取りに行かない）
                            string ok = Apply(call, args[0], out _);
                            if (ok == null)
                                return node;
                            preamble.Append($"bool __stop{stage} = false; ");
                            body.Append($"if (!{ok}) {{ __stop{stage} = true; continue; }} ");
                            stops.Add($"__stop{stage}");
                            stage++;
                            continue;
                        }
                        case "SkipWhile" when args.Count == 1:
                        {
                            // 条件に合う間は飛ばす。合わない要素が来たら、その先は条件を呼ばない（C# と同じ）
                            int start = body.Length;
                            string ok = Apply(call, args[0], out _);
                            if (ok == null)
                                return node;
                            string applied = body.ToString(start, body.Length - start);
                            body.Length = start;
                            preamble.Append($"bool __skipping{stage} = true; ");
                            body.Append($"if (__skipping{stage}) {{ {applied}if ({ok}) continue; __skipping{stage} = false; }} ");
                            stage++;
                            continue;
                        }
                        case "SelectMany" when args.Count == 1 || args.Count == 2:
                        {
                            // 内側のループを開く（#143）。集まりは List・配列・LINQ の結果
                            // 集まりの実際の型（デリゲートの戻り値の型は IEnumerable<T> なので、ラムダの本体・return の式から取る）
                            var returned = new List<ExpressionSyntax>();
                            bool block = false;
                            if (args[0] is AnonymousFunctionExpressionSyntax collectionLambda)
                            {
                                if (collectionLambda.Body is ExpressionSyntax collectionBody)
                                    returned.Add(collectionBody);
                                else
                                {
                                    block = true;
                                    returned.AddRange(collectionLambda.Body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax))
                                        .OfType<ReturnStatementSyntax>().Where(r => r.Expression != null).Select(r => r.Expression));
                                }
                            }
                            ITypeSymbol collection = returned.Count > 0 ? TypeOf(returned[0]) : (_model.SymbolInfoOf(args[0]).Symbol as IMethodSymbol)?.ReturnType;
                            ITypeSymbol inner;
                            System.Func<string, string> convert = null;
                            if (collection is IArrayTypeSymbol innerArray && innerArray.Rank == 1 && !block)
                            {
                                inner = innerArray.ElementType;
                                string fromArray = FromArrayHelper(inner);
                                convert = e => $"{fromArray}({e})";
                            }
                            else if (IsGrouping(collection) && !block)
                            {
                                // グループ（groups.SelectMany(g => g)。#140）：要素の DataList を回す
                                inner = ((INamedTypeSymbol)collection).TypeArguments[1];
                                convert = e => $"(({DataListType})({e})[2])";
                            }
                            else if (IsList(collection) && returned.All(r => IsList(TypeOf(r)))
                                     || IsEnumerable(collection) && returned.Count > 0 && returned.All(IsDataListBacked))
                                inner = ((INamedTypeSymbol)collection).TypeArguments[0];
                            else
                            {
                                Error(args[0], Loc.T("'SelectMany' : 集まりを返すラムダは、List・配列・LINQ の結果を返してください（{ ... } の本体のラムダは、List か LINQ の結果を返してください）", "'SelectMany' : The lambda that returns the collection must return a List, an array or a LINQ result (a lambda with a { ... } body must return a List or a LINQ result)"));
                                return node;
                            }
                            foreach (ExpressionSyntax r in returned)
                                _collectionReturns.Add(r);
                            string outerType = TypeText(type), outerValue = ValueExpr();
                            string list = Apply(call, args[0], out _, convert);
                            if (list == null)
                                return node;
                            int id = stage++;
                            string outer = $"__o{id}";
                            if (args.Count == 2)
                                body.Append($"{outerType} {outer} = {outerValue}; ");
                            body.Append($"int __cn{id} = {list}.Count; for (int __ci{id} = 0; __ci{id} < __cn{id}{{STOP:{id}}}; __ci{id}++) {{ ")
                                .Append($"{TypeText(inner)} __v{v + 1} = {Unwrap($"{list}[__ci{id}]", inner)}; ");
                            nested.Add((id, stops.Count));
                            v++;
                            type = inner;
                            pairMode = false;
                            if (args.Count == 2)
                            {
                                // 結果を作るラムダ：(外側の要素, 内側の要素) => ...
                                string mapped = ApplyWith(call, args[1], out ITypeSymbol resultType, -1, null, (outerType, outer), (TypeText(inner), $"__v{v}"));
                                if (mapped == null)
                                    return node;
                                body.Append($"{TypeText(resultType)} __v{v + 1} = {mapped}; ");
                                v++;
                                type = resultType;
                            }
                            continue;
                        }
                        case "Reverse" when args.Count == 0:
                            BufferSegment(true, null, null);
                            continue;
                        case "Concat" when args.Count == 1 && !pairMode:
                        {
                            ITypeSymbol other = TypeOf(args[0]);
                            ExpressionSyntax otherArg = args[0];
                            ITypeSymbol elementType = type;
                            int id = stage++;
                            if (other is IArrayTypeSymbol otherArray && otherArray.Rank == 1)
                                BufferSegment(false, null, buffer =>
                                {
                                    string o = Extra(TypeText(other), $"__cat{id}", otherArg);
                                    code.Append($"for (int __j = 0; __j < {o}.Length; __j++) {buffer}.Add({Wrap($"{o}[__j]", elementType)}); ");
                                });
                            else if (IsList(other) || IsEnumerable(other) && IsDataListBacked(otherArg))
                                BufferSegment(false, null, buffer =>
                                {
                                    string o = Extra(DataListType, $"__cat{id}", otherArg);
                                    code.Append($"for (int __j = 0; __j < {o}.Count; __j++) {buffer}.Add({o}[__j]); ");
                                });
                            else
                            {
                                Error(args[0], Loc.T("'Concat' : つなぐ相手は、List・配列・LINQ の結果にしてください", "'Concat' : The sequence to append must be a List, an array or a LINQ result"));
                                return node;
                            }
                            continue;
                        }
                        case "Append" when args.Count == 1 && !pairMode:
                        case "Prepend" when args.Count == 1 && !pairMode:
                        {
                            string item = Extra(TypeText(type), $"__item{stage++}", args[0]);
                            string add = $".Add({Wrap(item, type)}); ";
                            if (call.Name == "Append")
                                BufferSegment(false, null, buffer => code.Append(buffer).Append(add));
                            else
                                BufferSegment(false, buffer => preamble.Append(buffer).Append(add), null);
                            continue;
                        }
                        case "Distinct" when args.Count == 0 && pairMode:
                            continue;   // KeyValuePair はキーが重ならないので、何もしない（#61 決めたこと 6）
                        case "Distinct" when args.Count == 0:
                        {
                            // 最初に出たものを残す（C# と同じ順）。値・文字列は DataDictionary、それ以外（参照）は DataList で調べる
                            string token = $"__d{stage}";
                            body.Append($"{DataTokenType} {token} = {Wrap(ValueExpr(), type)}; ");
                            if (Accessor(type) is string accessor && accessor != "DataList" && accessor != "DataDictionary" || type.TypeKind == TypeKind.Enum)
                            {
                                string seen = Temp($"seen{stage}", DataDictionaryType);
                                body.Append($"if ({seen}.ContainsKey({token})) continue; {seen}.SetValue({token}, {token}); ");
                            }
                            else
                            {
                                string seen = Temp($"seen{stage}", DataListType);
                                body.Append($"if ({seen}.Contains({token})) continue; {seen}.Add({token}); ");
                            }
                            stage++;
                            continue;
                        }
                        case "GroupBy" when args.Count == 1 || args.Count == 2 && IsElementSelector(args[1]):
                            if (!Group(call))
                                return node;
                            continue;
                        case "OrderBy":
                        case "OrderByDescending":
                        {
                            var keys = new List<(LinqCallInfo, bool)> { (call, call.Name == "OrderByDescending") };
                            while (index + 1 < calls.Count && (calls[index + 1].Name == "ThenBy" || calls[index + 1].Name == "ThenByDescending"))
                            {
                                index++;
                                keys.Add((calls[index], calls[index].Name == "ThenByDescending"));
                            }
                            last = index == calls.Count - 1;
                            if (!Order(keys))
                                return node;
                            if (last)
                                break;   // 並べ替えで終わる：結果のシーケンス
                            continue;
                        }
                    }

                    if (call.Name == "OrderBy" || call.Name == "OrderByDescending")
                        break;
                    if (!last || !LinqTerminals.Contains(call.Name))
                    {
                        Error(call.Node, Loc.T($"'{call.Name}' : この LINQ のメソッド（この引数の形）は未対応です（Where / Select / SelectMany / OrderBy / ThenBy / GroupBy / Distinct / Skip / Take / SkipWhile / TakeWhile / Reverse / Concat / Append / Prepend と、{string.Join(" / ", LinqTerminals)} が使えます）", $"'{call.Name}' : This LINQ method (with these arguments) is not supported (Where / Select / SelectMany / OrderBy / ThenBy / GroupBy / Distinct / Skip / Take / SkipWhile / TakeWhile / Reverse / Concat / Append / Prepend and {string.Join(" / ", LinqTerminals)} are supported)"));
                        return node;
                    }

                    // 最後の呼び出し：条件・変換のラムダを受け取るものは、Where / Select の段にしてから集計する
                    bool takesPredicate = new[] { "Any", "Count", "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault" }.Contains(call.Name);
                    bool takesSelector = new[] { "Sum", "Min", "Max", "Average" }.Contains(call.Name);
                    if (args.Count == 1 && takesPredicate)
                    {
                        if (!Filter(call, args[0]))
                            return node;
                    }
                    else if (args.Count == 1 && takesSelector)
                    {
                        if (!Map(call, args[0]))
                            return node;
                    }
                    else if (call.Name == "All" && args.Count == 1)
                    {
                        string ok = Apply(call, args[0], out _);
                        if (ok == null)
                            return node;
                        body.Append($"if (!{ok}) return false; ");
                    }
                    else if (call.Name == "Contains" && args.Count == 1)
                    {
                        if (pairMode)
                        {
                            Error(call.Node, Loc.T($"'{call.Name}' : Dictionary の要素（KeyValuePair）の Contains は未対応です（ContainsKey などを使ってください）", $"'{call.Name}' : Contains on Dictionary elements (KeyValuePair) is not supported (use ContainsKey and so on)"));
                            return node;
                        }
                        Extra(TypeText(type), "__value", args[0]);
                    }
                    else if ((call.Name == "ElementAt" || call.Name == "ElementAtOrDefault") && args.Count == 1)
                    {
                        Extra("int", "__index", args[0]);
                    }
                    else if (call.Name == "Aggregate" && (args.Count == 1 || args.Count == 2))
                    {
                        // Aggregate(f)：最初の要素から始める。Aggregate(seed, f)：seed から始める
                        string elementText = TypeText(type), elementValue = ValueExpr();
                        string accText = elementText;
                        if (args.Count == 1)
                        {
                            preamble.Append($"{accText} __acc = default({accText}); bool __found = false; ");
                            body.Append($"if (!__found) {{ __acc = {elementValue}; __found = true; continue; }} ");
                        }
                        else
                        {
                            accText = TypeText(call.Method.TypeArguments[1]);
                            string seed = Extra(accText, "__seed", args[0]);
                            preamble.Append($"{accText} __acc = {seed}; ");
                        }
                        string next = ApplyWith(call, args[args.Count - 1], out _, -1, null, (accText, "__acc"), (elementText, elementValue));
                        if (next == null)
                            return node;
                        body.Append($"__acc = {next}; ");
                        aggregateType = accText;
                    }
                    else if (call.Name == "ToDictionary" && (args.Count == 1 || args.Count == 2))
                    {
                        string key = Apply(call, args[0], out ITypeSymbol keyType);
                        if (key == null)
                            return node;
                        string value = ValueExpr();
                        ITypeSymbol valueType = type;
                        if (args.Count == 2)
                        {
                            value = Apply(call, args[1], out valueType);
                            if (value == null)
                                return node;
                        }
                        preamble.Append($"{DataDictionaryType} __dict = new {DataDictionaryType}(); ");
                        body.Append($"{DataTokenType} __key = {Wrap(key, keyType)}; ")
                            .Append(Loc.T($"if (__dict.ContainsKey(__key)) {{ {Halt("ToDictionary", "同じキーがあります（C# では ArgumentException）")}}} ", $"if (__dict.ContainsKey(__key)) {{ {Halt("ToDictionary", "the same key appears more than once (C#: ArgumentException)")}}} "))
                            .Append($"__dict.SetValue(__key, {Wrap(value, valueType)}); ");
                    }
                    else if (args.Count != 0)
                    {
                        Error(call.Node, Loc.T($"'{call.Name}' : この引数の形は未対応です", $"'{call.Name}' : This form of arguments is not supported"));
                        return node;
                    }
                }

                string currentValue = ValueExpr();
                string valueText = TypeText(type);
                LinqCallInfo terminal = calls.Count == 0 ? null : calls[calls.Count - 1];
                string each;
                switch (terminal == null || LinqSequences.Contains(terminal.Name) ? "ToList" : terminal.Name)
                {
                    case "ToList":
                        // 結果のシーケンス：新しい DataList に入れる（決めたこと 5）
                        returnType = DataListType;
                        preamble.Append($"{DataListType} __result = new {DataListType}(); ");
                        each = $"__result.Add({Wrap(currentValue, type)}); ";
                        post = "return __result;";
                        break;
                    case "ElementAt":
                    case "ElementAtOrDefault":
                        returnType = valueText;
                        preamble.Append("int __pos = 0; ");
                        each = $"if (__pos == __index) return {currentValue}; __pos++; ";
                        post = (terminal.Name == "ElementAt" ? Halt("ElementAt", Loc.T("添字が範囲の外です（C# では ArgumentOutOfRangeException）", "the index is out of range (C#: ArgumentOutOfRangeException)")) : "") + $"return default({valueText});";
                        break;
                    case "Single":
                    case "SingleOrDefault":
                        returnType = valueText;
                        preamble.Append($"{valueText} __single = default({valueText}); bool __found = false; ");
                        each = Loc.T($"if (__found) {{ {Halt(terminal.Name, "要素が 2 つ以上あります（C# では InvalidOperationException）")}}} __single = {currentValue}; __found = true; ", $"if (__found) {{ {Halt(terminal.Name, "there are two or more elements (C#: InvalidOperationException)")}}} __single = {currentValue}; __found = true; ");
                        post = "if (__found) return __single; " + (terminal.Name == "Single" ? Halt("Single", NoElement) : "") + $"return default({valueText});";
                        break;
                    case "Aggregate":
                        returnType = aggregateType;
                        each = "";
                        post = (terminal.Args.Count == 1 ? $"if (!__found) {{ {Halt("Aggregate", NoElement)}}} " : "") + "return __acc;";
                        break;
                    case "ToArray":
                        returnType = valueText + "[]";
                        preamble.Append($"{DataListType} __result = new {DataListType}(); ");
                        each = $"__result.Add({Wrap(currentValue, type)}); ";
                        post = $"return {ToArrayHelper(type)}(__result);";
                        break;
                    case "ToDictionary":
                        returnType = DataDictionaryType;
                        each = "";
                        post = "return __dict;";
                        break;
                    case "Any":
                        returnType = "bool";
                        each = "return true; ";
                        post = "return false;";
                        break;
                    case "All":
                        returnType = "bool";
                        each = "";
                        post = "return true;";
                        break;
                    case "Count":
                        returnType = "int";
                        preamble.Append("int __count = 0; ");
                        each = "__count++; ";
                        post = "return __count;";
                        break;
                    case "Contains":
                        returnType = "bool";
                        each = $"if ({currentValue} == __value) return true; ";
                        post = "return false;";
                        break;
                    case "First":
                    case "FirstOrDefault":
                        returnType = valueText;
                        each = $"return {currentValue}; ";
                        post = (terminal.Name == "First" ? Halt("First", NoElement) : "") + $"return default({valueText});";
                        break;
                    case "Last":
                    case "LastOrDefault":
                        returnType = valueText;
                        preamble.Append($"{valueText} __last = default({valueText}); bool __found = false; ");
                        each = $"__last = {currentValue}; __found = true; ";
                        post = "if (__found) return __last; " + (terminal.Name == "Last" ? Halt("Last", NoElement) : "") + $"return default({valueText});";
                        break;
                    case "Sum":
                    case "Min":
                    case "Max":
                    case "Average":
                    {
                        if (!IsNumeric(type))
                        {
                            Error(terminal.Node, Loc.T($"'{terminal.Name}' : 数値（int / long / float / double など）以外には未対応です", $"'{terminal.Name}' : Only numbers (int / long / float / double and so on) are supported"));
                            return node;
                        }
                        returnType = TypeText(terminal.Method.ReturnType);
                        if (terminal.Name == "Sum")
                        {
                            preamble.Append($"{returnType} __acc = default({returnType}); ");
                            each = $"__acc += {currentValue}; ";
                            post = "return __acc;";
                        }
                        else if (terminal.Name == "Average")
                        {
                            preamble.Append("double __sum = 0; int __count = 0; ");
                            each = $"__sum += {currentValue}; __count++; ";
                            post = $"if (__count == 0) {{ {Halt("Average", NoElement)}}} return ({returnType})(__sum / __count);";
                        }
                        else
                        {
                            string op = terminal.Name == "Min" ? "<" : ">";
                            preamble.Append($"{valueText} __m = default({valueText}); bool __found = false; ");
                            each = $"if (!__found || {currentValue} {op} __m) __m = {currentValue}; __found = true; ";
                            post = $"if (!__found) {{ {Halt(terminal.Name, NoElement)}}} return ({returnType})__m;";
                        }
                        break;
                    }
                    default:
                        return node;
                }
                CloseLoop(each);

                if (isArray)
                    sourceParam = TypeText(sourceType);
                _helpers[name] = $"{modifiers} {returnType} {name}({sourceParam} __src{extraParams}{CaptureParams(captures)}) "
                                 + $"{{ {marker}{preamble}{code}{post} }}";
                return Parse($"{name}({sourceText ?? Text(Visit(source))}{extraArgs}{CaptureArgs(captures)})", node);
            }

            /// <summary>GroupBy の 2 つ目の引数が、要素を変換するラムダ（引数 1 つ）か（結果を作るラムダ・比較子の形は未対応。#140）</summary>
            private bool IsElementSelector(ExpressionSyntax fn) =>
                _model.GetTypeInfo(fn).ConvertedType is INamedTypeSymbol d && d.DelegateInvokeMethod != null && d.DelegateInvokeMethod.Parameters.Length == 1;

            private static string NoElement => Loc.T("要素がありません（C# では InvalidOperationException）", "there are no elements (C#: InvalidOperationException)");

            /// <summary>
            /// 匿名型・タプルの段（#138・#82）が Distinct() の代わりに置いた印：Where(x => __cls___Anon_0____Eq…(x, x))（同じ引数を 2 つ渡す、生成したクラスの値の比較）。
            /// 印なら、比較のメソッドの名前。そうでなければ null
            /// </summary>
            private static string DistinctEquality(ExpressionSyntax fn)
            {
                if (!(fn is SimpleLambdaExpressionSyntax lambda) || !(lambda.Body is InvocationExpressionSyntax call) || call.ArgumentList.Arguments.Count != 2)
                    return null;
                string parameter = lambda.Parameter.Identifier.ValueText;
                if (!parameter.StartsWith("__de", System.StringComparison.Ordinal)
                    || call.ArgumentList.Arguments.Any(a => !(a.Expression is IdentifierNameSyntax id) || id.Identifier.ValueText != parameter))
                    return null;
                string name = call.Expression.ToString();
                return (name.Contains(AnonymousTypeLowering.GeneratedPrefix) || name.Contains(TupleLowering.GeneratedPrefix)) && name.Contains("__Eq") ? name : null;
            }

            /// <summary>C# なら例外になるところ：エラーのログを出して、Udon の実行時例外にする</summary>
            private static string Halt(string op, string message) =>
                $"{{ global::UnityEngine.Debug.LogError(\"[UdonSharpPlus] {op} : {message}\"); {DataListType} __halt = null; __halt.Clear(); }} ";   // 自分のブロックに入れる（同じメソッドに何回出ても、名前がぶつからない）

            /// <summary>並べ替えのキーの比較（負・0・正）。未対応の型なら null</summary>
            private static string CompareText(string a, string b, ITypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol e)
                {
                    string u = e.EnumUnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return $"(({u}){a} < ({u}){b} ? -1 : ({u}){a} > ({u}){b} ? 1 : 0)";
                }
                switch (type.SpecialType)
                {
                    case SpecialType.System_String:
                        return $"string.Compare({a}, {b})";   // C# の既定の比較（Comparer<string>.Default）と同じく、カルチャに従う
                    case SpecialType.System_Boolean:
                        return $"({a} == {b} ? 0 : {a} ? 1 : -1)";
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
                        return $"({a} < {b} ? -1 : {a} > {b} ? 1 : 0)";
                    default:
                        return null;
                }
            }

            private static bool IsNumeric(ITypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Int32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                        return true;
                    default:
                        return false;
                }
            }

            private static IEnumerable<string> LambdaParameterNames(LambdaExpressionSyntax lambda)
            {
                switch (lambda)
                {
                    case SimpleLambdaExpressionSyntax s: return new[] { s.Parameter.Identifier.ValueText };
                    case ParenthesizedLambdaExpressionSyntax p: return p.ParameterList.Parameters.Select(x => x.Identifier.ValueText);
                    default: return Enumerable.Empty<string>();
                }
            }

            /// <summary>ラムダの外の変数の名前（エラーは出さない。名前がぶつかるかの判定用）</summary>
            private IEnumerable<string> CaptureNames(LambdaExpressionSyntax lambda)
            {
                foreach (IdentifierNameSyntax id in lambda.Body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    ISymbol symbol = _model.SymbolInfoOf(id).Symbol;
                    if (!(symbol is ILocalSymbol || symbol is IParameterSymbol p && !p.IsThis))
                        continue;
                    if (symbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(r.Span)))
                        continue;
                    yield return symbol.Name;
                }
            }
        }
    }
}
