using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CustomUdonSharp
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
                { "ToList", "ToArray", "ToDictionary", "Any", "All", "Count", "First", "FirstOrDefault", "Last", "LastOrDefault", "Sum", "Min", "Max", "Average", "Contains" };

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
                if (!(expression is InvocationExpressionSyntax node) || !(_model.GetSymbolInfo(node).Symbol is IMethodSymbol method) || !IsLinqMethod(method))
                    return null;
                var args = node.ArgumentList.Arguments.Select(a => a.Expression).ToList();
                if (method.ReducedFrom != null)
                    return node.Expression is MemberAccessExpressionSyntax access
                        ? new LinqCallInfo { Node = node, Method = method, Receiver = access.Expression, Args = args }
                        : null;
                // static の形：Enumerable.Where(list, f)
                if (args.Count == 0)
                    return null;
                return new LinqCallInfo { Node = node, Method = method, Receiver = args[0], Args = args.Skip(1).ToList() };
            }

            /// <summary>IEnumerable&lt;T&gt; の宣言は未対応（決めたこと 6。var で受ければ DataList になる）</summary>
            private bool IEnumerableDeclaration(GenericNameSyntax node)
            {
                if (_helpers == null || !(_model.GetSymbolInfo(node).Symbol is INamedTypeSymbol type) || !IsEnumerable(type))
                    return false;
                Error(node, "IEnumerable<T> / IOrderedEnumerable<T> 型の宣言は未対応です（LINQ の結果は var で受けるか、ToList() / ToArray() にしてください）");
                return true;
            }

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
                }
                if (calls.Count == 0)
                {
                    Error(node, "この形の LINQ の呼び出しは未対応です");
                    return node;
                }
                ExpressionSyntax source = current;

                // 元：List・配列・LINQ の結果（var で受けたもの。中身は DataList）
                ITypeSymbol sourceType = TypeOf(source);
                ITypeSymbol element;
                bool isArray = false;
                if (IsList(sourceType) || IsEnumerable(sourceType))
                    element = ((INamedTypeSymbol)sourceType).TypeArguments[0];
                else if (sourceType is IArrayTypeSymbol array && array.Rank == 1)
                {
                    element = array.ElementType;
                    isArray = true;
                }
                else
                {
                    Error(node, $"'{calls[0].Name}' : LINQ は List と配列にだけ使えます（{sourceType?.ToDisplayString() ?? "この型"} は未対応です）");
                    return node;
                }

                IMethodSymbol enclosing = EnclosingMethod(node);
                if (enclosing == null)
                {
                    Error(node, "LINQ は、フィールドの初期化子の中では未対応です（メソッドの中で呼んでください）");
                    return node;
                }

                string terminalName = calls[calls.Count - 1].Name;
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
                ITypeSymbol type = element;              // 今の段の要素の型
                int v = 0;                               // 今の段の値の変数 __v{v}
                int stage = 0;
                int segment = 0;

                // 元を回すループ
                header = isArray
                    ? $"int __n0 = __src.Length; for (int __i0 = 0; __i0 < __n0{{STOP}}; __i0++) {{ {TypeText(element)} __v0 = __src[__i0]; "
                    : $"int __n0 = __src.Count; for (int __i0 = 0; __i0 < __n0{{STOP}}; __i0++) {{ {TypeText(element)} __v0 = {Unwrap("__src[__i0]", element)}; ";

                void CloseLoop(string tail)
                {
                    code.Append(header.Replace("{STOP}", string.Concat(stops.Select(s => $" && !{s}"))))
                        .Append(body).Append(tail).Append("} ");
                    body.Clear();
                    stops.Clear();
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
                PreparedLambda Lambda(LinqCallInfo call, ExpressionSyntax fn, out ITypeSymbol resultType)
                {
                    resultType = null;
                    if (!(_model.GetTypeInfo(fn).ConvertedType is INamedTypeSymbol delegateType) || delegateType.DelegateInvokeMethod == null
                        || delegateType.DelegateInvokeMethod.Parameters.Length != 1)
                    {
                        Error(fn, $"'{call.Name}' : この形のラムダは未対応です（引数 1 つのラムダかメソッド名を渡してください。添字付きの (x, i) => ... は未対応です）");
                        return null;
                    }
                    resultType = delegateType.DelegateInvokeMethod.ReturnType;
                    bool conflict = fn is LambdaExpressionSyntax l && LambdaParameterNames(l).Any(captureNames.Contains);
                    PreparedLambda prepared = PrepareLambda(fn, delegateType.DelegateInvokeMethod, $"{name}_body{stage}", modifiers, conflict);
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
                string Apply(LinqCallInfo call, ExpressionSyntax fn, out ITypeSymbol resultType)
                {
                    PreparedLambda lambda = Lambda(call, fn, out resultType);
                    if (lambda == null)
                        return null;
                    string result = $"__r{stage++}";
                    body.Append($"{TypeText(resultType)} {result}; {{ {TypeText(type)} {lambda.Names[0]} = __v{v}; {result} = ({lambda.Each}); }} ");
                    return result;
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
                            Error(call.Node, $"'{call.Name}' : 比較子を渡す形は未対応です（キーを返すラムダだけ渡してください）");
                            return false;
                        }
                        string key = Apply(call, call.Args[0], out ITypeSymbol keyType);
                        if (key == null)
                            return false;
                        if (CompareText("a", "b", keyType) == null)
                        {
                            Error(call.Args[0], $"'{call.Name}' : この型のキー（{keyType.ToDisplayString()}）での並べ替えは未対応です（数値・string・bool・char・enum が使えます）");
                            return false;
                        }
                        string list = Temp($"k{s}_{keyLists.Count}", DataListType);
                        body.Append($"{list}.Add({Wrap(key, keyType)}); ");
                        keyLists.Add((list, keyType, descending));
                    }
                    body.Append($"{buffer}.Add({Wrap($"__v{v}", type)}); ");
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
                    header = $"for (int __i{next} = 0; __i{next} < __m{s}{{STOP}}; __i{next}++) {{ {TypeText(type)} __v{v} = {Unwrap($"{buffer}[__p{s}[__i{next}].Int]", type)}; ";
                    return true;
                }

                for (int index = 0; index < calls.Count; index++)
                {
                    LinqCallInfo call = calls[index];
                    bool last = index == calls.Count - 1;
                    var args = call.Args;

                    switch (call.Name)
                    {
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
                        case "Distinct" when args.Count == 0:
                        {
                            // 最初に出たものを残す（C# と同じ順）。値・文字列は DataDictionary、それ以外（参照）は DataList で調べる
                            string token = $"__d{stage}";
                            body.Append($"{DataTokenType} {token} = {Wrap($"__v{v}", type)}; ");
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
                        Error(call.Node, $"'{call.Name}' : この LINQ のメソッド（この引数の形）は未対応です（Where / Select / OrderBy / ThenBy / Distinct / Skip / Take と、{string.Join(" / ", LinqTerminals)} が使えます）");
                        return node;
                    }

                    // 最後の呼び出し：条件・変換のラムダを受け取るものは、Where / Select の段にしてから集計する
                    bool takesPredicate = new[] { "Any", "Count", "First", "FirstOrDefault", "Last", "LastOrDefault" }.Contains(call.Name);
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
                        Extra(TypeText(type), "__value", args[0]);
                    }
                    else if (call.Name == "ToDictionary" && (args.Count == 1 || args.Count == 2))
                    {
                        string key = Apply(call, args[0], out ITypeSymbol keyType);
                        if (key == null)
                            return node;
                        string value = $"__v{v}";
                        ITypeSymbol valueType = type;
                        if (args.Count == 2)
                        {
                            value = Apply(call, args[1], out valueType);
                            if (value == null)
                                return node;
                        }
                        preamble.Append($"{DataDictionaryType} __dict = new {DataDictionaryType}(); ");
                        body.Append($"{DataTokenType} __key = {Wrap(key, keyType)}; ")
                            .Append($"if (__dict.ContainsKey(__key)) {{ {Halt("ToDictionary", "同じキーがあります（C# では ArgumentException）")}}} ")
                            .Append($"__dict.SetValue(__key, {Wrap(value, valueType)}); ");
                    }
                    else if (args.Count != 0)
                    {
                        Error(call.Node, $"'{call.Name}' : この引数の形は未対応です");
                        return node;
                    }
                }

                string currentValue = $"__v{v}";
                string valueText = TypeText(type);
                LinqCallInfo terminal = calls[calls.Count - 1];
                string each;
                switch (terminal.Name)
                {
                    case "Where":
                    case "Select":
                    case "ToList":
                    case "Skip":
                    case "Take":
                    case "Distinct":
                    case "OrderBy":
                    case "OrderByDescending":
                    case "ThenBy":
                    case "ThenByDescending":
                        // 結果のシーケンス：新しい DataList に入れる（決めたこと 5）
                        returnType = DataListType;
                        preamble.Append($"{DataListType} __result = new {DataListType}(); ");
                        each = $"__result.Add({Wrap(currentValue, type)}); ";
                        post = "return __result;";
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
                            Error(terminal.Node, $"'{terminal.Name}' : 数値（int / long / float / double など）以外には未対応です");
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

                string sourceParam = isArray ? TypeText(sourceType) : DataListType;
                _helpers[name] = $"{modifiers} {returnType} {name}({sourceParam} __src{extraParams}{CaptureParams(captures)}) "
                                 + $"{{ {marker}{preamble}{code}{post} }}";
                return Parse($"{name}({Text(Visit(source))}{extraArgs}{CaptureArgs(captures)})", node);
            }

            private const string NoElement = "要素がありません（C# では InvalidOperationException）";

            /// <summary>C# なら例外になるところ：エラーのログを出して、Udon の実行時例外にする</summary>
            private static string Halt(string op, string message) =>
                $"global::UnityEngine.Debug.LogError(\"[CustomUdonSharp] {op} : {message}\"); {DataListType} __halt = null; __halt.Clear(); ";

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
                    ISymbol symbol = _model.GetSymbolInfo(id).Symbol;
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
