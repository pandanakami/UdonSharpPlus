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

namespace UdonSharpPlus
{
    /// <summary>
    /// ソース上で宣言されたインタフェースを、UdonSharp が扱える形へ書き換える。
    ///
    /// 実装側 (UdonSharpBehaviour : IFoo、汎用クラス : IFoo):
    ///   - 基底リストから IFoo を除去するだけ。
    ///   - UdonSharpBehaviour の実装メソッドの Udon 上の名前は MethodExportLayout のコンストラクタパッチ
    ///     (<see cref="MethodExportLayoutPatch"/>) でシグネチャ由来の固定名に揃える。
    ///     そのため、ここでは対象メソッドのキー一覧 (<see cref="InterfaceNaming.ImplementationKeys"/>) を作る。
    /// 呼び出し側:
    ///   - 型 IFoo → object（実体は UdonSharpBehaviour か、汎用クラスのインスタンス object[]。#50）
    ///   - x.M(a)  → __if_M__sig__call(x, a)
    ///     汎用クラスなら if (x is Bullet) return ((Bullet)x).M(a); のように普通の C# で振り分ける
    ///     （この後の汎用クラスの段（ClassLowering）が、コピーの呼び出しに書き換え、実装クラスのコピーを入れる）。
    ///     実装クラスが 4 個以上なら、インスタンスの型番号（[1]）の switch（ジャンプテーブル）で振り分ける（#65）。
    ///     それ以外は UdonSharpBehaviour として SetProgramVariable / SendCustomEvent / GetProgramVariable
    ///   - x is IFoo → __if_K__is(x),  x as IFoo → __if_K__as(x)
    ///     （汎用クラスは is で、UdonSharpBehaviour は U# の型ID "__refl_typeid" を実装クラスの型IDと照合）
    ///
    /// 書き換えは「行を増やさない」ようにしている（生成メンバーはクラス末尾 '}' と同じ行に置く）ので、
    /// U# のエラー行番号は元ソースと一致する。
    /// </summary>
    internal static class InterfaceLowering
    {
        internal const string UsbType = "global::UdonSharp.UdonSharpBehaviour";

        /// <summary>インタフェース型の置き換え先（UdonSharpBehaviour と汎用クラスの object[] の両方を入れられるもの。#50）</summary>
        internal const string ErasedType = "object";
        private static readonly string DumpDir = Path.Combine("Library", "UdonSharpPlus");

        /// <summary>書き換え後のソースを Library/UdonSharpPlus/ に出すか（テストでは止める）</summary>
        internal static bool DumpEnabled = true;

        internal sealed class InterfaceInfo
        {
            public INamedTypeSymbol Symbol;
            public string Key;
            public readonly List<long> ImplTypeIds = new List<long>();   // 実装クラス (具象) の U# 型ID
            public readonly List<INamedTypeSymbol> ImplClasses = new List<INamedTypeSymbol>();   // 実装する汎用クラス（#50）
            public string IsHelper => $"__if_{Key}__is";
            public string AsHelper => $"__if_{Key}__as";
            public readonly List<MethodInfo> Methods = new List<MethodInfo>();
        }

        internal sealed class MethodInfo
        {
            public IMethodSymbol Symbol;
            public string Signature;            // InterfaceNaming.SignatureName
            public string Export => Symbol.Name;  // Udon 上のメソッド名は元の名前（#51。代わりにオーバーロードを禁止する）
            public string CallHelper => $"__if_{Signature}__call";
            public string ParamField(int i) => InterfaceNaming.ParameterName(Signature, i);
            public string RetField => InterfaceNaming.ReturnName(Signature);
            public bool HasReturn => !Symbol.ReturnsVoid;
        }

