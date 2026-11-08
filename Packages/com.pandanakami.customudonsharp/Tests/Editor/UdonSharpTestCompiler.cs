using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEditor;
using UnityEngine;

namespace CustomUdonSharp.Tests
{
    /// <summary>U# のソース 1 ファイル分（ファイル名はクラス名と同じにする）</summary>
    internal readonly struct TestSource
    {
        public readonly string FileName;
        public readonly string Text;

        public TestSource(string fileName, string text)
        {
            FileName = fileName;
            Text = text;
        }
    }

    internal sealed class NetworkEventInfo
    {
        public string Name;
        public List<(string Name, string Type)> Parameters = new List<(string, string)>();

        public override string ToString() => $"{Name}({string.Join(", ", Parameters.Select(p => $"{p.Name}:{p.Type}"))})";
    }

    /// <summary>1 つの UdonSharpBehaviour から生成された Udon プログラム</summary>
    internal sealed class ProgramResult
    {
        public string TypeName;

        /// <summary>UdonAssembly（AssemblyModule.BuildUasmStr の結果）</summary>
        public string Uasm;

        /// <summary>ヒープの初期値（定数・リフレクション用の値など）。シンボル名 → 値</summary>
        public SortedDictionary<string, string> HeapDefaults = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>[NetworkCallable] の通信用メタデータ</summary>
        public List<NetworkEventInfo> NetworkEvents = new List<NetworkEventInfo>();

        /// <summary>コードのエクスポート（エントリポイント）</summary>
        public IEnumerable<string> EntryPoints => Exports(".code_start", ".code_end");

        /// <summary>データのエクスポート（public フィールドなど）</summary>
        public IEnumerable<string> ExportedSymbols => Exports(".data_start", ".data_end");

        private IEnumerable<string> Exports(string begin, string end)
        {
            bool inside = false;
            foreach (string raw in Uasm.Split('\n'))
            {
                string line = raw.Trim();
                if (line == begin) inside = true;
                else if (line == end) inside = false;
                else if (inside && line.StartsWith(".export ", StringComparison.Ordinal))
                    yield return line.Substring(".export ".Length).Trim();
            }
        }
    }

    internal sealed class CompileResult
    {
        public readonly List<ProgramResult> Programs = new List<ProgramResult>();

        /// <summary>U# とRoslyn の診断（"Error: path(line,col): message" 形式、並びは安定化済み）</summary>
        public readonly List<string> Diagnostics = new List<string>();

        /// <summary>本パッケージが出したログ（"Warning: [CustomUdonSharp] ..." 形式）</summary>
        public readonly List<string> Logs = new List<string>();

        public bool HasErrors => Diagnostics.Any(d => d.StartsWith("Error", StringComparison.Ordinal));

        public ProgramResult this[string typeName] =>
            Programs.FirstOrDefault(p => p.TypeName == typeName || p.TypeName.EndsWith("." + typeName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"program '{typeName}' not found. programs: {string.Join(", ", Programs.Select(p => p.TypeName))}");
    }

    /// <summary>
    /// U# のソースをメモリ上でコンパイルし、UdonAssembly・通信用メタデータ・診断を返す（Redmine #42 の手順）。
    ///
    /// UdonSharpCompilerV1.Compile と同じ流れをリフレクションでなぞり、次の 2 か所だけ置き換える。
    ///   - ファイル読み込み（LoadSyntaxTreesAndCreateModules）の代わりに、ソース文字列から ModuleBinding を作る。
    ///     本パッケージの Postfix は通らないので、InterfaceLowering.Process を直接呼ぶ
    ///   - EmitAllPrograms（CurrentJob と programAsset に依存）の代わりに、Emit と UdonAssembly の生成だけ行う
    /// U# はユーザー定義型を Unity のコンパイル済みアセンブリから探すので、Roslyn で作ったアセンブリを読み込み、
    /// CompilerUdonInterface._udonSharpAssemblies に一時的に追加する。
    ///
    /// U# の内部 API に依存しているので、SDK 更新で名前が変わるとここが例外を出す（それ自体が更新の検知になる）。
    /// </summary>
    internal static class UdonSharpTestCompiler
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private const string SourceDir = "Assets/CustomUdonSharpTests/";

