using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.CodeAnalysis;

namespace UdonSharpPlus
{
    /// <summary>
    /// U# のデバッグ情報（AssemblyDebugInfo：命令のアドレス → ソースの位置）で、汎用クラスのコピーのメソッドの位置を
    /// 元のクラスのソースの位置にする（Redmine #53）。
    ///
    /// 実行時例外（"Udon runtime exception detected!"）の位置は、RuntimeLogWatcher がこのデバッグ情報から引く。
    /// コピーは使う UdonSharpBehaviour の閉じ括弧の行に生成しているので、そのままでは位置がその行になる。
    ///
    /// - StartMethodEmit の Postfix：コピーのメソッドなら、メソッドのファイルを元のクラスのファイルにする
    /// - UpdateSyntaxNode の Prefix：コピーのメソッドの中のノードなら、#52 の印から引いた元の位置で記録する
    ///   （記録の形は U# と同じ。区間の切り替えは文の単位に合わせて少し変える。Record を参照）
    /// - FinalizeMethodEmit の Prefix：コピーのメソッドなら、最後の区間も記録する
    /// コピー以外のメソッドには何もしない。
    /// </summary>
    internal static class DebugInfoPatch
    {
        private sealed class MethodState
        {
            public SourceMap.Entry First;   // メソッドの最初の印（印のないノードはここにする）
        }

        private static FieldInfo _currentSpan;       // MethodDebugMarker?
        private static FieldInfo _currentDebugInfo;  // MethodDebugInfo
        private static FieldInfo _debugMarkers;      // List<MethodDebugMarker>
        private static FieldInfo _containingFilePath;
        private static FieldInfo _startInstruction, _startLine, _startChar;
        private static Type _markerType;
        private static PropertyInfo _compileContext; // EmitContext.CompileContext
        private static PropertyInfo _module;         // EmitContext.Module
        private static PropertyInfo _currentAddress; // AssemblyModule.CurrentAddress
        private static PropertyInfo _roslynSymbol;   // MethodSymbol.RoslynSymbol

        private static readonly ConditionalWeakTable<object, MethodState> Current = new ConditionalWeakTable<object, MethodState>();

        public static void Apply(Harmony harmony)
        {
            _markerType = UdonSharpInternals.MethodDebugMarker;
            _currentSpan = UdonSharpInternals.CurrentSpan;
            _currentDebugInfo = UdonSharpInternals.CurrentDebugInfo;
            _debugMarkers = UdonSharpInternals.DebugMarkers;
            _containingFilePath = UdonSharpInternals.ContainingFilePath;
            _startInstruction = UdonSharpInternals.MarkerStartInstruction;
            _startLine = UdonSharpInternals.MarkerStartLine;
            _startChar = UdonSharpInternals.MarkerStartChar;
            _compileContext = UdonSharpInternals.EmitCompileContext;
            _module = UdonSharpInternals.EmitModule;
            _currentAddress = UdonSharpInternals.ModuleCurrentAddress;
            // MethodSymbol は基底の RoslynSymbol を new で隠しているので、宣言されている方を引く（名前だけだと曖昧になる）
            _roslynSymbol = UdonSharpInternals.MethodSymbolRoslynSymbol;

            harmony.Patch(UdonSharpInternals.StartMethodEmit, postfix: new HarmonyMethod(typeof(DebugInfoPatch), nameof(PostfixStart)));
            harmony.Patch(UdonSharpInternals.UpdateSyntaxNode, prefix: new HarmonyMethod(typeof(DebugInfoPatch), nameof(PrefixUpdate)));
            harmony.Patch(UdonSharpInternals.FinalizeMethodEmit, prefix: new HarmonyMethod(typeof(DebugInfoPatch), nameof(PrefixFinish)));
        }

        // 引数名は元のメソッドと一致させる（Harmony は名前で対応づける）
        private static void PostfixStart(object __instance, object methodSymbol, object context)
        {
            try
            {
                Current.Remove(__instance);

                var symbol = _roslynSymbol.GetValue(methodSymbol) as ISymbol;
                if (symbol == null || symbol.DeclaringSyntaxReferences.Length == 0)
                    return;
                SourceMap.Entry first = SourceMap.FirstIn(symbol.DeclaringSyntaxReferences[0].GetSyntax());
                if (first == null)
                    return;   // コピーではない

                object compileContext = _compileContext.GetValue(context);
                string path = DiagnosticLocationPatch.FileNameOf(compileContext, first);
                if (path == null)
                    return;

                _containingFilePath.SetValue(_currentDebugInfo.GetValue(__instance), path);
                Current.Add(__instance, new MethodState { First = first });
            }
            catch (Exception)
            {
                // 失敗しても U# のデバッグ情報はそのまま作る
                Current.Remove(__instance);
            }
        }

        private static bool PrefixUpdate(object __instance, SyntaxNode node, object context)
        {
            if (!Current.TryGetValue(__instance, out MethodState state))
                return true;
            try
            {
                SourceMap.Entry entry = SourceMap.Find(node.GetLocation());
                // 別のクラスのコピーの印を拾ったら（メソッドの外の印）、メソッドの最初の印にする
                if (entry == null || entry.TreeIndex != state.First.TreeIndex || entry.ClassName != state.First.ClassName)
                    entry = state.First;
                Record(__instance, context, entry.Line, entry.Character);
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void PrefixFinish(object __instance)
        {
            if (!Current.TryGetValue(__instance, out _))
                return;
            Current.Remove(__instance);
            try
            {
                // 最後の区間も記録する（U# は捨てるが、文の単位だとメソッドの最後の文がまるごと前の文の位置になるため）
                object span = _currentSpan.GetValue(__instance);
                if (span != null)
                    Markers(__instance).Add(span);
                _currentSpan.SetValue(__instance, null);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 位置を記録する。AssemblyDebugInfo.UpdateSyntaxNode と同じ形（区間の始まりの命令と位置）で記録するが、
        /// 位置が変わったらその場で次の区間を始める（U# は次の呼び出しまで始めないので、区間の頭がずれる。
        /// U# はノードごとに位置が細かく変わるので目立たないが、文の単位だと 1 文ずれる）
        /// </summary>
        private static void Record(object debugInfo, object context, int line, int character)
        {
            object span = _currentSpan.GetValue(debugInfo);   // null か、箱詰めの MethodDebugMarker
            if (span != null && (int)_startLine.GetValue(span) == line && (int)_startChar.GetValue(span) == character)
                return;

            if (span != null)
                Markers(debugInfo).Add(span);

            object marker = Activator.CreateInstance(_markerType);
            _startInstruction.SetValue(marker, Convert.ToInt32(_currentAddress.GetValue(_module.GetValue(context))));
            _startLine.SetValue(marker, line);
            _startChar.SetValue(marker, character);
            _currentSpan.SetValue(debugInfo, marker);
        }

        private static System.Collections.IList Markers(object debugInfo)
        {
            object methodInfo = _currentDebugInfo.GetValue(debugInfo);
            var markers = (System.Collections.IList)_debugMarkers.GetValue(methodInfo);
            if (markers == null)
            {
                markers = (System.Collections.IList)Activator.CreateInstance(_debugMarkers.FieldType);
                _debugMarkers.SetValue(methodInfo, markers);
            }
            return markers;
        }
    }
}