        /// <param name="context">
        /// U# の CompilationContext。本パッケージのエラー・警告を U# の診断として登録する
        /// （エラーがあれば U# はこの直後でコンパイルを止める）。null ならログに出すだけ。
        /// </param>
        public static void Process(Array bindings, FieldInfo treeField, FieldInfo sourceTextField, object context)
        {
            InterfaceNaming.ImplementationKeys = new HashSet<string>();
            SourceMap.Clear();

            var original = new List<SyntaxTree>(bindings.Length);
            foreach (object b in bindings)
                original.Add((SyntaxTree)treeField.GetValue(b));

            var diagnostics = new LoweringDiagnostics();
            List<SyntaxTree> current = original;
            SourceMap.RegisterTrees(current);

            // 0) ユーザー定義のジェネリクス → 型引数ごとのコピー（#59）。後の段はジェネリクスのないコードを見る
            if (GenericLowering.MayHaveGenerics(current))
            {
                current = GenericLowering.Run(current, diagnostics, GetMetadataReferences());
                SourceMap.RegisterTrees(current);
            }

            // タプル（#81）は UdonSharp が対応していないのでエラーにする。書き換えの前に見るので、元のソースの位置に出る
            if (TupleCheck.MayHaveTuples(current))
                TupleCheck.Run(current, diagnostics, GetMetadataReferences());

            // 1) インタフェース → 2) 汎用クラス（#49）の順に書き換える。どちらも行数を変えず、tree の並びも変えない。
            // インタフェースの段は、汎用クラスの実装への振り分けを普通の C#（((Bullet)x).M(a)）で書き、
            // 汎用クラスの段がそれをコピーの呼び出しにする（#50）
            var rewrittenClasses = new List<RewrittenClass>();
            var classNumbers = new Dictionary<string, int>();
            if (current.Any(t => t.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Any()))
            {
                current = LowerInterfaces(current, diagnostics, rewrittenClasses, classNumbers);
                SourceMap.RegisterTrees(current);
            }

            var usedClasses = new HashSet<string>();
            if (ClassLowering.MayHaveClasses(current))
            {
                current = ClassLowering.Run(current, diagnostics, GetMetadataReferences(), usedClasses, classNumbers);
                SourceMap.RegisterTrees(current);
            }

            // 汎用クラスの中で出たインタフェースのエラーは、UdonSharpBehaviour から使われるクラスのものだけ残す
            // （使われないクラスは書き換えず、検査もしない。#49 と同じ）
            diagnostics.DropUnusedOwners(usedClasses);
            current = RestoreUnusedClasses(current, rewrittenClasses, usedClasses);

            // 3) List / Dictionary → DataList / DataDictionary（#56）。汎用クラスのコピーの中も含め、UdonSharpBehaviour の中を書き換える
            if (CollectionLowering.MayHaveCollections(current))
            {
                current = CollectionLowering.Run(current, diagnostics, GetMetadataReferences());
                SourceMap.RegisterTrees(current);
            }

            // エラーがあれば U# はこの直後でコンパイルを止めるので、書き換えた tree は使われない。
            // 元の tree のままにしておくと、U# が診断の位置からファイル名を引ける（binding.tree と照合するため）
            if (diagnostics.HasErrors && context != null)
            {
                diagnostics.Report(context, original);
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                if (ReferenceEquals(current[i], original[i]))
                    continue;

                object binding = bindings.GetValue(i);
                string newText = current[i].GetText().ToString();
                treeField.SetValue(binding, current[i]);
                sourceTextField?.SetValue(binding, newText);

                if (DumpEnabled)
                    Dump(binding, newText);
            }

            diagnostics.Report(context, current);
        }

        /// <summary>インタフェースの段で中身を書き換えた汎用クラス（使われなければ元に戻す）</summary>
        private sealed class RewrittenClass
        {
            public int TreeIndex;
            public string Tag;
            public ClassDeclarationSyntax Original;
        }

