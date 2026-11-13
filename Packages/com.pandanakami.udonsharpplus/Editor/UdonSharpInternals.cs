using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using UdonSharp.Serialization;

namespace UdonSharpPlus
{
    /// <summary>
    /// 本パッケージが使う UdonSharp の内部 API（Redmine #39）。起動時にまとめて探し、1 つでも見つからなければ
    /// どのパッチも当てない（一部だけ当たった中途半端な状態にしない）。
    /// 対応する SDK のバージョンを上げるときは、まずこれが全部見つかるか（UdonSharpInternalsTests）を確かめる。
    /// </summary>
    internal static class UdonSharpInternals
    {
        // ---- 型 --------------------------------------------------------------------
        public static Type CompilationContext, ModuleBinding, DiagnosticSeverity, MethodExportLayout;
        public static Type Symbol, MethodSymbol, TypeSymbol;
        public static Type AssemblyDebugInfo, MethodDebugInfo, MethodDebugMarker, EmitContext, AssemblyModule;
        public static Type DataList, DataDictionary;

        // ---- UdonSharpInterfacePatch・InterfaceLowering --------------------------------
        public static MethodInfo LoadSyntaxTreesAndCreateModules;
        public static FieldInfo BindingTree, BindingSourceText, BindingFilePath;
        public static MethodInfo GetMetadataReferences;
        public static FieldInfo FullTypeFormat;          // TypeSymbol._fullTypeFormat
        public static MethodInfo GetTypeId;              // UdonSharpInternalUtility.GetTypeID(string)

        // ---- MethodExportLayoutPatch --------------------------------------------------
        public static ConstructorInfo MethodExportLayoutCtor;
        public static PropertyInfo SymbolRoslynSymbol;   // 基底 Symbol の RoslynSymbol（ISymbol）

        // ---- DiagnosticLocationPatch・LoweringDiagnostics --------------------------------
        public static PropertyInfo ModuleBindings;
        public static MethodInfo AddDiagnosticWithLocation, AddDiagnosticWithNode, TranslateLocationToFileName;

        // ---- DebugInfoPatch -----------------------------------------------------------
        public static FieldInfo CurrentSpan, CurrentDebugInfo, DebugMarkers, ContainingFilePath;
        public static FieldInfo MarkerStartInstruction, MarkerStartLine, MarkerStartChar;
        public static PropertyInfo EmitCompileContext, EmitModule, ModuleCurrentAddress;
        public static PropertyInfo MethodSymbolRoslynSymbol;   // MethodSymbol が new で再定義した RoslynSymbol
        public static MethodInfo StartMethodEmit, UpdateSyntaxNode, FinalizeMethodEmit;

        // ---- LoweredFieldSerializerPatch ----------------------------------------------
        public static MethodInfo SerializerCreate;

        // ---- UpgradeGuardPatch --------------------------------------------------------
        public static MethodInfo UpgradeScripts;         // UdonSharpUpgrader.UpgradeScripts(UdonSharpProgramAsset[])
        public static FieldInfo NeedsProgramUpgradePass; // UdonSharpUpgrader._needsProgramUpgradePass（static）
        public static MethodInfo TryGetSystemType;       // TypeSymbol.TryGetSystemType(ITypeSymbol, out Type)
        public static PropertyInfo EditorCacheInstance;  // UdonSharpEditorCache.Instance
        public static MethodInfo ClearUpgradePassQueue;  // UdonSharpEditorCache.ClearUpgradePassQueue()
        public static MethodInfo UpgradeVisitField;      // UdonSharpUpgrader.SerializationUpdateSyntaxRewriter.VisitFieldDeclaration（#127）
        public static FieldInfo UpgradeRewriterModel;    // UdonSharpUpgrader.SerializationUpdateSyntaxRewriter.model

        /// <summary>見つからなかったものの名前（Resolve の後）</summary>
        public static IReadOnlyList<string> Missing => _missing;

        private static readonly List<string> _missing = new List<string>();

