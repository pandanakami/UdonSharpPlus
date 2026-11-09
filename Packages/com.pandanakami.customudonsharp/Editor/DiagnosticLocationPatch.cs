using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.CodeAnalysis;

namespace CustomUdonSharp
{
    /// <summary>
    /// CompilationContext.AddDiagnostic に Prefix を当て、汎用クラスのコピー（使う UdonSharpBehaviour の閉じ括弧の行に生成）の
    /// 中で出た診断の位置を、元のクラスのソースの位置に戻す（Redmine #52）。
    ///
    /// - Roslyn のエラー、U# の bind / emit のエラー、emit 中の例外は、どれもこのメソッドを通る
    /// - 位置は、そのとき U# が持っている同じファイルの tree（ModuleBindings）の Location にする
    ///   （U# は binding.tree と照合してファイル名を引くため）
    /// - 同じクラスのメソッドが複数の UdonSharpBehaviour にコピーされて同じエラーが出る場合は、1 つにまとめる
    /// - メッセージの末尾に「（汎用クラス Saver のメソッド Exists の中）」の補足を付ける
    ///   （メッセージの中の式は書き換え後のもの（((string)(__this)[2]) など）なので、どこのコードかを示す）
    /// </summary>
    internal static class DiagnosticLocationPatch
    {
        private static FieldInfo _treeField;
        private static PropertyInfo _bindingsProperty;
        private static MethodInfo _addWithLocation;

        // コンパイル（CompilationContext）ごとに、戻した診断の重複を覚えておく
        private static readonly ConditionalWeakTable<object, HashSet<string>> Reported = new ConditionalWeakTable<object, HashSet<string>>();

        public static void Apply(Harmony harmony)
        {
            Type contextType = AccessTools.TypeByName("UdonSharp.Compiler.CompilationContext");
            Type severityType = AccessTools.TypeByName("UdonSharp.Compiler.DiagnosticSeverity");
            Type bindingType = AccessTools.TypeByName("UdonSharp.Compiler.ModuleBinding");
            if (contextType == null || severityType == null || bindingType == null)
                throw new MissingMemberException("CompilationContext.AddDiagnostic not found. UdonSharp internals may have changed.");

            _treeField = AccessTools.Field(bindingType, "tree");
            _bindingsProperty = AccessTools.Property(contextType, "ModuleBindings");
            _addWithLocation = AccessTools.Method(contextType, "AddDiagnostic", new[] { severityType, typeof(Location), typeof(string) });
            MethodInfo addWithNode = AccessTools.Method(contextType, "AddDiagnostic", new[] { severityType, typeof(SyntaxNode), typeof(string) });
            if (_treeField == null || _bindingsProperty == null || _addWithLocation == null || addWithNode == null)
                throw new MissingMemberException("CompilationContext.AddDiagnostic not found. UdonSharp internals may have changed.");

            harmony.Patch(_addWithLocation, prefix: new HarmonyMethod(typeof(DiagnosticLocationPatch), nameof(PrefixLocation)));
            harmony.Patch(addWithNode, prefix: new HarmonyMethod(typeof(DiagnosticLocationPatch), nameof(PrefixNode)));
        }

        // 引数名は元のメソッドと一致させる（Harmony は名前で対応づける）
        private static bool PrefixLocation(object __instance, object severity, ref Location location, ref string message)
        {
            try
            {
                Location mapped = Map(__instance, location, ref message);
                if (mapped == null)
                    return true;
                if (!FirstTime(__instance, severity, mapped, message))
                    return false;   // 同じ位置・同じ内容はもう登録済み
                location = mapped;
            }
            catch (Exception)
            {
                // 位置の差し替えに失敗しても、元の診断はそのまま登録する
            }
            return true;
        }

        private static bool PrefixNode(object __instance, object severity, SyntaxNode node, string message)
        {
            try
            {
                Location mapped = Map(__instance, node?.GetLocation(), ref message);
                if (mapped == null)
                    return true;
                if (FirstTime(__instance, severity, mapped, message))
                    _addWithLocation.Invoke(__instance, new[] { severity, mapped, message });
                return false;   // Location 版で登録した（か、重複なので捨てた）
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>生成コードの中の位置なら、元のファイルの位置（メッセージには補足を付ける）。そうでなければ null</summary>
        private static Location Map(object context, Location location, ref string message)
        {
            SourceMap.Entry entry = SourceMap.Find(location);
            if (entry == null)
                return null;

            Location mapped = OriginalLocation(context, entry);
            if (mapped != null)
                message += SourceMap.Describe(entry);
            return mapped;
        }

        /// <summary>元の位置の、U# の表記でのファイル名（TranslateLocationToFileName）。引けなければ null（#53）</summary>
        internal static string FileNameOf(object context, SourceMap.Entry entry)
        {
            Location location = OriginalLocation(context, entry);
            if (location == null)
                return null;
            _translateLocation = _translateLocation ?? AccessTools.Method(context.GetType(), "TranslateLocationToFileName", new[] { typeof(Location) });
            return _translateLocation?.Invoke(context, new object[] { location }) as string;
        }

        private static MethodInfo _translateLocation;

        private static Location OriginalLocation(object context, SourceMap.Entry entry)
        {
            // tree はパスを持たない（U# はパスなしでパースする）ので、binding の番号で引く
            if (_bindingsProperty == null || !(_bindingsProperty.GetValue(context) is Array bindings) || entry.TreeIndex < 0 || entry.TreeIndex >= bindings.Length)
                return null;
            var tree = (SyntaxTree)_treeField.GetValue(bindings.GetValue(entry.TreeIndex));
            return tree == null ? null : SourceMap.ToLocation(entry, tree);
        }

        private static bool FirstTime(object context, object severity, Location location, string message)
        {
            HashSet<string> seen = Reported.GetValue(context, _ => new HashSet<string>());
            FileLinePositionSpan span = location.GetLineSpan();
            lock (seen)
                return seen.Add($"{severity}|{span.Path}|{span.StartLinePosition}|{message}");
        }
    }
}