        // UdonSharp.Lib.Internal.CompilerConstants と同じ値
        private const string UsbTypeIDHeapKey = "__refl_typeid";
        private const string UsbTypeNameHeapKey = "__refl_typename";
        private const string UsbTypeIDArrayHeapKey = "__refl_typeids";

        private static readonly object CompileLock = new object();
        private static int _assemblyCounter;

        private static Assembly _editorAssembly;
        private static Assembly _runtimeAssembly;

        private static Assembly EditorAssembly => _editorAssembly ?? (_editorAssembly = FindAssembly("UdonSharp.Editor"));
        private static Assembly RuntimeAssembly => _runtimeAssembly ?? (_runtimeAssembly = FindAssembly("UdonSharp.Runtime"));

        private static Assembly FindAssembly(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
            ?? throw new InvalidOperationException($"assembly '{name}' not found");

        private static Type EditorType(string fullName) =>
            EditorAssembly.GetType(fullName) ?? throw new MissingMemberException($"type '{fullName}' not found. UdonSharp internals may have changed.");

        /// <summary>parameters を省略すると名前だけで探す（オーバーロードがあるときは型を渡す。引数なしは Type.EmptyTypes）</summary>
        private static MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            MethodInfo m = parameters == null ? type.GetMethod(name, All) : type.GetMethod(name, All, null, parameters, null);
            return m ?? throw new MissingMemberException(type.FullName, name);
        }

        private static MethodInfo Method(Type type, string name, Type parameter, params Type[] more) =>
            Method(type, name, new[] { parameter }.Concat(more).ToArray());

        private static PropertyInfo Property(Type type, string name) =>
            type.GetProperty(name, All) ?? throw new MissingMemberException(type.FullName, name);

        private static FieldInfo Field(Type type, string name) =>
            type.GetField(name, All) ?? throw new MissingMemberException(type.FullName, name);

        public static CompileResult Compile(params TestSource[] sources)
        {
            lock (CompileLock)
            {
                bool dump = InterfaceLowering.DumpEnabled;
                InterfaceLowering.DumpEnabled = false;
                try
                {
                    return CompileInternal(sources);
                }
                finally
                {
                    InterfaceLowering.DumpEnabled = dump;
                }
            }
        }

