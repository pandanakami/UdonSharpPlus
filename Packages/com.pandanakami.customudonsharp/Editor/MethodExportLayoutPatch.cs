using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;

namespace CustomUdonSharp
{
    /// <summary>
    /// CompilationContext.MethodExportLayout のコンストラクタに Prefix を当て、インタフェース実装メソッドの
    /// Udon 上の名前を、どの実装クラスでも揃うものに差し替える。
    ///   メソッド名 : 元の名前（U# は引数のあるメソッドを "__0_Receive" のように改名するが、それをやめる）
    ///   引数・戻り値 : シグネチャ由来の固定名（Receive__int__string__p0 / __ret。実装側で引数名が違っても揃うよう位置で付ける）
    /// メソッド名を元の名前にする代わりに、同じクラスでの同名メソッドとの共存を禁止する（InterfaceLowering で検査。
    /// 素の U# が [NetworkCallable] に課しているのと同じ制約。#51）
    ///
    /// MethodExportLayout は BuildMethodLayout で 1 か所だけ生成され、同じインスタンスが
    /// 呼ばれる側 (EmitContext.GetMethodLinkage) と呼ぶ側 (BoundUdonSharpBehaviourInvocationExpression) の
    /// 両方から参照されるので、ここで差し替えれば全体で整合する。
    /// </summary>
    internal static class MethodExportLayoutPatch
    {
        private static PropertyInfo _roslynSymbolProperty;

        public static void Apply(Harmony harmony)
        {
            Type contextType = AccessTools.TypeByName("UdonSharp.Compiler.CompilationContext");
            Type layoutType = contextType == null ? null : AccessTools.Inner(contextType, "MethodExportLayout");
            Type methodSymbolType = AccessTools.TypeByName("UdonSharp.Compiler.Symbols.MethodSymbol");
            ConstructorInfo ctor = layoutType == null || methodSymbolType == null
                ? null
                : AccessTools.Constructor(layoutType, new[] { methodSymbolType, typeof(string), typeof(string), typeof(string[]) });

            // MethodSymbol は RoslynSymbol を new で再定義しているため、MethodSymbol から引くと AmbiguousMatchException になる。
            // 基底 Symbol の virtual プロパティ (ISymbol) を使う
            Type symbolType = AccessTools.TypeByName("UdonSharp.Compiler.Symbols.Symbol");
            _roslynSymbolProperty = symbolType == null ? null : symbolType.GetProperty("RoslynSymbol", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (ctor == null || _roslynSymbolProperty == null)
                throw new MissingMemberException("MethodExportLayout constructor not found. UdonSharp internals may have changed.");

            harmony.Patch(ctor, prefix: new HarmonyMethod(typeof(MethodExportLayoutPatch), nameof(Prefix)));
        }

        // 引数名は元のコンストラクタと一致させる（Harmony は名前で対応づける）。MethodSymbol は internal なので object で受ける
        private static void Prefix(object method, ref string exportMethodName, ref string returnExportName, ref string[] parameterExportNames)
        {
            var keys = InterfaceNaming.ImplementationKeys;
            if (keys.Count == 0 || method == null)
                return;

            if (!(_roslynSymbolProperty.GetValue(method) is IMethodSymbol symbol))
                return;

            // 組み込みイベント（"Interact" → "_interact" のように改名済み）は Udon 側と名前を取り決めているので触らない。
            // [NetworkCallable] も対象にする：メソッド名はもともと元の名前で、引数名だけが揃う。
            // 通信用メタデータ（UdonSharpCompilerV1.CollectNetworkMetadataFromAttributes）もこのレイアウトから作られる
            bool isBuiltinEvent = exportMethodName != symbol.Name && !exportMethodName.StartsWith("__", StringComparison.Ordinal);
            if (isBuiltinEvent)
                return;

            // ここに来る時点で U# 用ツリーはインタフェース型→object、汎用クラス→object[] の置換済みなので、置換は不要
            if (!keys.Contains(InterfaceNaming.Key(symbol, _ => false)))
                return;

            string signature = InterfaceNaming.SignatureName(symbol, _ => false);

            exportMethodName = symbol.Name;
            if (returnExportName != null)
                returnExportName = InterfaceNaming.ReturnName(signature);
            if (parameterExportNames != null)
                parameterExportNames = Enumerable.Range(0, parameterExportNames.Length)
                    .Select(i => InterfaceNaming.ParameterName(signature, i))
                    .ToArray();
        }
    }
}
