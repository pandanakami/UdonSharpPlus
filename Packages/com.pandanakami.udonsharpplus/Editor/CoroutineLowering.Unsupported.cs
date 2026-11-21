using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UdonSharpPlus
{
    internal static partial class CoroutineLowering
    {
        /// <summary>
        /// コルーチンの対応しない書き方を、ほかの段が書き換える前の形で見つけてエラーにする（#107 の決めたこと 3・5、追加テスト案の 15）。
        /// ほかの段（ローカル関数・普通のクラス・インタフェース）が書き換えたあとでは元の形が分からず、エラーが出ないか、
        /// 生成したコードについての分かりにくいエラーになるため、下ごしらえの最初に調べる。原因のところに 1 つだけ出す
        /// - yield を書ける場所：UdonSharpBehaviour の、IEnumerator を返すインスタンスメソッドだけ
        /// - IEnumerator の値：コルーチン（ソースのメソッド）が返すものだけ。外部のメソッドが返すもの・MoveNext・Current・Reset は使えない
        /// - ほかのビヘイビアの、IEnumerator を返すメソッド（other.Foo()）は呼べない（番号は、そのビヘイビアの状態の表のもの）
        /// </summary>
        private static void CheckUnsupported(List<SyntaxTree> trees, InterfaceLowering.LoweringDiagnostics diagnostics, IEnumerable<MetadataReference> references)
        {
            if (!trees.Any(t => MayContain(t.GetRoot(), EnumeratorWords) && t.GetRoot().DescendantNodes().Any(n => n is YieldStatementSyntax
                    || (n is IdentifierNameSyntax id && (id.Identifier.ValueText == "IEnumerator" || UsesCoroutineApi(id))))))
                return;
            var compilation = Compilations.For(trees, references);
            var sourceClasses = trees.SelectMany(t => TypeDeclarations.Of<ClassDeclarationSyntax>(t.GetRoot())
                    .Select(c => compilation.Model(t).GetDeclaredSymbol(c) as INamedTypeSymbol))
                .Where(c => c != null).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
            // ソースの、IEnumerator を返すメソッドの名前（宣言だけから。ほかのビヘイビアのコルーチンの呼び出しを見つけるため）と、
            // 外部のもので IEnumerator を返すことの多い GetEnumerator
            var enumeratorMethods = new HashSet<string>(System.StringComparer.Ordinal) { "GetEnumerator" };
            foreach (INamedTypeSymbol t in TreeFilter.SourceTypes(compilation.Assembly.GlobalNamespace))
                foreach (ISymbol member in t.GetMembers())
                    if (member is IMethodSymbol method && method.ReturnType.SpecialType == SpecialType.System_Collections_IEnumerator)
                        enumeratorMethods.Add(method.Name);
            // IEnumerator の using の別名（どのファイルのものも）
            var enumeratorAliases = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (SyntaxTree tree in trees)
                foreach (UsingDirectiveSyntax u in tree.GetRoot().DescendantNodes(n => !(n is TypeDeclarationSyntax)).OfType<UsingDirectiveSyntax>())
                    if (u.Alias != null && u.Name.DescendantTokens().Any(tk => tk.ValueText == "IEnumerator"))
                        enumeratorAliases.Add(u.Alias.Name.Identifier.ValueText);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.Model(tree);
                SyntaxNode root = tree.GetRoot();
                var reported = new HashSet<SyntaxNode>();

                // yield を書いたところ（ラムダ・ローカル関数・メソッド・アクセサー）ごとに 1 つ
                foreach (YieldStatementSyntax y in root.DescendantNodes().OfType<YieldStatementSyntax>())
                {
                    SyntaxNode owner = y.Ancestors().FirstOrDefault(a => a is AnonymousFunctionExpressionSyntax || a is LocalFunctionStatementSyntax
                                                                        || a is BaseMethodDeclarationSyntax || a is AccessorDeclarationSyntax);
                    if (owner == null || !reported.Add(owner))
                        continue;
                    // UdonSharpBehaviour の中はそのまま、普通のクラスの中は、UdonSharpBehaviour から使われるときだけ（owner。#49 と同じ）。
                    // MonoBehaviour など、UdonSharp Plus が書き換えないクラスの中は調べない（普通の Unity のコルーチン・列挙のための yield）
                    if (!(owner.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is TypeDeclarationSyntax typeDecl)
                        || !(model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol containing))
                        continue;
                    string tag = null;
                    if (!InterfaceLowering.IsUdonSharpBehaviour(containing))
                    {
                        if (!ClassLowering.IsPlainClass(containing))
                            continue;
                        tag = ClassLowering.TagOf(containing);
                    }
                    string message = YieldPlaceError(model, owner);
                    if (message != null)
                        diagnostics.Error(PlaceLocation(owner), message, tag);
                }

                // インタフェースのメソッドの IEnumerator（インタフェースを通してコルーチンを開始する形）。
                // 実装する UdonSharpBehaviour・普通のクラス（使われるもの）があるときだけ
                foreach (InterfaceDeclarationSyntax itf in TypeDeclarations.Of<InterfaceDeclarationSyntax>(root))
                {
                    if (!(model.GetDeclaredSymbol(itf) is INamedTypeSymbol itfType))
                        continue;
                    var implementers = sourceClasses.Where(t => t.AllInterfaces.Contains(itfType, SymbolEqualityComparer.Default)).ToList();
                    bool byBehaviour = implementers.Any(InterfaceLowering.IsUdonSharpBehaviour);
                    var plainTags = implementers.Where(ClassLowering.IsPlainClass).Select(ClassLowering.TagOf).ToList();
                    if (!byBehaviour && plainTags.Count == 0)
                        continue;
                    foreach (MethodDeclarationSyntax m in itf.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (!(model.GetDeclaredSymbol(m) is IMethodSymbol s) || s.ReturnType.ToDisplayString() != EnumeratorName)
                            continue;
                        string message = Loc.T(
                            "インタフェースのメソッドのコルーチン（IEnumerator を返すメソッド）は未対応です。インタフェースには、コルーチンを開始する void のメソッドを書いてください",
                            "Coroutines in interface methods (methods returning IEnumerator) are not supported. Declare a void method that starts the coroutine instead");
                        if (byBehaviour)
                            diagnostics.Error(m.Identifier.GetLocation(), message);
                        else
                            foreach (string t in plainTags)
                                diagnostics.Error(m.Identifier.GetLocation(), message, t);
                    }
                }

                // IEnumerator の値の使い方（UdonSharpBehaviour の中）。
                // コルーチンに関わる名前を含まないクラスは、呼び出しの名前が、ソースの IEnumerator を返すメソッドか GetEnumerator のものだけを
                // 問い合わせる（すべての呼び出しの問い合わせに、1 秒ほどかかっていた。#203）
                foreach (ClassDeclarationSyntax cls in TypeDeclarations.Of<ClassDeclarationSyntax>(root))
                {
                    if (!(model.GetDeclaredSymbol(cls) is INamedTypeSymbol type) || !InterfaceLowering.IsUdonSharpBehaviour(type))
                        continue;
                    HashSet<string> only = TouchesEnumerators(cls, enumeratorAliases) ? null : enumeratorMethods;
                    foreach (SyntaxNode node in OwnNodes(cls))
                        CheckEnumeratorUse(model, node, diagnostics, only);
                }
            }
        }

        /// <summary>yield を書いたところが、コルーチンとして書けない場所ならエラーの文（書ける場所なら null）</summary>
        private static string YieldPlaceError(SemanticModel model, SyntaxNode owner)
        {
            switch (owner)
            {
                case AnonymousFunctionExpressionSyntax _:
                    return null;   // C# でもエラー（CS1621）
                case LocalFunctionStatementSyntax local:
                    // ローカル関数は、デリゲートの段がクラスのメソッドに移すので、そのメソッドのコルーチンになる（#174）
                    if (!(model.GetDeclaredSymbol(local) is IMethodSymbol fn))
                        return null;
                    if (fn.ReturnType.ToDisplayString() != EnumeratorName)
                        return Loc.T($"{fn.ReturnType.ToDisplayString()} を返す yield（列挙のための yield）は未対応です。コルーチンは、IEnumerator を返すローカル関数・メソッドに書いてください",
                            $"yield returning {fn.ReturnType.ToDisplayString()} (an iterator for enumeration) is not supported. Write coroutines as local functions or methods returning IEnumerator");
                    if (!InterfaceLowering.IsUdonSharpBehaviour(fn.ContainingType))
                        return Loc.T("UdonSharpBehaviour でないクラスのコルーチンは未対応です。UdonSharpBehaviour のメソッドに書いてください",
                            "Coroutines in classes that are not UdonSharpBehaviour are not supported. Write them in an UdonSharpBehaviour");
                    if (IsStaticLocal(fn))
                        return Loc.T("static のローカル関数・static メソッドの中のローカル関数のコルーチンは未対応です。static を外してください",
                            "Coroutines in static local functions, or in local functions inside static methods, are not supported. Remove static");
                    return null;
                case AccessorDeclarationSyntax _:
                    return Loc.T("プロパティ・インデクサーの中の yield は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                        "yield inside a property or indexer is not supported. Write coroutines as methods returning IEnumerator");
            }
            if (!(owner is MethodDeclarationSyntax m) || !(model.GetDeclaredSymbol(m) is IMethodSymbol symbol))
                return Loc.T("ここの yield は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                    "yield is not supported here. Write coroutines as methods returning IEnumerator");
            if (symbol.ReturnType.ToDisplayString() != EnumeratorName)
                return Loc.T($"{symbol.ReturnType.ToDisplayString()} を返す yield（列挙のための yield）は未対応です。コルーチンは、IEnumerator を返すメソッドに書いてください",
                    $"yield returning {symbol.ReturnType.ToDisplayString()} (an iterator for enumeration) is not supported. Write coroutines as methods returning IEnumerator");
            if (!InterfaceLowering.IsUdonSharpBehaviour(symbol.ContainingType))
                return Loc.T("UdonSharpBehaviour でないクラスのメソッドのコルーチンは未対応です。UdonSharpBehaviour のメソッドに書いてください",
                    "Coroutines in methods of classes that are not UdonSharpBehaviour are not supported. Write them in an UdonSharpBehaviour");
            if (symbol.IsStatic)
                return Loc.T("static メソッドのコルーチンは未対応です。static を外してください",
                    "Coroutines in static methods are not supported. Remove static");
            return null;
        }

        /// <summary>static のローカル関数か、static のメンバーの中のローカル関数か（移したメソッドが static になる）</summary>
        private static bool IsStaticLocal(IMethodSymbol fn)
        {
            ISymbol s = fn;
            while (s is IMethodSymbol m && (m.MethodKind == MethodKind.LocalFunction || m.MethodKind == MethodKind.AnonymousFunction))
            {
                if (m.IsStatic)
                    return true;
                s = m.ContainingSymbol;
            }
            return s?.IsStatic == true;
        }

        private static Location PlaceLocation(SyntaxNode owner)
        {
            switch (owner)
            {
                case LocalFunctionStatementSyntax f: return f.Identifier.GetLocation();
                case MethodDeclarationSyntax m: return m.Identifier.GetLocation();
                case AccessorDeclarationSyntax a: return a.Keyword.GetLocation();
                default: return owner.GetLocation();
            }
        }

        private static readonly string[] EnumeratorWords = { "yield", "IEnumerator", "StartCoroutine", "StopCoroutine", "StopAllCoroutines" };

        /// <summary>cls が、コルーチン・IEnumerator に関わる名前（yield・IEnumerator とその別名・StartCoroutine など）を含むか</summary>
        private static bool TouchesEnumerators(ClassDeclarationSyntax cls, HashSet<string> aliases)
        {
            if (!MayContain(cls, EnumeratorWords) && (aliases.Count == 0 || !MayContain(cls, aliases.ToArray())))
                return false;
            return cls.DescendantTokens().Any(t => t.IsKind(SyntaxKind.YieldKeyword)
                || t.IsKind(SyntaxKind.IdentifierToken) && (System.Array.IndexOf(EnumeratorWords, t.ValueText) >= 0 || aliases.Contains(t.ValueText)));
        }

        /// <summary>呼び出しの名前（Foo()・x.Foo()・x?.Foo()・Foo&lt;T&gt;()）。分からない形は null</summary>
        private static string InvokedName(InvocationExpressionSyntax call)
        {
            switch (call.Expression)
            {
                case SimpleNameSyntax name: return name.Identifier.ValueText;
                case MemberAccessExpressionSyntax access: return access.Name.Identifier.ValueText;
                case MemberBindingExpressionSyntax binding: return binding.Name.Identifier.ValueText;
                default: return null;
            }
        }

        /// <param name="only">null でなければ、呼び出しは、名前がこの中のものだけを調べる</param>
        private static void CheckEnumeratorUse(SemanticModel model, SyntaxNode node, InterfaceLowering.LoweringDiagnostics diagnostics, HashSet<string> only)
        {
            // e.MoveNext()・e.Current・e.Reset()
            if (node is MemberAccessExpressionSyntax access
                && (access.Name.Identifier.ValueText == "MoveNext" || access.Name.Identifier.ValueText == "Current" || access.Name.Identifier.ValueText == "Reset")
                && model.GetTypeInfo(access.Expression).Type?.ToDisplayString() == EnumeratorName)
            {
                diagnostics.Error(access.Name.GetLocation(), Loc.T(
                    $"IEnumerator の {access.Name.Identifier.ValueText} は未対応です。IEnumerator はコルーチンにだけ使えます（StartCoroutine で動かしてください）",
                    $"IEnumerator.{access.Name.Identifier.ValueText} is not supported. IEnumerator can only be used for coroutines (run it with StartCoroutine)"));
                return;
            }

            if (!(node is InvocationExpressionSyntax call) || only != null && InvokedName(call) is string invoked && !only.Contains(invoked)
                || !(model.GetSymbolInfo(call).Symbol is IMethodSymbol method)
                || method.ReturnType.ToDisplayString() != EnumeratorName)
                return;
            if (method.DeclaringSyntaxReferences.Length == 0 && method.MethodKind != MethodKind.DelegateInvoke)
            {
                // 外部のメソッドが返す IEnumerator（transform.GetEnumerator() など）
                diagnostics.Error(call.GetLocation(), Loc.T(
                    $"外部のメソッド（{method.ContainingType.Name}.{method.Name}）が返す IEnumerator は、コルーチンとして使えません。IEnumerator はコルーチンにだけ使えます",
                    $"An IEnumerator returned by an external method ({method.ContainingType.Name}.{method.Name}) cannot be used as a coroutine. IEnumerator can only be used for coroutines"));
                return;
            }
            // other.Foo()：ほかのビヘイビアの、IEnumerator を返すメソッド
            if (call.Expression is MemberAccessExpressionSyntax target && !method.IsStatic
                && !(target.Expression is ThisExpressionSyntax) && !(target.Expression is BaseExpressionSyntax)
                && InterfaceLowering.IsUdonSharpBehaviour(method.ContainingType))
            {
                diagnostics.Error(call.GetLocation(), Loc.T(
                    $"ほかのビヘイビアのコルーチン（{target.Expression}.{method.Name}(...)）は未対応です。そのビヘイビアに、コルーチンを開始する public メソッドを作って呼んでください",
                    $"Coroutines of another behaviour ({target.Expression}.{method.Name}(...)) are not supported. Add a public method that starts the coroutine to that behaviour and call it"));
            }
        }
    }
}