        private static CompileResult CompileInternal(TestSource[] sources)
        {
            var result = new CompileResult();

            Type contextType = EditorType("UdonSharp.Compiler.CompilationContext");
            Type bindingType = EditorType("UdonSharp.Compiler.ModuleBinding");
            Type compilerType = EditorType("UdonSharp.Compiler.UdonSharpCompilerV1");
            Type udonInterfaceType = EditorType("UdonSharp.Compiler.Udon.CompilerUdonInterface");
            Type optionsType = EditorType("UdonSharp.Compiler.UdonSharpCompileOptions");
            Type assemblyModuleType = EditorType("UdonSharp.Compiler.Assembly.AssemblyModule");
            Type emitContextType = EditorType("UdonSharp.Compiler.Emit.EmitContext");
            Type typeSymbolType = EditorType("UdonSharp.Compiler.Symbols.TypeSymbol");
            Type phaseType = contextType.GetNestedType("CompilePhase", All) ?? throw new MissingMemberException(contextType.FullName, "CompilePhase");

            FieldInfo treeField = Field(bindingType, "tree");
            FieldInfo sourceTextField = Field(bindingType, "sourceText");
            FieldInfo filePathField = Field(bindingType, "filePath");
            FieldInfo semanticModelField = Field(bindingType, "semanticModel");
            FieldInfo assemblyModuleField = Field(bindingType, "assemblyModule");
            PropertyInfo phaseProperty = Property(contextType, "CurrentPhase");

            // ---- Setup ---------------------------------------------------------
            object options = Activator.CreateInstance(optionsType);
            Property(optionsType, "CurrentBuildTarget").SetValue(options, EditorUserBuildSettings.activeBuildTarget);
            object context = Activator.CreateInstance(contextType, All, null, new[] { options }, null);

            Method(udonInterfaceType, "AssemblyCacheInit").Invoke(null, null);
            Method(udonInterfaceType, "CacheInit").Invoke(null, null);
            phaseProperty.SetValue(context, Enum.Parse(phaseType, "Setup"));

            // LoadSyntaxTreesAndCreateModules と同じ解析オプション
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithDocumentationMode(DocumentationMode.None)
                .WithLanguageVersion(LanguageVersion.CSharp9);

            Array bindings = Array.CreateInstance(bindingType, sources.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                string path = SourceDir + sources[i].FileName;
                object binding = Activator.CreateInstance(bindingType);
                treeField.SetValue(binding, CSharpSyntaxTree.ParseText(sources[i].Text, parseOptions, path));
                filePathField.SetValue(binding, path);
                sourceTextField.SetValue(binding, sources[i].Text);
                bindings.SetValue(binding, i);
            }

            Property(contextType, "ModuleBindings").SetValue(context, bindings);

            // 本パッケージの書き換え（通常は LoadSyntaxTreesAndCreateModules の Postfix で行われる）
            CaptureLogs(result.Logs, () => InterfaceLowering.Process(bindings, treeField, sourceTextField, context));

            // U# と同じく、ここでエラーがあれば止める（UdonSharpCompilerV1.Compile）
            if ((int)Property(contextType, "ErrorCount").GetValue(context) > 0)
                return Finish(result, context, contextType);

            var trees = bindings.Cast<object>().Select(b => (SyntaxTree)treeField.GetValue(b)).ToList();

            // ---- Roslyn compile --------------------------------------------------
            phaseProperty.SetValue(context, Enum.Parse(phaseType, "RoslynCompile"));
            var compilation = CSharpCompilation.Create(
                $"CustomUdonSharpTestAssembly{_assemblyCounter++}_{Guid.NewGuid():N}",
                trees,
                (IEnumerable<MetadataReference>)Method(contextType, "GetMetadataReferences").Invoke(null, null),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            byte[] image;
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                if (!emit.Success)
                {
                    result.Diagnostics.AddRange(emit.Diagnostics
                        .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                        .Select(d => Format("Error", d.Location, $"error {d.Id}: {d.GetMessage()}")));
                    return Finish(result, context, contextType);
                }
                image = stream.ToArray();
            }

            Assembly built = Assembly.Load(image);

            FieldInfo assembliesField = Field(udonInterfaceType, "_udonSharpAssemblies");
            var savedAssemblies = (ImmutableArray<Assembly>)assembliesField.GetValue(null);
            assembliesField.SetValue(null, savedAssemblies.Add(built));
            try
            {
                Property(contextType, "RoslynCompilation").SetValue(context, compilation);
                foreach (object binding in bindings)
                    semanticModelField.SetValue(binding, compilation.GetSemanticModel((SyntaxTree)treeField.GetValue(binding)));

                // ---- ルートの UdonSharpBehaviour（ファイル名と同じ名前の具象クラス）----------
                Type tupleType = typeof(ValueTuple<,>).MakeGenericType(typeof(INamedTypeSymbol), bindingType);
                var roots = new List<(INamedTypeSymbol Type, object Binding)>();
                foreach (object binding in bindings)
                {
                    var tree = (SyntaxTree)treeField.GetValue(binding);
                    SemanticModel model = compilation.GetSemanticModel(tree);
                    string expectedName = Path.GetFileNameWithoutExtension((string)filePathField.GetValue(binding));

                    INamedTypeSymbol root = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                        .Select(c => model.GetDeclaredSymbol(c))
                        .FirstOrDefault(s => s != null && !s.IsAbstract && !s.IsGenericType && s.Name == expectedName && InterfaceLowering.IsUdonSharpBehaviour(s));
                    if (root != null)
                        roots.Add((root, binding));
                }

                Array rootArray = Array.CreateInstance(tupleType, roots.Count);
                for (int i = 0; i < roots.Count; i++)
                    rootArray.SetValue(Activator.CreateInstance(tupleType, roots[i].Type, roots[i].Binding), i);

                Method(contextType, "BuildUdonBehaviourInheritanceLookup").Invoke(context, new object[] { roots.Select(r => r.Type).ToList() });

                // ---- Bind -------------------------------------------------------------
                phaseProperty.SetValue(context, Enum.Parse(phaseType, "Bind"));
                Method(compilerType, "BindAllPrograms").Invoke(null, new object[] { rootArray, context });

                if ((int)Property(contextType, "ErrorCount").GetValue(context) == 0)
                {
                    // ---- Emit ---------------------------------------------------------
                    phaseProperty.SetValue(context, Enum.Parse(phaseType, "Emit"));
                    foreach (var (rootType, binding) in roots)
                    {
                        ProgramResult program = EmitProgram(context, contextType, compilerType, assemblyModuleType, emitContextType, typeSymbolType,
                            rootType, binding, assemblyModuleField);
                        if (program != null)
                            result.Programs.Add(program);
                    }
                }
            }
            finally
            {
                assembliesField.SetValue(null, savedAssemblies);
            }

            return Finish(result, context, contextType);
        }

