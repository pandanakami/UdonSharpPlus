using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// コンパイルエラーにはならないが、C# と動きが違うもの・気付きにくいものを使っているとき、コンパイルのたびに Info のログで知らせる（Redmine #118）。
    ///
    /// - 注意ごとに ID（USP001 など）を付ける。出す・出さないは Project Settings > Udon Sharp Plus で ID ごとに選べる（UdonSharpPlusSettings）
    /// - 同じ ID は、コンパイルごとに 1 回だけ、最初に使った位置（ファイルのパスの順、ファイルの中の位置の順）に出す
    /// - 書き換える前の元のソースを見るので、位置はそのまま元のソースの位置になる
    /// - 使われない普通のクラスの中のものは出さない（書き換えも検査もしないものなので。エラーと同じ）
    /// - U# の診断（Log）で出すと、コンソールでその位置に飛べないので、ファイルへのリンク（&lt;a href line&gt;）を付けて自分で出す
    /// </summary>
    internal static class Notices
    {
        internal sealed class Rule
        {
            public readonly string Id;
            public readonly bool DefaultOn;
            private readonly string _titleJa, _titleEn, _messageJa, _messageEn, _docJa, _docEn;

            /// <summary>設定の画面に出す短い説明（設定の言語。#67）</summary>
            public string Title => Loc.T(_titleJa, _titleEn);
            public string Message => Loc.T(_messageJa, _messageEn);
            /// <summary>詳しい説明の場所</summary>
            public string Doc => Loc.T(_docJa, _docEn);

            public Rule(string id, bool defaultOn, string titleJa, string titleEn, string messageJa, string messageEn, string docJa, string docEn = null)
            {
                Id = id;
                DefaultOn = defaultOn;
                _titleJa = titleJa;
                _titleEn = titleEn;
                _messageJa = messageJa;
                _messageEn = messageEn;
                _docJa = docJa;
                _docEn = docEn ?? docJa;   // ファイル名だけのものは同じ
            }
        }

        internal static readonly Rule[] Rules =
        {
            new Rule("USP001", true, "LINQ の結果を変数に入れる（遅延評価にしない）", "Keeping a LINQ result in a variable (no deferred execution)",
                "LINQ の結果を変数に入れています。UdonSharp Plus の LINQ は遅延評価にせず、呼んだ時点で結果を作ります。あとで元の List を変えても結果は変わらず、2 回使っても同じ結果です（C# では使うたびに計算し直します）", "A LINQ result is kept in a variable. LINQ in UdonSharp Plus is not deferred: the result is built when it is called. Changing the source List later does not change the result, and using it twice gives the same result (in C# it is computed again each time it is used)",
                "collections.md の「LINQ」", "collections.md"),
            new Rule("USP002", false, "Concat・Append・Prepend のあとを途中で止める LINQ（ラムダを呼ぶ回数）", "LINQ that stops early after Concat / Append / Prepend (how often lambdas are called)",
                "Concat・Append・Prepend のあとを途中で止める書き方です。結果は C# と同じですが、その前の段のラムダは、全部の要素で呼ばれます（C# では必要な分だけ）", "This LINQ stops early after Concat / Append / Prepend. The result is the same as C#, but the lambdas before it are called for every element (in C# only as many as needed)",
                "collections.md の「LINQ」", "collections.md"),
            new Rule("USP003", true, "foreach の中で、回している List / Dictionary を書き換える", "Changing the List / Dictionary being iterated inside foreach",
                "foreach で回している List / Dictionary を、その中で書き換えています。C# の InvalidOperationException にはならず、Add した要素は回らず、Remove・Clear すると範囲外を読んで実行時例外で止まります", "The List / Dictionary being iterated by foreach is changed inside the loop. This does not throw InvalidOperationException as in C#: added elements are not visited, and Remove / Clear reads out of range and stops with a runtime exception",
                "collections.md の「List / Dictionary」", "collections.md"),
            new Rule("USP004", false, "Dictionary の foreach・LINQ の順番", "Order of foreach and LINQ over a Dictionary",
                "Dictionary の foreach・LINQ の順番は、DataDictionary の GetKeys() の順番です。C# の Dictionary と同じとは限りません", "foreach and LINQ over a Dictionary use the order of DataDictionary.GetKeys(). It is not always the same as a C# Dictionary",
                "collections.md の「List / Dictionary」", "collections.md"),
            new Rule("USP005", true, "インタフェース型の値と null の比較", "Comparing an interface value with null",
                "インタフェース型の値と null を比べています。参照の比較なので、Destroy した UdonSharpBehaviour は null になりません（UnityEngine.Object の比較とは違います）", "An interface value is compared with null. This is a reference comparison, so a destroyed UdonSharpBehaviour is not null (unlike the UnityEngine.Object comparison)",
                "interface.md"),
            new Rule("USP006", false, "普通のクラスの値型のフィールドのメソッド（自分を書き換えるもの）", "Methods on a value-type field of a plain class (ones that change the field)",
                "普通のクラスの値型のフィールドで、戻り値のないメソッドを呼んでいます。フィールドのコピーに対して呼ばれるので、そのメソッドがフィールド自体を書き換えるもの（Normalize など）なら、反映されません。x = x.normalized; のように代入してください", "A method with no return value is called on a value-type field of a plain class. It is called on a copy of the field, so if the method changes the field itself (Normalize and so on), the change is lost. Assign the result instead, like x = x.normalized;",
                "class.md"),
            new Rule("USP007", true, "UdonSharpBehaviour のデリゲート（Destroy したあとに呼ぶ）", "Delegates of an UdonSharpBehaviour (calling after Destroy)",
                "UdonSharpBehaviour のメソッド、またはその中で作ったラムダなどのデリゲートです。相手の UdonSharpBehaviour を Destroy したあとに呼ぶと、実行時例外で止まります（C# ではそのまま呼ばれます）", "This is a delegate of an UdonSharpBehaviour method, or of a lambda created in one. Calling it after the UdonSharpBehaviour is destroyed stops with a runtime exception (in C# the method is still called)",
                "delegates.md"),
            new Rule("USP008", false, "UdonSharpBehaviour の中の static メソッドのデリゲートの -=", "-= with a delegate of a static method inside an UdonSharpBehaviour",
                "UdonSharpBehaviour の中で作った static メソッドのデリゲートを -= しています。デリゲートは作った UdonSharpBehaviour ごとに別のものなので、別の UdonSharpBehaviour で作ったものは外れません（C# では外れます）", "A delegate of a static method created inside an UdonSharpBehaviour is removed with -=. Each UdonSharpBehaviour creates its own delegate, so one created in another UdonSharpBehaviour is not removed (in C# it is)",
                "delegates.md"),
            new Rule("USP009", false, "値がタプルの Dictionary の TryGetValue", "TryGetValue on a Dictionary whose values are tuples",
                "値がタプルの Dictionary の TryGetValue です。見つからなかったときの out の値は null になります（C# では各要素が既定値のタプル）。見つかったときだけ使ってください", "This is TryGetValue on a Dictionary whose values are tuples. When the key is not found, the out value is null (in C# it is a tuple of default values). Use it only when the key is found",
                "tuples.md"),
            new Rule("USP010", false, "== を宣言した型の List の Contains・IndexOf・Remove", "Contains / IndexOf / Remove on a List of a type that declares ==",
                "== を宣言した型の List の Contains・IndexOf・LastIndexOf・Remove です。C# と同じく Equals で比べ、== は使われません。Equals の override は未対応なので、参照の比較になります", "This is Contains / IndexOf / LastIndexOf / Remove on a List of a type that declares ==. As in C#, they compare with Equals, not ==. Overriding Equals is not supported, so this is a reference comparison",
                "operators.md"),
            new Rule("USP011", false, "C# で例外になる場合に、例外の代わりにログを出して止まるメソッド", "Methods that log and stop instead of throwing where C# throws",
                "C# で例外になる場合（要素がない、キーがない、範囲の外など）、例外の代わりにエラーのログを出して、Udon の実行時例外で止まります（try / catch はできません）", "Where C# would throw (no elements, missing key, out of range and so on), this logs an error and stops with an Udon runtime exception instead (try / catch is not possible)",
                "collections.md の「LINQ」", "collections.md"),
            new Rule("USP012", false, "インタフェース型の変数でメソッドを呼ぶ（null のときの例外の位置）", "Calling a method on an interface variable (exception position when null)",
                "インタフェース型の値でメソッドを呼んでいます。値が null のときの実行時例外の位置は、呼び出した行ではなく、このクラスの閉じ括弧の行になります", "A method is called on an interface value. When the value is null, the runtime exception is reported on the closing brace line of this class, not on the calling line",
                "interface.md"),
            new Rule("USP013", false, "メソッドの呼び出しの結果で、普通のクラスのメソッドを呼ぶ（null のときの例外の位置）", "Calling a plain class method on the result of a method call (exception position when null)",
                "メソッドの呼び出しの結果で、普通のクラスのメソッドを呼んでいます。結果が null のときの実行時例外の位置は、呼び出した行ではなく、このクラスの閉じ括弧の行になることがあります", "A plain class method is called on the result of a method call. When the result is null, the runtime exception may be reported on the closing brace line of this class, not on the calling line",
                "class.md"),
            new Rule("USP014", false, ".NET のデリゲート（Action・Func など）の例外の位置", "Exception position for .NET delegates (Action, Func and so on)",
                ".NET のデリゲート（Action・Func・Predicate・Comparison）を使っています。ソースに宣言がないので、宣言の行で止まる実行時例外の位置が、この型を最初に使ったファイルの最後の行になります", "A .NET delegate (Action / Func / Predicate / Comparison) is used. It has no declaration in source, so a runtime exception reported at the declaration appears on the last line of the first file that uses this type",
                "delegates.md"),
            new Rule("USP015", false, "書き換えたファイルの、エラー・例外の列の位置", "Column positions of errors and exceptions in rewritten files",
                "このファイルは UdonSharp Plus が書き換えています。書き換えた行の、コンパイルエラー・実行時例外の列の位置は、ずれることがあります（行は合います）", "This file is rewritten by UdonSharp Plus. On rewritten lines, the column of compile errors and runtime exceptions may be off (the line is correct)",
                "README.md の「全体に共通すること」", "README.md"),
            new Rule("USP016", false, "デリゲートの呼び出しの速さ", "Speed of delegate calls",
                "デリゲートの呼び出しは、普通のメソッドの呼び出しより遅くなります（UdonSharpBehaviour のメソッドは SendCustomEvent などを使い、ラムダがキャプチャした変数は object[] の要素の読み書きになります）。毎フレーム何度も呼ぶところでは気をつけてください", "Calling a delegate is slower than calling a normal method (UdonSharpBehaviour methods use SendCustomEvent and similar, and variables captured by a lambda are read and written as object[] elements). Be careful where it is called many times per frame",
                "delegates.md"),
        };

        internal static Rule Find(string id) => Rules.First(r => r.Id == id);

        private const string Prefix = "[UdonSharpPlus] ";

        /// <summary>テストで出し先を差し替える（null ならコンソール）。テストの仮のファイルの注意を、コンソールに残さないため</summary>
        internal static Action<string> Output;

        /// <summary>見つけたもの（まだ出していない）。使われない普通のクラスの中のものは、出す前に捨てる</summary>
        internal sealed class Found
        {
            private readonly List<(Rule Rule, int Tree, int Position, Location Location, INamedTypeSymbol Owner)> _items =
                new List<(Rule, int, int, Location, INamedTypeSymbol)>();

            internal void Add(Rule rule, int tree, Location location, INamedTypeSymbol owner) =>
                _items.Add((rule, tree, location.SourceSpan.Start, location, owner));

            internal bool IsEmpty => _items.Count == 0;

            /// <summary>
            /// ID ごとに最初の 1 つを出す。paths は binding の並びのファイルのパス。
            /// usedClasses は UdonSharpBehaviour から使われた普通のクラスの型タグ（ClassLowering.TagOf）
            /// </summary>
            internal void Report(IReadOnlyList<string> paths, HashSet<string> usedClasses)
            {
                foreach (var group in _items
                             .Where(i => i.Owner == null || IsUsed(i.Owner, usedClasses))
                             .Where(i => !IsPackage(i.Tree, paths))
                             // U# のファイルの並びはコンパイルごとに変わりうるので、パスの順で選ぶ（毎回同じ位置に出すため）
                             .OrderBy(i => i.Tree >= 0 && i.Tree < paths.Count ? ProjectRelative(paths[i.Tree]) ?? "" : "", StringComparer.Ordinal)
                             .ThenBy(i => i.Tree).ThenBy(i => i.Position)
                             .GroupBy(i => i.Rule.Id)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var first = group.First();
                    string path = first.Tree >= 0 && first.Tree < paths.Count ? ProjectRelative(paths[first.Tree]) : null;
                    string text = Text(first.Rule, path, first.Location);
                    if (Output != null)
                        Output(text);
                    else
                        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", text);
                }
            }

            /// <summary>パッケージ（Packages/ の下）のファイル。使う人には直せないので出さない</summary>
            private static bool IsPackage(int tree, IReadOnlyList<string> paths) =>
                tree >= 0 && tree < paths.Count && (ProjectRelative(paths[tree]) ?? "").StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

            private static bool IsUsed(INamedTypeSymbol owner, HashSet<string> usedClasses)
            {
                if (usedClasses == null)
                    return true;
                if (owner.IsGenericType)
                {
                    // ジェネリッククラスは、型引数ごとのコピーの名前（Pool__int など）で記録される
                    string head = owner.Name + "__";
                    return usedClasses.Any(u => u.StartsWith(head, StringComparison.Ordinal) || u.Contains("." + head));
                }
                return usedClasses.Contains(ClassLowering.TagOf(owner));
            }
        }

        /// <summary>ログの文。位置はコンソールから開けるリンクにする（Unity のコンソールの &lt;a href="path" line="n"&gt;）</summary>
        internal static string Text(Rule rule, string path, Location location)
        {
            var start = location.GetLineSpan().StartLinePosition;
            int line = start.Line + 1;
            string at = $"{path ?? "Unknown File"}({line},{start.Character + 1})";
            string link = path != null ? $"<a href=\"{path}\" line=\"{line}\">{at}</a>" : at;
            return Loc.T($"{Prefix}{link}: {rule.Id}: {rule.Message}（詳しくは {rule.Doc}。Project Settings > Udon Sharp Plus で、出さないようにできます）", $"{Prefix}{link}: {rule.Id}: {rule.Message} (see {rule.Doc}; you can turn this off in Project Settings > Udon Sharp Plus)");
        }

        private static string ProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string p = path.Replace('\\', '/');
            string root = System.IO.Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/') + "/";
            return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length) : p;
        }

        // ---- 見つける ---------------------------------------------------------------

        /// <summary>元のソース（書き換える前）から探す。出す注意がひとつもなければ何もしない</summary>
        public static Found Scan(IReadOnlyList<SyntaxTree> trees, IEnumerable<MetadataReference> references, Func<Rule, bool> enabled)
        {
            var found = new Found();
            var on = new HashSet<string>(Rules.Where(enabled).Select(r => r.Id));
            // USP015 は書き換えの結果を見るので、ここでは探さない（InterfaceLowering.Process で足す）
            if (on.Count == 0 || (on.Count == 1 && on.Contains("USP015")))
                return found;

            try
            {
                var compilation = CSharpCompilation.Create(
                    "UdonSharpPlus_NoticePrepass",
                    trees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                for (int i = 0; i < trees.Count; i++)
                    new Walker(compilation.GetSemanticModel(trees[i]), i, found, on).Visit(trees[i].GetRoot());
                return found;
            }
            catch (Exception e)
            {
                // 注意は付け足しの情報なので、探すところで何かあっても、コンパイルは止めない
                Debug.LogWarning(Loc.T($"{Prefix}コンパイル時の注意を探すところで例外が起きたため、今回は注意を出しません：{e.GetType().Name}: {e.Message}", $"{Prefix}An exception occurred while looking for compile-time notices, so no notices are shown this time: {e.GetType().Name}: {e.Message}"));
                return new Found();
            }
        }

        /// <summary>USP015：書き換えたファイルの、最初に変わった行</summary>
        public static void AddRewritten(Found found, IReadOnlyList<SyntaxTree> original, IReadOnlyList<SyntaxTree> rewritten, Func<Rule, bool> enabled)
        {
            Rule rule = Find("USP015");
            if (!enabled(rule))
                return;
            for (int i = 0; i < original.Count && i < rewritten.Count; i++)
            {
                if (ReferenceEquals(original[i], rewritten[i]))
                    continue;
                var before = original[i].GetText().Lines;
                var after = rewritten[i].GetText().Lines;
                for (int line = 0; line < before.Count; line++)
                {
                    if (line < after.Count && before[line].ToString() == after[line].ToString())
                        continue;
                    var span = before[line].Span;
                    found.Add(rule, i, Location.Create(original[i], new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, 0)), null);
                    return;
                }
            }
        }

        private sealed class Walker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _model;
            private readonly int _tree;
            private readonly Found _found;
            private readonly HashSet<string> _on;

            public Walker(SemanticModel model, int tree, Found found, HashSet<string> on)
            {
                _model = model;
                _tree = tree;
                _found = found;
                _on = on;
            }

            private void Add(string id, SyntaxNode node) => Add(id, node.GetLocation(), node);

            private void Add(string id, Location location, SyntaxNode context)
            {
                if (!_on.Contains(id))
                    return;
                INamedTypeSymbol host = Host(context);
                // UdonSharpBehaviour でも普通のクラスでもないもの（エディタ用のクラスなど）の中は、U# のコードではないので出さない
                if (host == null)
                    return;
                _found.Add(Find(id), _tree, location, InterfaceLowering.IsUdonSharpBehaviour(host) ? null : host);
            }

            /// <summary>node を含む型（UdonSharpBehaviour か普通のクラス）。どちらでもなければ null</summary>
            private INamedTypeSymbol Host(SyntaxNode node)
            {
                var decl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (decl == null || !(_model.GetDeclaredSymbol(decl) is INamedTypeSymbol type))
                    return null;
                return InterfaceLowering.IsUdonSharpBehaviour(type) || ClassLowering.IsPlainClass(type) ? type : null;
            }

            // ---- 型の判定 -----------------------------------------------------------

            private static bool IsGeneric(ITypeSymbol type, string definition) =>
                type is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == definition;

            private static bool IsList(ITypeSymbol type) => IsGeneric(type, "System.Collections.Generic.List<T>");

            private static bool IsDictionary(ITypeSymbol type) => IsGeneric(type, "System.Collections.Generic.Dictionary<TKey, TValue>");

            private static bool IsDictionaryView(ITypeSymbol type) =>
                IsDictionary(type) || type?.ContainingType != null && IsDictionary(type.ContainingType);

            private static bool IsEnumerable(IMethodSymbol m) => m?.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";

            private static bool IsSourceInterface(ITypeSymbol type) =>
                type != null && type.TypeKind == TypeKind.Interface && type.Locations.Any(l => l.IsInSource);

            private static bool IsDotNetDelegate(ITypeSymbol type) =>
                type is INamedTypeSymbol n && n.TypeKind == TypeKind.Delegate && n.ContainingNamespace?.ToDisplayString() == "System"
                && n.ContainingType == null && (n.Name == "Action" || n.Name == "Func" || n.Name == "Predicate" || n.Name == "Comparison");

            private static ExpressionSyntax Unwrap(ExpressionSyntax e)
            {
                while (e is ParenthesizedExpressionSyntax p)
                    e = p.Expression;
                return e;
            }

            private static SyntaxNode ParentSkippingParens(SyntaxNode node)
            {
                SyntaxNode parent = node.Parent;
                while (parent is ParenthesizedExpressionSyntax)
                    parent = parent.Parent;
                return parent;
            }

            private static readonly HashSet<string> ShortCircuit = new HashSet<string>
                { "First", "FirstOrDefault", "Any", "All", "Contains", "Take", "TakeWhile", "ElementAt", "ElementAtOrDefault", "Single", "SingleOrDefault" };

            private static readonly HashSet<string> Buffering = new HashSet<string> { "Concat", "Append", "Prepend" };

            private static readonly HashSet<string> Throwing = new HashSet<string>
                { "First", "Last", "Single", "SingleOrDefault", "Min", "Max", "Average", "ElementAt", "ToDictionary", "Range", "Repeat" };

            private static readonly HashSet<string> Mutators = new HashSet<string>
                { "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "Clear", "Sort", "Reverse", "TryAdd" };

            // ---- 呼び出し -----------------------------------------------------------

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                base.VisitInvocationExpression(node);
                if (!(_model.GetSymbolInfo(node).Symbol is IMethodSymbol m))
                    return;
                var access = node.Expression as MemberAccessExpressionSyntax;
                SyntaxNode at = (SyntaxNode)access?.Name ?? node;
                ITypeSymbol receiver = access != null ? _model.GetTypeInfo(access.Expression).Type : null;

                if (IsEnumerable(m))
                {
                    string returnName = m.ReturnType.OriginalDefinition.Name;
                    if ((returnName == "IEnumerable" || returnName == "IOrderedEnumerable") && IsStored(node))
                        Add("USP001", at);
                    if (ShortCircuit.Contains(m.Name) && access != null && HasBufferingAfterLambda(access.Expression))
                        Add("USP002", at);
                    if (receiver != null && IsDictionaryView(receiver))
                        Add("USP004", at);
                    if (Throwing.Contains(m.Name) || m.Name == "Aggregate" && m.Parameters.Length == (m.IsExtensionMethod && m.ReducedFrom == null ? 2 : 1))
                        Add("USP011", at);
                }
                else if (m.ContainingType != null && IsDictionary(m.ContainingType) && m.Name == "TryGetValue"
                         && m.ContainingType.TypeArguments[1].IsTupleType)
                {
                    Add("USP009", at);
                }
                else if (m.ContainingType != null && IsList(m.ContainingType)
                         && (m.Name == "Contains" || m.Name == "IndexOf" || m.Name == "LastIndexOf" || m.Name == "Remove")
                         && DeclaresEquality(m.ContainingType.TypeArguments[0]))
                {
                    Add("USP010", at);
                }

                if (access != null && !m.IsStatic && m.ReducedFrom == null)
                {
                    if (IsSourceInterface(receiver))
                        Add("USP012", at);
                    if (m.ContainingType is INamedTypeSymbol owner && ClassLowering.IsPlainClass(owner) && Unwrap(access.Expression) is InvocationExpressionSyntax)
                        Add("USP013", at);
                    if (m.ReturnsVoid && !m.IsReadOnly && m.ContainingType?.TypeKind == TypeKind.Struct
                        && _model.GetSymbolInfo(access.Expression).Symbol is IFieldSymbol field
                        && field.ContainingType is INamedTypeSymbol fieldOwner && ClassLowering.IsPlainClass(fieldOwner))
                        Add("USP006", at);
                }
            }

            private static bool DeclaresEquality(ITypeSymbol type) =>
                type.GetMembers(WellKnownMemberNames.EqualityOperatorName).Any(s => s.Locations.Any(l => l.IsInSource));

            /// <summary>LINQ の結果を、変数・フィールド・プロパティに入れているか</summary>
            private static bool IsStored(ExpressionSyntax node)
            {
                SyntaxNode parent = ParentSkippingParens(node);
                if (parent is EqualsValueClauseSyntax)
                    return parent.Parent is VariableDeclaratorSyntax || parent.Parent is PropertyDeclarationSyntax;
                return parent is AssignmentExpressionSyntax assign && assign.IsKind(SyntaxKind.SimpleAssignmentExpression) && Unwrap(assign.Right) == Unwrap(node);
            }

            /// <summary>受け側の LINQ の鎖に、ラムダを取る段のあとの Concat・Append・Prepend があるか</summary>
            private bool HasBufferingAfterLambda(ExpressionSyntax receiver)
            {
                bool buffering = false;
                for (ExpressionSyntax e = Unwrap(receiver); e is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax ma; e = Unwrap(ma.Expression))
                {
                    if (!IsEnumerable(_model.GetSymbolInfo(call).Symbol as IMethodSymbol))
                        break;
                    if (Buffering.Contains(ma.Name.Identifier.Text))
                        buffering = true;
                    else if (buffering && call.ArgumentList.Arguments.Any(a => a.Expression is AnonymousFunctionExpressionSyntax))
                        return true;
                }
                return false;
            }

            // ---- foreach --------------------------------------------------------------

            public override void VisitForEachStatement(ForEachStatementSyntax node)
            {
                base.VisitForEachStatement(node);
                ITypeSymbol type = _model.GetTypeInfo(node.Expression).Type;
                if (type == null)
                    return;
                if (IsDictionaryView(type))
                    Add("USP004", node.Expression);
                if (!IsList(type) && !IsDictionaryView(type))
                    return;

                // 回しているもの（.Keys・.Values なら、その Dictionary）
                ExpressionSyntax collection = Unwrap(node.Expression);
                if (!IsList(type) && !IsDictionary(type) && collection is MemberAccessExpressionSyntax view)
                    collection = Unwrap(view.Expression);
                ISymbol target = _model.GetSymbolInfo(collection).Symbol;
                if (!(target is ILocalSymbol || target is IFieldSymbol || target is IParameterSymbol || target is IPropertySymbol))
                    return;

                foreach (SyntaxNode n in node.Statement.DescendantNodesAndSelf())
                {
                    if (n is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax ma
                        && Mutators.Contains(ma.Name.Identifier.Text)
                        && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(Unwrap(ma.Expression)).Symbol, target)
                        && _model.GetSymbolInfo(call).Symbol is IMethodSymbol m && (IsList(m.ContainingType) || IsDictionary(m.ContainingType)))
                    {
                        Add("USP003", ma.Name);
                        return;
                    }
                    // Dictionary の添字への代入（C# では、回している途中に値を変えるのも InvalidOperationException）
                    if (n is AssignmentExpressionSyntax assign && Unwrap(assign.Left) is ElementAccessExpressionSyntax element
                        && IsDictionary(_model.GetTypeInfo(element.Expression).Type)
                        && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(Unwrap(element.Expression)).Symbol, target))
                    {
                        Add("USP003", assign);
                        return;
                    }
                }
            }

            // ---- null との比較 -----------------------------------------------------------

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                base.VisitBinaryExpression(node);
                if (!node.IsKind(SyntaxKind.EqualsExpression) && !node.IsKind(SyntaxKind.NotEqualsExpression))
                    return;
                ExpressionSyntax other = IsNull(node.Left) ? node.Right : IsNull(node.Right) ? node.Left : null;
                if (other != null && IsSourceInterface(_model.GetTypeInfo(other).Type))
                    Add("USP005", node);
            }

            public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
            {
                base.VisitIsPatternExpression(node);
                ExpressionSyntax pattern = node.Pattern is ConstantPatternSyntax c ? c.Expression
                    : node.Pattern is UnaryPatternSyntax u && u.Pattern is ConstantPatternSyntax uc ? uc.Expression : null;
                if (pattern != null && IsNull(pattern) && IsSourceInterface(_model.GetTypeInfo(node.Expression).Type))
                    Add("USP005", node);
            }

            private static bool IsNull(ExpressionSyntax e) => Unwrap(e).IsKind(SyntaxKind.NullLiteralExpression);

            // ---- デリゲート -----------------------------------------------------------

            public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                base.VisitParenthesizedLambdaExpression(node);
                Delegate(node, null);
            }

            public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                base.VisitSimpleLambdaExpression(node);
                Delegate(node, null);
            }

            public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
            {
                base.VisitAnonymousMethodExpression(node);
                Delegate(node, null);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                base.VisitIdentifierName(node);
                if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node)
                    return;   // a.M は VisitMemberAccessExpression で見る
                MethodGroup(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                base.VisitMemberAccessExpression(node);
                MethodGroup(node);
            }

            /// <summary>メソッドを、呼ばずにデリゲートにしているもの（Action a = M;、a += Other.M;）</summary>
            private void MethodGroup(ExpressionSyntax node)
            {
                if (node.Parent is InvocationExpressionSyntax call && call.Expression == node)
                    return;
                if (node.Parent is MemberAccessExpressionSyntax outer && outer.Expression == node)
                    return;
                if (_model.GetSymbolInfo(node).Symbol is IMethodSymbol method)
                    Delegate(node, method);
            }

            /// <param name="method">メソッドからデリゲートを作るもの（ラムダなら null）</param>
            private void Delegate(ExpressionSyntax node, IMethodSymbol method)
            {
                if (!(_model.GetTypeInfo(node).ConvertedType is INamedTypeSymbol type) || type.TypeKind != TypeKind.Delegate)
                    return;
                if (IsInNameOf(node) || IsInlinedArgument(node))
                    return;

                INamedTypeSymbol host = Host(node);
                if (host == null)
                    return;
                bool behaviourTarget = method != null
                    ? method.MethodKind == MethodKind.LocalFunction ? InterfaceLowering.IsUdonSharpBehaviour(host)
                    : method.ContainingType is INamedTypeSymbol owner && InterfaceLowering.IsUdonSharpBehaviour(owner)
                    : InterfaceLowering.IsUdonSharpBehaviour(host);

                if (behaviourTarget)
                {
                    Add("USP007", node);
                    Add("USP016", node);
                }
                if (method == null && node is AnonymousFunctionExpressionSyntax && Captures(node))
                    Add("USP016", node);
                if (IsDotNetDelegate(type))
                    Add("USP014", node);

                if (method != null && method.IsStatic && InterfaceLowering.IsUdonSharpBehaviour(host)
                    && ParentSkippingParens(node) is AssignmentExpressionSyntax assign && assign.IsKind(SyntaxKind.SubtractAssignmentExpression)
                    && Unwrap(assign.Right) == node)
                    Add("USP008", assign);
            }

            private bool Captures(ExpressionSyntax lambda)
            {
                try
                {
                    DataFlowAnalysis flow = _model.AnalyzeDataFlow(lambda);
                    return flow != null && flow.Succeeded && flow.Captured.Any(s => !(s is IParameterSymbol p && p.IsThis));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            private static bool IsInNameOf(SyntaxNode node) =>
                node.Ancestors().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is IdentifierNameSyntax id && id.Identifier.Text == "nameof");

            /// <summary>LINQ・List・Dictionary のメソッドの引数に直接書いたもの（デリゲートを作らず、その場に展開する）</summary>
            private bool IsInlinedArgument(ExpressionSyntax node)
            {
                if (!(ParentSkippingParens(node) is ArgumentSyntax arg) || !(arg.Parent?.Parent is InvocationExpressionSyntax call))
                    return false;
                if (!(_model.GetSymbolInfo(call).Symbol is IMethodSymbol m))
                    return false;
                return IsEnumerable(m) || IsList(m.ContainingType) || IsDictionary(m.ContainingType);
            }
        }
    }
}