        /// <summary>全部探す。全部見つかれば true</summary>
        public static bool Resolve()
        {
            _missing.Clear();

            CompilationContext = T("UdonSharp.Compiler.CompilationContext");
            ModuleBinding = T("UdonSharp.Compiler.ModuleBinding");
            DiagnosticSeverity = T("UdonSharp.Compiler.DiagnosticSeverity");
            MethodExportLayout = Need(CompilationContext == null ? null : AccessTools.Inner(CompilationContext, "MethodExportLayout"), "CompilationContext.MethodExportLayout");
            Symbol = T("UdonSharp.Compiler.Symbols.Symbol");
            MethodSymbol = T("UdonSharp.Compiler.Symbols.MethodSymbol");
            TypeSymbol = T("UdonSharp.Compiler.Symbols.TypeSymbol");
            AssemblyDebugInfo = T("UdonSharp.Compiler.AssemblyDebugInfo");
            MethodDebugInfo = T("UdonSharp.Compiler.MethodDebugInfo");
            MethodDebugMarker = T("UdonSharp.Compiler.MethodDebugMarker");
            EmitContext = T("UdonSharp.Compiler.Emit.EmitContext");
            AssemblyModule = T("UdonSharp.Compiler.Assembly.AssemblyModule");
            DataList = T("VRC.SDK3.Data.DataList");
            DataDictionary = T("VRC.SDK3.Data.DataDictionary");
            Type internalUtility = T("UdonSharp.Internal.UdonSharpInternalUtility");

            LoadSyntaxTreesAndCreateModules = M(CompilationContext, "LoadSyntaxTreesAndCreateModules");
            BindingTree = F(ModuleBinding, "tree");
            BindingSourceText = F(ModuleBinding, "sourceText");
            BindingFilePath = F(ModuleBinding, "filePath");
            GetMetadataReferences = M(CompilationContext, "GetMetadataReferences");
            FullTypeFormat = F(TypeSymbol, "_fullTypeFormat");
            GetTypeId = M(internalUtility, "GetTypeID", typeof(string));

            MethodExportLayoutCtor = MethodExportLayout == null || MethodSymbol == null
                ? Need<ConstructorInfo>(null, "MethodExportLayout(MethodSymbol, string, string, string[])")
                : Need(AccessTools.Constructor(MethodExportLayout, new[] { MethodSymbol, typeof(string), typeof(string), typeof(string[]) }),
                    "MethodExportLayout(MethodSymbol, string, string, string[])");
            // MethodSymbol は RoslynSymbol を new で再定義しているため、名前だけで引くと曖昧になる。宣言されている方を引く
            SymbolRoslynSymbol = Need(Symbol?.GetProperty("RoslynSymbol", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), "Symbol.RoslynSymbol");
            MethodSymbolRoslynSymbol = Need(MethodSymbol == null ? null : AccessTools.DeclaredProperty(MethodSymbol, "RoslynSymbol"), "MethodSymbol.RoslynSymbol");

            ModuleBindings = P(CompilationContext, "ModuleBindings");
            AddDiagnosticWithLocation = M(CompilationContext, "AddDiagnostic", DiagnosticSeverity, typeof(Location), typeof(string));
            AddDiagnosticWithNode = M(CompilationContext, "AddDiagnostic", DiagnosticSeverity, typeof(SyntaxNode), typeof(string));
            TranslateLocationToFileName = M(CompilationContext, "TranslateLocationToFileName", typeof(Location));

            CurrentSpan = F(AssemblyDebugInfo, "_currentSpan");
            CurrentDebugInfo = F(AssemblyDebugInfo, "_currentDebugInfo");
            DebugMarkers = F(MethodDebugInfo, "debugMarkers");
            ContainingFilePath = F(MethodDebugInfo, "containingFilePath");
            MarkerStartInstruction = F(MethodDebugMarker, "startInstruction");
            MarkerStartLine = F(MethodDebugMarker, "startLine");
            MarkerStartChar = F(MethodDebugMarker, "startChar");
            EmitCompileContext = P(EmitContext, "CompileContext");
            EmitModule = P(EmitContext, "Module");
            ModuleCurrentAddress = P(AssemblyModule, "CurrentAddress");
            StartMethodEmit = M(AssemblyDebugInfo, "StartMethodEmit");
            UpdateSyntaxNode = M(AssemblyDebugInfo, "UpdateSyntaxNode");
            FinalizeMethodEmit = M(AssemblyDebugInfo, "FinalizeMethodEmit");

            SerializerCreate = M(typeof(Serializer), "Create", typeof(TypeSerializationMetadata));

            Type programAsset = T("UdonSharp.UdonSharpProgramAsset");
            Type upgrader = T("UdonSharpEditor.UdonSharpUpgrader");
            UpgradeScripts = M(upgrader, "UpgradeScripts", programAsset?.MakeArrayType());
            NeedsProgramUpgradePass = F(upgrader, "_needsProgramUpgradePass");
            TryGetSystemType = M(TypeSymbol, "TryGetSystemType", typeof(ITypeSymbol), typeof(Type).MakeByRefType());
            Type editorCache = T("UdonSharp.UdonSharpEditorCache");
            EditorCacheInstance = P(editorCache, "Instance");
            ClearUpgradePassQueue = M(editorCache, "ClearUpgradePassQueue");
            Type upgradeRewriter = Need(upgrader == null ? null : AccessTools.Inner(upgrader, "SerializationUpdateSyntaxRewriter"), "UdonSharpUpgrader.SerializationUpdateSyntaxRewriter");
            UpgradeVisitField = M(upgradeRewriter, "VisitFieldDeclaration", typeof(Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax));
            UpgradeRewriterModel = F(upgradeRewriter, "model");

            return _missing.Count == 0;
        }

        private static Type T(string name) => Need(AccessTools.TypeByName(name), name);

        private static MethodInfo M(Type type, string name, params Type[] parameters)
        {
            bool unknownParameter = Array.IndexOf(parameters, null) >= 0;
            MethodInfo method = type == null || unknownParameter
                ? null
                : parameters.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, parameters);
            return Need(method, $"{type?.Name ?? "?"}.{name}");
        }

        private static FieldInfo F(Type type, string name) => Need(type == null ? null : AccessTools.Field(type, name), $"{type?.Name ?? "?"}.{name}");

        private static PropertyInfo P(Type type, string name) => Need(type == null ? null : AccessTools.Property(type, name), $"{type?.Name ?? "?"}.{name}");

        private static TMember Need<TMember>(TMember member, string name) where TMember : class
        {
            if (member == null)
                _missing.Add(name);
            return member;
        }
    }
}