        /// <summary>U# の診断を結果に加えて、並べ替える</summary>
        private static CompileResult Finish(CompileResult result, object context, Type contextType)
        {
            var diagnostics = (System.Collections.IEnumerable)Property(contextType, "Diagnostics").GetValue(context);
            foreach (object diagnostic in diagnostics)
            {
                Type t = diagnostic.GetType();
                result.Diagnostics.Add(Format(
                    t.GetProperty("Severity").GetValue(diagnostic).ToString(),
                    (Location)t.GetProperty("Location").GetValue(diagnostic),
                    (string)t.GetProperty("Message").GetValue(diagnostic)));
            }
            result.Diagnostics.Sort(StringComparer.Ordinal);

            return result;
        }

        /// <summary>EmitAllPrograms のうち、Udon プログラムの生成に関わる部分だけをなぞる</summary>
        private static ProgramResult EmitProgram(object context, Type contextType, Type compilerType, Type assemblyModuleType, Type emitContextType, Type typeSymbolType,
            INamedTypeSymbol rootType, object binding, FieldInfo assemblyModuleField)
        {
            object assemblyModule = Activator.CreateInstance(assemblyModuleType, All, null, new[] { context }, null);
            assemblyModuleField.SetValue(binding, assemblyModule);
            object emitContext = Activator.CreateInstance(emitContextType, All, null, new object[] { assemblyModule, rootType }, null);

            MethodInfo getSpecialType = Method(emitContextType, "GetTypeSymbol", typeof(SpecialType));
            MethodInfo getSystemType = Method(emitContextType, "GetTypeSymbol", typeof(Type));
            object rootTable = Property(emitContextType, "RootTable").GetValue(emitContext);
            MethodInfo createReflectionValue = Method(rootTable.GetType(), "CreateReflectionValue", typeof(string), typeSymbolType, typeof(object));
            MethodInfo getFullTypeName = Method(typeSymbolType, "GetFullTypeName", typeof(ITypeSymbol));
            MethodInfo getTypeId = Method(RuntimeAssembly.GetType("UdonSharp.Internal.UdonSharpInternalUtility", true), "GetTypeID", typeof(string));
            PropertyInfo baseTypeProperty = Property(typeSymbolType, "BaseType");
            PropertyInfo roslynSymbolProperty = typeSymbolType.GetProperty("RoslynSymbol", All | BindingFlags.DeclaredOnly)
                                                ?? Property(typeSymbolType.BaseType, "RoslynSymbol");

            string typeName = (string)getFullTypeName.Invoke(null, new object[] { rootType });
            long typeId = (long)getTypeId.Invoke(null, new object[] { typeName });

            createReflectionValue.Invoke(rootTable, new[] { UsbTypeIDHeapKey, getSpecialType.Invoke(emitContext, new object[] { SpecialType.System_Int64 }), typeId });
            createReflectionValue.Invoke(rootTable, new[] { UsbTypeNameHeapKey, getSpecialType.Invoke(emitContext, new object[] { SpecialType.System_String }), typeName });

            Type usbSystemType = RuntimeAssembly.GetType("UdonSharp.UdonSharpBehaviour", true);
            object usbType = getSystemType.Invoke(emitContext, new object[] { usbSystemType });
            object emitType = Property(emitContextType, "EmitType").GetValue(emitContext);
            bool hasInherited = (bool)Method(contextType, "HasInheritedUdonSharpBehaviours").Invoke(context, new[] { emitType });

            if (!ReferenceEquals(baseTypeProperty.GetValue(emitType), usbType) || hasInherited)
            {
                var baseTypeIds = new List<long>();
                for (object current = emitType; !ReferenceEquals(current, usbType); current = baseTypeProperty.GetValue(current))
                {
                    var symbol = (ITypeSymbol)roslynSymbolProperty.GetValue(current);
                    baseTypeIds.Add((long)getTypeId.Invoke(null, new object[] { (string)getFullTypeName.Invoke(null, new object[] { symbol }) }));
                }
                createReflectionValue.Invoke(rootTable, new[] { UsbTypeIDArrayHeapKey, getSystemType.Invoke(emitContext, new object[] { typeof(long[]) }), baseTypeIds.ToArray() });
            }

            try
            {
                Method(emitContextType, "Emit", Type.EmptyTypes).Invoke(emitContext, null);
            }
            catch (TargetInvocationException e)
            {
                // EmitAllPrograms と同様に診断として記録する（CompilerException も他の例外も）
                Exception inner = e.InnerException ?? e;
                object node = Property(emitContextType, "CurrentNode").GetValue(emitContext);
                Method(contextType, "AddDiagnostic", EditorType("UdonSharp.Compiler.DiagnosticSeverity"), typeof(Location), typeof(string))
                    .Invoke(context, new object[]
                    {
                        Enum.Parse(EditorType("UdonSharp.Compiler.DiagnosticSeverity"), "Error"),
                        (inner.GetType().GetProperty("Location")?.GetValue(inner) as Location) ?? (node as SyntaxNode)?.GetLocation(),
                        inner.Message,
                    });
                return null;
            }

            var program = new ProgramResult
            {
                TypeName = typeName,
                Uasm = (string)Method(assemblyModuleType, "BuildUasmStr").Invoke(assemblyModule, null),
            };

            // ヒープの初期値（AssembleProgram で heap に設定される値）
            object moduleRootTable = Property(assemblyModuleType, "RootTable").GetValue(assemblyModule);
            foreach (object value in (System.Collections.IEnumerable)Method(moduleRootTable.GetType(), "GetAllUniqueChildValues").Invoke(moduleRootTable, null))
            {
                Type valueType = value.GetType();
                object defaultValue = valueType.GetProperty("DefaultValue", All)?.GetValue(value);
                if (defaultValue != null)
                    program.HeapDefaults[(string)valueType.GetProperty("UniqueID", All).GetValue(value)] = FormatValue(defaultValue);
            }

            // 通信用メタデータ
            var metadata = (Array)Method(compilerType, "CollectNetworkMetadataFromAttributes").Invoke(null, new[] { context, emitContext });
            foreach (object entry in metadata)
            {
                var info = new NetworkEventInfo { Name = (string)entry.GetType().GetProperty("Name").GetValue(entry) };
                foreach (object parameter in (Array)entry.GetType().GetProperty("Parameters").GetValue(entry))
                {
                    Type pt = parameter.GetType();
                    info.Parameters.Add(((string)pt.GetProperty("Name").GetValue(parameter), pt.GetProperty("Type").GetValue(parameter).ToString()));
                }
                program.NetworkEvents.Add(info);
            }

            return program;
        }

        private static string FormatValue(object value)
        {
            if (value is string s)
                return "\"" + s + "\"";
            if (value is Array array)
                return "[" + string.Join(", ", array.Cast<object>().Select(v => v == null ? "null" : FormatValue(v))) + "]";
            return value.ToString();
        }

        private static string Format(string severity, Location location, string message)
        {
            if (location == null || location == Location.None)
                return $"{severity}: {message}";
            var span = location.GetLineSpan();
            return $"{severity}: {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {message}";
        }

        private static void CaptureLogs(List<string> logs, Action action)
        {
            void OnLog(string message, string stackTrace, LogType type)
            {
                if (message.Contains("[CustomUdonSharp]"))
                    logs.Add($"{type}: {message}");
            }

            Application.logMessageReceived += OnLog;
            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
        }
    }
}