        /// <summary>
        /// UdonSharpBehaviour から使われなかった汎用クラスの宣言を、書き換え前に戻す。
        /// 使われないクラスは汎用クラスの段で書き換えないので、インタフェースの段の書き換え（型を object にする、など）だけが残り、
        /// 書き換えられなかった書き方（?. など）が C# として通らなくなるため。インタフェースの宣言は残しているので、元のままで通る
        /// </summary>
        private static List<SyntaxTree> RestoreUnusedClasses(List<SyntaxTree> trees, List<RewrittenClass> rewritten, HashSet<string> used)
        {
            var result = new List<SyntaxTree>(trees);
            foreach (var group in rewritten.Where(c => !used.Contains(c.Tag)).GroupBy(c => c.TreeIndex))
            {
                SyntaxTree tree = result[group.Key];
                SyntaxNode root = tree.GetRoot();
                var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
                foreach (RewrittenClass c in group)
                {
                    int line = c.Original.GetLocation().GetLineSpan().StartLinePosition.Line;
                    ClassDeclarationSyntax target = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(d =>
                        d.Identifier.ValueText == c.Original.Identifier.ValueText && d.GetLocation().GetLineSpan().StartLinePosition.Line == line);
                    if (target != null)
                        replacements[target] = c.Original;
                }
                if (replacements.Count == 0)
                    continue;
                SyntaxNode newRoot = root.ReplaceNodes(replacements.Keys, (o, _) => replacements[o]);
                result[group.Key] = CSharpSyntaxTree.ParseText(newRoot.ToFullString(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8);
            }
            SourceMap.RegisterTrees(result);
            return result;
        }

        /// <summary>
        /// 汎用クラスの型番号（インスタンスの [1]。#65）を振る。呼び出しヘルパーは型番号の switch で実装クラスへ振り分けるので、
        /// 同じヘルパーで振り分けるクラス（groups の 1 つ）の中では重ならないようにする。
        /// クラスに番号は 1 つだけで、完全名の順に、重ならない最小の番号を選ぶ（複数のグループに入るクラスも 1 つ）。
        /// 番号が小さいほど U# の switch がジャンプテーブルになりやすい（0〜256）。どこのグループにも入らないクラスは 0（表に入れない）
        /// </summary>
        private static void AssignClassNumbers(IEnumerable<List<INamedTypeSymbol>> groups, Dictionary<string, int> numbers)
        {
            var groupsOf = new Dictionary<string, List<List<INamedTypeSymbol>>>();
            foreach (List<INamedTypeSymbol> group in groups)
                foreach (INamedTypeSymbol c in group)
                {
                    string tag = ClassLowering.TagOf(c);
                    if (!groupsOf.TryGetValue(tag, out var list))
                        groupsOf[tag] = list = new List<List<INamedTypeSymbol>>();
                    list.Add(group);
                }

            foreach (string tag in groupsOf.Keys.OrderBy(t => t, StringComparer.Ordinal))
            {
                var taken = new HashSet<int>();
                foreach (List<INamedTypeSymbol> group in groupsOf[tag])
                    foreach (INamedTypeSymbol other in group)
                        if (numbers.TryGetValue(ClassLowering.TagOf(other), out int n))
                            taken.Add(n);
                int number = 0;
                while (taken.Contains(number))
                    number++;
                numbers[tag] = number;
            }
        }

        /// <summary>インタフェースの書き換え。書き換えた tree の一覧を返す（変わらない tree はそのまま）</summary>
        /// <param name="classNumbers">汎用クラスの型番号（型タグ → 番号）を入れて返す（#65）</param>
        private static List<SyntaxTree> LowerInterfaces(List<SyntaxTree> trees, LoweringDiagnostics diagnostics, List<RewrittenClass> rewrittenClasses,
            Dictionary<string, int> classNumbers)
        {
            var compilation = CSharpCompilation.Create(
                "UdonSharpPlus_InterfacePrepass",
                trees,
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var interfaces = CollectInterfaces(compilation, trees, diagnostics);
            if (interfaces.Count == 0)
                return trees;

            CollectImplementers(compilation, trees, interfaces, diagnostics);

            // 呼び出しの補助メソッドは、同じシグネチャなら別のインタフェースのメソッドとも共有するので、
            // 汎用クラスへの振り分けは、そのシグネチャのメソッドを持つインタフェースを実装する全クラスにする
            var classImpls = new Dictionary<string, List<INamedTypeSymbol>>();
            foreach (InterfaceInfo info in interfaces.Values)
                foreach (MethodInfo m in info.Methods)
                {
                    if (!classImpls.TryGetValue(m.Signature, out var list))
                        classImpls[m.Signature] = list = new List<INamedTypeSymbol>();
                    foreach (INamedTypeSymbol c in info.ImplClasses)
                        if (!list.Contains(c, SymbolEqualityComparer.Default))
                            list.Add(c);
                }
            foreach (var list in classImpls.Values)
                list.Sort((a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));

            AssignClassNumbers(classImpls.Values, classNumbers);

            var result = new List<SyntaxTree>(trees.Count);
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                var rewriter = new Rewriter(model, interfaces, classImpls, classNumbers, diagnostics);
                SyntaxNode newRoot = rewriter.Visit(tree.GetRoot());
                foreach (var (tag, decl) in rewriter.RewrittenClasses)
                    rewrittenClasses.Add(new RewrittenClass { TreeIndex = result.Count, Tag = tag, Original = decl });
                result.Add(rewriter.Changed
                    ? CSharpSyntaxTree.ParseText(newRoot.ToFullString(), (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8)
                    : tree);
            }
            return result;
        }

        /// <summary>本パッケージが出すエラー・警告</summary>
        internal sealed class LoweringDiagnostics
        {
            private const string Prefix = "[UdonSharpPlus] ";

            private readonly List<(bool IsError, Location Location, string Message, string Owner)> _entries =
                new List<(bool IsError, Location Location, string Message, string Owner)>();

            public bool HasErrors => _entries.Any(e => e.IsError);

            /// <param name="owner">汎用クラスの中で出たものなら、そのクラスの名前（ClassLowering.ClassInfo.Tag と同じ表記）</param>
            public void Error(Location location, string message, string owner = null) => _entries.Add((true, location, message, owner));

            public void Warning(Location location, string message) => _entries.Add((false, location, message, null));

            /// <summary>汎用クラスの中で出たもののうち、使われないクラスのものを捨てる</summary>
            public void DropUnusedOwners(HashSet<string> usedClasses) =>
                _entries.RemoveAll(e => e.Owner != null && !usedClasses.Contains(e.Owner));

            /// <param name="context">U# の CompilationContext（null ならログに出す）</param>
            /// <param name="targets">U# に渡す tree（binding と同じ並び）。位置を同じ binding のこの tree に移す</param>
            public void Report(object context, IReadOnlyList<SyntaxTree> targets)
            {
                System.Reflection.MethodInfo add = null;
                Type severityType = null;
                if (context != null && _entries.Count > 0)
                {
                    severityType = UdonSharpInternals.DiagnosticSeverity;
                    add = UdonSharpInternals.AddDiagnosticWithLocation;
                    if (add == null)
                        Debug.LogWarning(Prefix + "CompilationContext.AddDiagnostic が見つからないため、エラー・警告をログに出します。UdonSharp の内部が変わった可能性があります");
                }

                // 警告を先、エラーを後に（従来のログと同じ順）。同じ位置・内容の重複は 1 つにする
                var seen = new HashSet<string>();
                foreach (var entry in _entries.OrderBy(e => e.IsError))
                {
                    // 汎用クラスのコピーの中なら、元のクラスの位置に戻す（#52）
                    SourceMap.Entry source = SourceMap.Find(entry.Location);
                    Location location = source != null && source.TreeIndex >= 0 && source.TreeIndex < targets.Count
                        ? SourceMap.ToLocation(source, targets[source.TreeIndex]) ?? Remap(entry.Location, targets)
                        : Remap(entry.Location, targets);
                    string message = source != null && !ReferenceEquals(location, entry.Location)
                        ? entry.Message + SourceMap.Describe(source)
                        : entry.Message;
                    // ジェネリッククラスのコピーが複数あると、同じ位置に同じエラーが「どのコピーの中か」だけ違って並ぶので、
                    // 末尾の説明を除いた内容で比べて 1 つにする（#81）
                    if (!seen.Add($"{entry.IsError}|{At(location, entry.Message)}"))
                        continue;

                    if (add != null)
                    {
                        object severity = Enum.Parse(severityType, entry.IsError ? "Error" : "Warning");
                        add.Invoke(context, new[] { severity, location, Prefix + message });
                    }
                    else if (entry.IsError)
                        Debug.LogError(Prefix + At(location, message));
                    else
                        Debug.LogWarning(Prefix + At(location, message));
                }
            }

            /// <summary>
            /// 書き換えでは行数を変えないので、同じ binding の U# に渡す tree の、同じ行・列の位置にする
            /// （途中の書き換えでできた tree の位置も、元の tree の位置も、この方法で移せる。列は書き換えでずれることがある）。
            /// tree はファイルパスではなく binding の番号で対応付ける（U# の tree はパスを持たないため）
            /// </summary>
            private static Location Remap(Location location, IReadOnlyList<SyntaxTree> targets)
            {
                int index = location != null && location.IsInSource ? SourceMap.TreeIndex(location.SourceTree) : -1;
                if (index < 0 || index >= targets.Count || ReferenceEquals(location.SourceTree, targets[index]))
                    return location;
                SyntaxTree target = targets[index];

                var start = location.GetLineSpan().StartLinePosition;
                var lines = target.GetText().Lines;
                if (start.Line >= lines.Count)
                    return location;

                var line = lines[start.Line];
                int position = line.Start + Math.Min(start.Character, line.End - line.Start);
                return Location.Create(target, new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));
            }
        }

        /// <summary>"path(line,col): message" の形にする（U# や Roslyn の診断と同じ書式）</summary>
        internal static string At(Location location, string message)
        {
            if (location == null || !location.IsInSource)
                return message;
            var span = location.GetLineSpan();
            return $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {message}";
        }

        private static Dictionary<INamedTypeSymbol, InterfaceInfo> CollectInterfaces(CSharpCompilation compilation, List<SyntaxTree> trees, LoweringDiagnostics diagnostics)
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
                // Udon 上の名前は元のメソッド名なので、同じ名前で引数の違うメソッドは置けない（継承したインタフェースを含む）
                var all = new[] { info.Symbol }.Concat(info.Symbol.AllInterfaces)
                    .SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
                    .Where(m => m.MethodKind == MethodKind.Ordinary)
                    .ToList();
                foreach (var group in all.GroupBy(m => m.Name).Where(g => g.Count() > 1))
                {
                    IMethodSymbol own = group.FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(m.ContainingType, info.Symbol));
                    if (own != null)
                        diagnostics.Error(own.Locations.FirstOrDefault(),
                            $"'{info.Symbol.Name}.{group.Key}' : インタフェースのメソッドのオーバーロードは未対応です（Udon 上の名前が元のメソッド名になるため、名前を変えてください）");
                }

