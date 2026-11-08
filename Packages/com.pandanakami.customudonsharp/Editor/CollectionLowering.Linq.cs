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
        /// - Where / Select は要素を 1 つずつ次の段に渡すので、途中のリストを作らない。First / Any などは見つかった時点で止まる
        /// - 結果がシーケンスのまま（変数に入れる、ToList()）なら、新しい DataList を作って返す。ToArray() は配列
        /// - 元は List・配列・LINQ の結果（DataList）。Dictionary は後で（決めたこと 4）
        /// - 汎用クラスのコピーの中では、拡張メソッドの呼び出しは static の形（Enumerable.Where(list, f)）になっているので、両方の形を受け付ける
        /// </summary>
        private sealed partial class Rewriter
        {
            private const string EnumerableName = "System.Linq.Enumerable";
            private const string IEnumerableName = "System.Collections.Generic.IEnumerable<T>";

            private static bool IsEnumerable(ITypeSymbol type) => type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == IEnumerableName;

            private static bool IsLinqMethod(IMethodSymbol method) =>
                method != null && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == EnumerableName;

            private static readonly string[] LinqTerminals =
                { "ToList", "ToArray", "Any", "All", "Count", "First", "FirstOrDefault", "Last", "LastOrDefault", "Sum", "Min", "Max", "Average", "Contains" };

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
                Error(node, "IEnumerable<T> 型の宣言は未対応です（LINQ の結果は var で受けるか、ToList() / ToArray() にしてください）");
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
                string modifiers = enclosing.IsStatic ? "private static" : "private";
                string marker = SourceMap.MarkerFor(node);

                // ラムダの引数の名前が、別のラムダの外の変数と同じなら（C# 8 以降は書ける）、そのラムダは本体のメソッドにする
                // （ループの中のブロックで宣言すると、生成メソッドの引数と名前がぶつかるため）
                var lambdaArgs = calls.SelectMany(c => c.Args).Where(a => a is LambdaExpressionSyntax).ToList();
                var captureNames = new HashSet<string>(lambdaArgs.SelectMany(a => CaptureNames((LambdaExpressionSyntax)a)));

                var captures = new List<Capture>();
                var perElement = new StringBuilder();   // 要素ごとの処理（__v0 から順に）
                string pre = "", post;
                string extraParams = "", extraArgs = "";
                string returnType;
                ITypeSymbol type = element;              // 今の段の要素の型
                int v = 0;                               // 今の段の値の変数 __v{v}
                int stage = 0;

                // ラムダを 1 つ準備して、外の変数をまとめる
                PreparedLambda Lambda(LinqCallInfo call, ExpressionSyntax fn)
                {
                    if (!(_model.GetTypeInfo(fn).ConvertedType is INamedTypeSymbol delegateType) || delegateType.DelegateInvokeMethod == null
                        || delegateType.DelegateInvokeMethod.Parameters.Length != 1)
                    {
                        Error(fn, $"'{call.Name}' : この形のラムダは未対応です（引数 1 つのラムダかメソッド名を渡してください。添字付きの (x, i) => ... は未対応です）");
                        return null;
                    }
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

                // 1 つのラムダを今の値に当てはめて、結果を変数に入れる文（ラムダの引数は、兄弟のブロックの中だけで宣言する）
                string Apply(PreparedLambda lambda, string resultType, string result) =>
                    $"{resultType} {result}; {{ {TypeText(type)} {lambda.Names[0]} = __v{v}; {result} = ({lambda.Each}); }} ";

                // Where：条件に合わなければ次の要素へ
                bool Filter(LinqCallInfo call, ExpressionSyntax fn)
                {
                    PreparedLambda lambda = Lambda(call, fn);
                    if (lambda == null)
                        return false;
                    perElement.Append(Apply(lambda, "bool", $"__ok{stage}")).Append($"if (!__ok{stage}) continue; ");
                    stage++;
                    return true;
                }

                // Select：値を変換する
                bool Map(LinqCallInfo call, ExpressionSyntax fn)
                {
                    PreparedLambda lambda = Lambda(call, fn);
                    if (lambda == null)
                        return false;
                    ITypeSymbol resultType = ((INamedTypeSymbol)_model.GetTypeInfo(fn).ConvertedType).DelegateInvokeMethod.ReturnType;
                    perElement.Append(Apply(lambda, TypeText(resultType), $"__v{v + 1}"));
                    v++;
                    type = resultType;
                    stage++;
                    return true;
                }

                for (int index = 0; index < calls.Count; index++)
                {
                    LinqCallInfo call = calls[index];
                    bool last = index == calls.Count - 1;
                    var args = call.Args;

                    if (call.Name == "Where" && args.Count == 1)
                    {
                        if (!Filter(call, args[0]))
                            return node;
                        continue;
                    }
                    if (call.Name == "Select" && args.Count == 1)
                    {
                        if (!Map(call, args[0]))
                            return node;
                        continue;
                    }
                    if ((call.Name == "ToList" || call.Name == "ToArray") && args.Count == 0 && !last)
                        continue;   // 途中の ToList / ToArray は、結果が外に見えないので何もしない

                    if (!last || !LinqTerminals.Contains(call.Name))
                    {
                        Error(call.Node, $"'{call.Name}' : この LINQ のメソッド（この引数の形）は未対応です（Where / Select と、{string.Join(" / ", LinqTerminals)} が使えます）");
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
                        PreparedLambda lambda = Lambda(call, args[0]);
                        if (lambda == null)
                            return node;
                        perElement.Append(Apply(lambda, "bool", $"__ok{stage}")).Append($"if (!__ok{stage}) return false; ");
                        stage++;
                    }
                    else if (call.Name == "Contains" && args.Count == 1)
                    {
                        extraParams = $", {TypeText(type)} __value";
                        extraArgs = ", " + Text(Visit(args[0]));
                    }
                    else if (args.Count != 0)
                    {
                        Error(call.Node, $"'{call.Name}' : この引数の形は未対応です");
                        return node;
                    }
                }

                string value = $"__v{v}";
                string valueType = TypeText(type);
                string halt(string op) =>
                    $"global::UnityEngine.Debug.LogError(\"[CustomUdonSharp] {op} : 要素がありません（C# では InvalidOperationException）\"); {DataListType} __halt = null; __halt.Clear(); ";

                LinqCallInfo terminal = calls[calls.Count - 1];
                string each;
                switch (terminal.Name)
                {
                    case "Where":
                    case "Select":
                    case "ToList":
                        // 結果のシーケンス：新しい DataList に入れる（決めたこと 5）
                        returnType = DataListType;
                        pre = $"{DataListType} __r = new {DataListType}(); ";
                        each = $"__r.Add({Wrap(value, type)}); ";
                        post = "return __r;";
                        break;
                    case "ToArray":
                        returnType = valueType + "[]";
                        pre = $"{DataListType} __r = new {DataListType}(); ";
                        each = $"__r.Add({Wrap(value, type)}); ";
                        post = $"return {ToArrayHelper(type)}(__r);";
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
                        pre = "int __c = 0; ";
                        each = "__c++; ";
                        post = "return __c;";
                        break;
                    case "Contains":
                        returnType = "bool";
                        each = $"if ({value} == __value) return true; ";
                        post = "return false;";
                        break;
                    case "First":
                    case "FirstOrDefault":
                        returnType = valueType;
                        each = $"return {value}; ";
                        post = (terminal.Name == "First" ? halt("First") : "") + $"return default({valueType});";
                        break;
                    case "Last":
                    case "LastOrDefault":
                        returnType = valueType;
                        pre = $"{valueType} __last = default({valueType}); bool __found = false; ";
                        each = $"__last = {value}; __found = true; ";
                        post = "if (__found) return __last; " + (terminal.Name == "Last" ? halt("Last") : "") + $"return default({valueType});";
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
                            pre = $"{returnType} __acc = default({returnType}); ";
                            each = $"__acc += {value}; ";
                            post = "return __acc;";
                        }
                        else if (terminal.Name == "Average")
                        {
                            pre = "double __sum = 0; int __c = 0; ";
                            each = $"__sum += {value}; __c++; ";
                            post = $"if (__c == 0) {{ {halt("Average")}}} return ({returnType})(__sum / __c);";
                        }
                        else
                        {
                            string op = terminal.Name == "Min" ? "<" : ">";
                            pre = $"{valueType} __m = default({valueType}); bool __found = false; ";
                            each = $"if (!__found || {value} {op} __m) __m = {value}; __found = true; ";
                            post = $"if (!__found) {{ {halt(terminal.Name)}}} return ({returnType})__m;";
                        }
                        break;
                    }
                    default:
                        return node;
                }

                // 元を回すループ
                string loop = isArray
                    ? $"int __n = __src.Length; for (int __i = 0; __i < __n; __i++) {{ {TypeText(element)} __v0 = __src[__i]; "
                    : $"int __n = __src.Count; for (int __i = 0; __i < __n; __i++) {{ {TypeText(element)} __v0 = {Unwrap("__src[__i]", element)}; ";
                string sourceParam = isArray ? TypeText(sourceType) : DataListType;

                _helpers[name] = $"{modifiers} {returnType} {name}({sourceParam} __src{extraParams}{CaptureParams(captures)}) "
                                 + $"{{ {marker}{pre}{loop}{perElement}{each}}} {post} }}";
                return Parse($"{name}({Text(Visit(source))}{extraArgs}{CaptureArgs(captures)})", node);
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
