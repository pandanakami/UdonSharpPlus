using System;
using System.Reflection;
using HarmonyLib;
using UdonSharp.Serialization;
using UnityEditor;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// UdonSharp の CompilationContext.LoadSyntaxTreesAndCreateModules に Postfix を当て、
    /// パース済み SyntaxTree をインタフェース対応のコードへ書き換える。
    /// ほかのパッチもここでまとめて当てる。必要な内部 API（UdonSharpInternals）が 1 つでも見つからないとき、
    /// または当てる途中で失敗したときは、どのパッチも当てない（素の UdonSharp と同じ動きになる。#39）
    /// </summary>
    [InitializeOnLoad]
    internal static class UdonSharpInterfacePatch
    {
        private const string HarmonyId = "com.pandanakami.udonsharpplus";

        /// <summary>パッチが当たっているか</summary>
        internal static bool Enabled { get; private set; }

        static UdonSharpInterfacePatch()
        {
            SdkVersionCheck.WarnIfOutOfRange();   // 範囲外でも動きは変えない（#95）

            if (!UdonSharpInternals.Resolve())
            {
                ReportDisabled(Loc.T("見つからないもの：", "not found: ") + string.Join(Loc.T("、", ", "), UdonSharpInternals.Missing));
                return;
            }

            var harmony = new Harmony(HarmonyId);
            harmony.UnpatchAll(HarmonyId);
            try
            {
                harmony.Patch(UdonSharpInternals.LoadSyntaxTreesAndCreateModules,
                    postfix: new HarmonyMethod(typeof(UdonSharpInterfacePatch), nameof(Postfix)));
                MethodExportLayoutPatch.Apply(harmony);
                DiagnosticLocationPatch.Apply(harmony);
                DebugInfoPatch.Apply(harmony);
                LoweredFieldSerializerPatch.Apply(harmony);
                UpgradeGuardPatch.Apply(harmony);
                Enabled = true;
            }
            catch (Exception e)
            {
                // 引数名が変わった場合などは、当てるときに失敗する。当てた分も外して、中途半端な状態にしない
                harmony.UnpatchAll(HarmonyId);
                ReportDisabled(Loc.T("パッチを当てられませんでした：", "could not apply the patches: ") + e.Message);
            }
        }

        private static void ReportDisabled(string detail)
        {
            Debug.LogError(Loc.T($"[UdonSharpPlus] この VRChat SDK（{SdkVersion()}）では UdonSharp の内部が変わっているため、無効にしました（{detail}）。UdonSharp Plus を更新してください", $"[UdonSharpPlus] UdonSharp's internals have changed in this VRChat SDK ({SdkVersion()}), so UdonSharp Plus has been disabled ({detail}). Update UdonSharp Plus"));
        }

        /// <summary>UdonSharp を含む VRChat SDK（com.vrchat.worlds）のバージョン</summary>
        internal static string SdkVersion()
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Serializer).Assembly)?.version ?? Loc.T("不明", "unknown");
            }
            catch (Exception)
            {
                return Loc.T("不明", "unknown");
            }
        }

        private static void Postfix(object __instance, object __result)
        {
            try
            {
                // UdonSharp の自動アップグレードが読み込むときは書き換えない（ソースに書き戻されるため。#84）
                if (UpgradeGuardPatch.ConsumeSkip())
                    return;
                if (__result is Array bindings)
                    InterfaceLowering.Process(bindings, UdonSharpInternals.BindingTree, UdonSharpInternals.BindingSourceText, __instance);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UdonSharpPlus] Interface lowering failed, compiling original sources: {e}");
            }
        }
    }
}