                foreach (ISymbol member in info.Symbol.GetMembers())
                {
                    if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                        info.Methods.Add(new MethodInfo { Symbol = m, Signature = InterfaceNaming.SignatureName(m, erase) });
                    else if (!(member is IMethodSymbol))
                        diagnostics.Warning(member.Locations.FirstOrDefault(), $"'{member}' : インタフェースはメソッドのみ対応しています（プロパティ/イベントは未対応）");
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
            Dictionary<INamedTypeSymbol, InterfaceInfo> interfaces, LoweringDiagnostics diagnostics)
        {
            var fullTypeFormat = (SymbolDisplayFormat)UdonSharpInternals.FullTypeFormat.GetValue(null);
            var getTypeId = UdonSharpInternals.GetTypeId;

            Func<INamedTypeSymbol, bool> erase = t => interfaces.ContainsKey(t.OriginalDefinition);
            var keys = new HashSet<string>();
            var reported = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(decl) is INamedTypeSymbol cls))
                        continue;

                    if (!IsUdonSharpBehaviour(cls))
                    {
                        if (ClassLowering.IsPlainClass(cls))
                            CollectClassImplementer(cls, interfaces, diagnostics, reported);
                        continue;
                    }

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
                                diagnostics.Error(impl.Locations.FirstOrDefault(), $"'{cls.Name}.{m.Symbol.Name}' : 明示的インタフェース実装は未対応です。public メソッドとして実装してください");
                                continue;
                            }

                            for (IMethodSymbol cur = impl; cur != null; cur = cur.OverriddenMethod)
                                keys.Add(InterfaceNaming.Key(cur, erase));

                            CheckNoOverload(cls, impl, m.Symbol, diagnostics, reported);
                        }
                    }
                }
            }

            InterfaceNaming.ImplementationKeys = keys;
        }

        /// <summary>
        /// インタフェースを実装する汎用クラス（#50）。呼び出しは普通の C# の呼び出しにする（Udon 上の名前は関係ない）ので、
        /// オーバーロードの制約はない。明示的インタフェース実装だけは、((Bullet)x).M(a) で呼べないので未対応
        /// </summary>
        private static void CollectClassImplementer(INamedTypeSymbol cls, Dictionary<INamedTypeSymbol, InterfaceInfo> interfaces,
            LoweringDiagnostics diagnostics, HashSet<ISymbol> reported)
        {
            foreach (INamedTypeSymbol implemented in cls.AllInterfaces)
            {
                if (!interfaces.TryGetValue(implemented.OriginalDefinition, out var info))
                    continue;

                if (!cls.IsAbstract && !cls.IsStatic && !info.ImplClasses.Contains(cls, SymbolEqualityComparer.Default))
                    info.ImplClasses.Add(cls);

                foreach (MethodInfo m in info.Methods)
                {
                    if (cls.FindImplementationForInterfaceMember(m.Symbol) is IMethodSymbol impl
                        && impl.ExplicitInterfaceImplementations.Length > 0 && reported.Add(impl))
                        diagnostics.Error(impl.Locations.FirstOrDefault(), $"'{cls.Name}.{m.Symbol.Name}' : 明示的インタフェース実装は未対応です。public メソッドとして実装してください",
                            ClassLowering.TagOf(cls));
                }
            }
        }

        /// <summary>
        /// インタフェース実装メソッドは Udon 上で元の名前になるので、素の U# の [NetworkCallable] と同じく、
        /// 同じクラス（基底の UdonSharpBehaviour を含む）に同じ名前のメソッドを置けない（引数なしでも）。
        /// オーバーライドの関係にあるものは同じメソッドとみなす。
        /// </summary>
        private static void CheckNoOverload(INamedTypeSymbol cls, IMethodSymbol impl, IMethodSymbol interfaceMethod,
            LoweringDiagnostics diagnostics, HashSet<ISymbol> reported)
        {
            IMethodSymbol root = impl;
            while (root.OverriddenMethod != null)
                root = root.OverriddenMethod;

            for (INamedTypeSymbol t = cls; t != null && t.ToDisplayString() != "UdonSharp.UdonSharpBehaviour"; t = t.BaseType)
            {
                foreach (IMethodSymbol other in t.GetMembers(impl.Name).OfType<IMethodSymbol>())
                {
                    IMethodSymbol otherRoot = other;
                    while (otherRoot.OverriddenMethod != null)
                        otherRoot = otherRoot.OverriddenMethod;
                    if (SymbolEqualityComparer.Default.Equals(otherRoot, root) || !reported.Add(other))
                        continue;

                    diagnostics.Error(other.Locations.FirstOrDefault(),
                        $"'{cls.Name}.{impl.Name}' : インタフェース（{interfaceMethod.ContainingType.Name}）の実装メソッドと同じ名前のメソッドは置けません" +
                        "（Udon 上の名前が元のメソッド名になるため。[NetworkCallable] と同じ制約です）");
                }
            }
        }

        internal static bool IsUdonSharpBehaviour(INamedTypeSymbol type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.ToDisplayString() == "UdonSharp.UdonSharpBehaviour")
                    return true;
            return false;
        }

        /// <summary>
        /// 値が使われない位置に書かれた式か（代入・インクリメントを文に書き換えてよいか）。
        /// 式文、for の増分、戻り値が void のラムダの式の本体（ForEach(x => a.b = x) など。C# では文と同じ意味。#73）
        /// </summary>
        internal static bool IsStatementExpression(ExpressionSyntax node, SemanticModel model)
        {
            switch (node.Parent)
            {
                case ExpressionStatementSyntax _:
                    return true;
                case ForStatementSyntax f:
                    return f.Incrementors.Contains(node);
                case LambdaExpressionSyntax lambda when lambda.Body == node:
                    return model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol d && d.DelegateInvokeMethod != null && d.DelegateInvokeMethod.ReturnsVoid;
                // 式形式のメンバーで、値を返さないもの（コンストラクタ、set、void のメソッド・ローカル関数）の本体（#79）
                case ArrowExpressionClauseSyntax arrow:
                    switch (arrow.Parent)
                    {
                        case ConstructorDeclarationSyntax _:
                        case DestructorDeclarationSyntax _:
                            return true;
                        case AccessorDeclarationSyntax accessor:
                            return !accessor.IsKind(SyntaxKind.GetAccessorDeclaration);
                        case MethodDeclarationSyntax method:
                            return model.GetDeclaredSymbol(method) is IMethodSymbol m && m.ReturnsVoid;
                        case LocalFunctionStatementSyntax local:
                            return model.GetDeclaredSymbol(local) is IMethodSymbol lm && lm.ReturnsVoid;
                        default:
                            return false;
                    }
                default:
                    return false;
            }
        }

        /// <summary>
        /// UdonSharp 自身のライブラリ（UdonSharp.Lib.Internal.GetComponentShim など）の型か。
        /// ユーザーのスクリプトと一緒にコンパイル対象に入るが、UdonSharp が名前で探して呼ぶので、書き換えてはいけない（#78）
        /// </summary>
        internal static bool IsUdonSharpLibrary(ISymbol symbol)
        {
            string ns = symbol.ContainingNamespace?.ToDisplayString();
            return ns == "UdonSharp.Lib" || ns != null && ns.StartsWith("UdonSharp.Lib.", StringComparison.Ordinal);
        }

        private static readonly HashSet<string> GetComponentNames = new HashSet<string>
        {
            "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren", "GetComponentInParent", "GetComponentsInParent",
        };

        /// <summary>
        /// Component・GameObject の GetComponent 系（型引数で取得するもの）なら、その型引数。違えば null。
        /// インタフェース・普通のクラスを渡すと、書き換えで GetComponent&lt;object&gt; などになり、実行時に例外で止まる（#76）
        /// </summary>
        internal static ITypeSymbol GetComponentTypeArgument(IMethodSymbol method)
        {
            if (!method.IsGenericMethod || method.TypeArguments.Length != 1 || !GetComponentNames.Contains(method.Name))
                return null;
            string container = method.ContainingType?.ToDisplayString();
            return container == "UnityEngine.Component" || container == "UnityEngine.GameObject" || container == "UdonSharp.UdonSharpBehaviour"
                ? method.TypeArguments[0]
                : null;
        }

        internal static string GetComponentError(IMethodSymbol method, ITypeSymbol type) =>
            $"'{method.Name}<{type.Name}>' : インタフェース・普通のクラスは GetComponent 系で取得できません（実行時に例外になります）。" +
            "GetComponent<UdonSharpBehaviour>() などで受けて、is / キャストしてください";

        private static IEnumerable<MetadataReference> GetMetadataReferences()
        {
            return (IEnumerable<MetadataReference>)UdonSharpInternals.GetMetadataReferences.Invoke(null, null);
        }

        private static void Dump(object binding, string text)
        {
            try
            {
                string filePath = (string)UdonSharpInternals.BindingFilePath.GetValue(binding);
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
            private readonly Dictionary<string, List<INamedTypeSymbol>> _classImpls;   // シグネチャ → 実装する汎用クラス
            private readonly Dictionary<string, int> _classNumbers;                    // 型タグ → 型番号（#65）
            private readonly LoweringDiagnostics _diagnostics;
            private readonly Func<INamedTypeSymbol, bool> _erase;
            private readonly Stack<ClassState> _states = new Stack<ClassState>();

            public bool Changed { get; private set; }

            /// <summary>中身を書き換えた汎用クラス（型タグ、元の宣言）</summary>
            public readonly List<(string Tag, ClassDeclarationSyntax Original)> RewrittenClasses = new List<(string, ClassDeclarationSyntax)>();

            public Rewriter(SemanticModel model, Dictionary<INamedTypeSymbol, InterfaceInfo> interfaces,
                Dictionary<string, List<INamedTypeSymbol>> classImpls, Dictionary<string, int> classNumbers, LoweringDiagnostics diagnostics)
            {
                _model = model;
                _interfaces = interfaces;
                _classImpls = classImpls;
                _classNumbers = classNumbers;
                _diagnostics = diagnostics;
                _erase = t => interfaces.ContainsKey(t.OriginalDefinition);
            }

            private readonly Stack<string> _owners = new Stack<string>();   // 書き換え中の汎用クラス（UdonSharpBehaviour の中なら null）

            private void Error(SyntaxNode node, string message) =>
                _diagnostics.Error(node.GetLocation(), message, _owners.Count > 0 ? _owners.Peek() : null);

            private InterfaceInfo AsTarget(ITypeSymbol type)
            {
                return type is INamedTypeSymbol named && _interfaces.TryGetValue(named.OriginalDefinition, out var info) ? info : null;
            }

            private string TypeName(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array)
                    return TypeName(array.ElementType) + "[]";
                if (AsTarget(type) != null)
                    return ErasedType;
                // List<IShape> → List<object>（型引数の中も置き換える。#56）
                if (type is INamedTypeSymbol named && CollectionLowering.GenericText(named, TypeName) is string generic)
                    return generic;
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            // インタフェース宣言自体には手を入れない
            public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => node;

            // using エイリアス（using IDmg = Game.IDamageable;）の右側は名前しか書けないので、object に置き換えない。
            // エイリアスを使う側（IDmg x）は、名前が指すインタフェースとして置き換わる（#79）
            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) => node;

            // 明示的インタフェース実装（void IFoo.M()）の IFoo は、名前の位置なので object に置き換えられない（エラーは別に出す）
            public override SyntaxNode VisitExplicitInterfaceSpecifier(ExplicitInterfaceSpecifierSyntax node) => node;

            // ---- フィールド（シリアライズ・同期は未対応。#72） ---------------------

            /// <summary>
            /// UdonSharpBehaviour のインタフェース型（とその配列）のフィールドに [SerializeField] / [UdonSynced] / [SerializeReference] があればエラーにする。
            /// object になるので Unity はシリアライズせず（Inspector に出ない）、同期もできないため。public は、コードで代入して使う分には問題ないのでそのまま
            /// （普通のクラス型のフィールドと同じ扱い）
            /// </summary>
            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                ITypeSymbol type = _model.GetTypeInfo(node.Declaration.Type).Type;
                while (type is IArrayTypeSymbol array)
                    type = array.ElementType;
                if (AsTarget(type) != null
                    && _model.GetDeclaredSymbol(node.Declaration.Variables[0]) is IFieldSymbol field && IsUdonSharpBehaviour(field.ContainingType)
                    && node.AttributeLists.SelectMany(l => l.Attributes).Any(a => IsSerializationAttribute(_model.GetSymbolInfo(a).Symbol?.ContainingType)))
                {
                    foreach (VariableDeclaratorSyntax v in node.Declaration.Variables)
                        Error(v, $"'{v.Identifier.ValueText}' : インタフェース型のフィールドは、シリアライズ・同期できません（[SerializeField] / [UdonSynced] は付けられません）。Inspector で設定するなら UdonSharpBehaviour 型で受けて、キャストしてください");
                }
                return base.VisitFieldDeclaration(node);
            }

            private static bool IsSerializationAttribute(INamedTypeSymbol attribute)
            {
                string name = attribute?.ToDisplayString();
                return name == "UnityEngine.SerializeField" || name == "UnityEngine.SerializeReference" || name == "UdonSharp.UdonSyncedAttribute";
            }

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
                return SyntaxFactory.ParseTypeName(ErasedType).WithTriviaFrom(node);
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
                if (IsNameOf(node))
                    return RewriteNameOf(node);

                if (!(_model.GetSymbolInfo(node).Symbol is IMethodSymbol method))
                    return base.VisitInvocationExpression(node);

                if (GetComponentTypeArgument(method) is ITypeSymbol component && AsTarget(component) != null)
                {
                    Error(node, GetComponentError(method, component));
                    return base.VisitInvocationExpression(node);
                }

                if (AsTarget(method.ContainingType) == null)
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

            // ---- nameof --------------------------------------------------------

            private bool IsNameOf(ExpressionSyntax expr)
            {
                return expr is InvocationExpressionSyntax inv
                    && inv.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "nameof"
                    && inv.ArgumentList.Arguments.Count == 1
                    && _model.GetConstantValue(inv).HasValue;   // 同名のユーザーメソッドではなく nameof 演算子
            }

            /// <summary>
            /// 対象インタフェースを含む nameof(...) は、元の結果の文字列リテラルにする
            /// （型の置き換えで nameof(UdonSharpBehaviour.M) になり壊れるのを防ぐ）。
            /// メソッド名は Udon 上でも元の名前なので、イベント名として渡してもそのまま届く。
            /// </summary>
            private SyntaxNode RewriteNameOf(InvocationExpressionSyntax node)
            {
                ExpressionSyntax target = node.ArgumentList.Arguments[0].Expression;
                bool referencesInterface = target.DescendantNodesAndSelf().OfType<NameSyntax>()
                    .Any(n => _model.GetSymbolInfo(n).Symbol is INamedTypeSymbol t && AsTarget(t) != null);
                if (!referencesInterface)
                    return base.VisitInvocationExpression(node);

                Changed = true;
                return StringLiteral((string)_model.GetConstantValue(node).Value).WithTriviaFrom(node);
            }

            private static LiteralExpressionSyntax StringLiteral(string text) =>
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(text));

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

                // 書き換えるのは UdonSharpBehaviour と汎用クラス（#49）の中身。汎用クラスはこの後の段で、
                // 書き換え後の中身が使う側にコピーされる。それ以外のクラス（UdonSharp と関係ないもの）には手を入れない
                bool plainClass = classSymbol != null && ClassLowering.IsPlainClass(classSymbol);
                bool rewriteMembers = classSymbol == null || IsUdonSharpBehaviour(classSymbol) || plainClass;

                _states.Push(state);
                _owners.Push(plainClass ? ClassLowering.TagOf(classSymbol) : null);
                var visited = rewriteMembers ? (ClassDeclarationSyntax)base.VisitClassDeclaration(node) : node;
                _owners.Pop();
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
                        if (!IsUdonSharpBehaviour(classSymbol) && !plainClass)
                            Error(node, $"'{classSymbol.Name}' : UdonSharpBehaviour と普通のクラス以外でのインタフェース実装は未対応です");

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
                    members.Add($"private static {ErasedType} {i.AsHelper}(object __o) {{ return {i.IsHelper}(__o) ? __o : null; }}");

                // 生成メンバーは閉じ括弧 '}' と同じ行に置き、行番号を一切ずらさない
                if (members.Count > 0)
                    visited = GeneratedMembers.AppendOnClosingLine(visited, members);

                if (plainClass && visited != node)
                    RewrittenClasses.Add((ClassLowering.TagOf(classSymbol), node));
                return visited;
            }

            private string BuildCallHelper(MethodInfo m)
            {
                var ps = m.Symbol.Parameters;
                var sb = new StringBuilder();
                string ret = m.HasReturn ? TypeName(m.Symbol.ReturnType) : "void";

                sb.Append($"private static {ret} {m.CallHelper}({ErasedType} __t");
                for (int i = 0; i < ps.Length; i++)
                {
                    sb.Append($", {TypeName(ps[i].Type)} {ps[i].Name}");
                    if (ps[i].DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ParameterSyntax p && p.Default != null)
                        sb.Append(' ').Append(p.Default.WithoutTrivia().ToFullString());
                }

                sb.Append(") { ");

                // 汎用クラス（#50）：普通の C# で呼ぶ。この後の段で、そのクラスのメソッドのコピーの呼び出しになる
                string args = string.Join(", ", ps.Select(p => p.Name));
                if (_classImpls.TryGetValue(m.Signature, out var classes) && UseSwitch(classes))
                {
                    // 実装クラスが多いとき（#65）：汎用クラスのインスタンスかを 1 回だけ確かめ、型番号（[1]）の switch で振り分ける。
                    // U# は case が 0〜256 の定数で 4 個以上の switch をジャンプテーブル（JUMP_INDIRECT）にするので、クラスの数によらない
                    sb.Append("if (__t != null && __t.GetType().Equals(typeof(object[]))) { object[] __a = (object[])__t; ");
                    sb.Append("if (__a.Length > 1 && __a[0] != null && __a[0].GetType().Equals(typeof(string))) { switch ((int)__a[1]) { ");
                    foreach (INamedTypeSymbol c in classes)
                    {
                        string name = c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        string call = $"(({name})__t).{m.Symbol.Name}({args})";
                        sb.Append($"case {_classNumbers[ClassLowering.TagOf(c)]}: ");
                        sb.Append(m.HasReturn ? $"return {call}; " : $"{call}; return; ");
                    }
                    sb.Append("} } } ");
                }
                else if (classes != null)
                {
                    foreach (INamedTypeSymbol c in classes)
                    {
                        string name = c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        string call = $"(({name})__t).{m.Symbol.Name}({args})";
                        sb.Append(m.HasReturn
                            ? $"if (__t is {name}) return {call}; "
                            : $"if (__t is {name}) {{ {call}; return; }} ");
                    }
                }

                // UdonSharpBehaviour：プログラム変数とイベントで呼ぶ
                sb.Append($"{UsbType} __u = ({UsbType})__t; ");
                for (int i = 0; i < ps.Length; i++)
                    sb.Append($"__u.SetProgramVariable(\"{m.ParamField(i)}\", {ps[i].Name}); ");
                sb.Append($"__u.SendCustomEvent(\"{m.Export}\"); ");
                if (m.HasReturn)
                    sb.Append($"return ({ret})__u.GetProgramVariable(\"{m.RetField}\"); ");
                sb.Append('}');
                return sb.ToString();
            }

            /// <summary>
            /// 型番号の switch で振り分けるか（#65）。U# がジャンプテーブルにする条件（case が 4 個以上、0〜256）を満たすときだけ。
            /// 満たさなければ is を並べる形のまま（3 個以下なら比べる回数も少ない）
            /// </summary>
            private bool UseSwitch(List<INamedTypeSymbol> classes) =>
                classes.Count >= 4 && classes.All(c => _classNumbers.TryGetValue(ClassLowering.TagOf(c), out int n) && n <= 256);

            // 汎用クラス（#50）は is で見る（この後の段で、型タグを見る判定になる）。
            // UdonSharpBehaviour は、U# が is 演算子非対応のため GetType() で UdonBehaviour か確認し、
            // U# の型ID (__refl_typeid) を実装クラス一覧と照合
            private static string BuildIsHelper(InterfaceInfo i)
            {
                var sb = new StringBuilder();
                sb.Append($"private static bool {i.IsHelper}(object __o) {{ if (__o == null) return false; ");
                foreach (INamedTypeSymbol c in i.ImplClasses.OrderBy(c => c.ToDisplayString(), StringComparer.Ordinal))
                    sb.Append($"if (__o is {c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}) return true; ");
                if (i.ImplTypeIds.Count == 0)
                    return sb.Append("return false; }").ToString();

                string cond = string.Join(" || ", i.ImplTypeIds.Select(id => $"__id == {id}L"));
                sb.Append($"if (!__o.GetType().Equals(typeof(global::VRC.Udon.UdonBehaviour))) return false; object __v = (({UsbType})__o).GetProgramVariable(\"__refl_typeid\"); if (__v == null) return false; long __id = (long)__v; return {cond}; }}");
                return sb.ToString();
            }
        }
    }
}
