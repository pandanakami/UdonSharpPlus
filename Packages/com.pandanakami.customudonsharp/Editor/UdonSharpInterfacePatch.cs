using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace CustomUdonSharp
{
    /// <summary>
    /// UdonSharp の CompilationContext.LoadSyntaxTreesAndCreateModules に Postfix を当て、
    /// パース済み SyntaxTree をインタフェース対応のコードへ書き換える。
    /// </summary>
    [InitializeOnLoad]
    internal static class UdonSharpInterfacePatch
    {
        private const string HarmonyId = "com.pandanakami.customudonsharp";

        private static FieldInfo _treeField;
        private static FieldInfo _sourceTextField;

        static UdonSharpInterfacePatch()
        {
            try
            {
                Type contextType = AccessTools.TypeByName("UdonSharp.Compiler.CompilationContext");
                Type bindingType = AccessTools.TypeByName("UdonSharp.Compiler.ModuleBinding");
                MethodInfo target = contextType == null ? null : AccessTools.Method(contextType, "LoadSyntaxTreesAndCreateModules");

                if (target == null || bindingType == null)
                {
                    Debug.LogError("[CustomUdonSharp] Patch target not found. UdonSharp internals may have changed.");
                    return;
                }

                _treeField = AccessTools.Field(bindingType, "tree");
                _sourceTextField = AccessTools.Field(bindingType, "sourceText");

                var harmony = new Harmony(HarmonyId);
                harmony.UnpatchAll(HarmonyId);
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(UdonSharpInterfacePatch), nameof(Postfix)));
                MethodExportLayoutPatch.Apply(harmony);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CustomUdonSharp] Failed to apply patch: {e}");
            }
        }

        private static void Postfix(object __result)
        {
            try
            {
                if (__result is Array bindings)
                    InterfaceLowering.Process(bindings, _treeField, _sourceTextField);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CustomUdonSharp] Interface lowering failed, compiling original sources: {e}");
            }
        }
    }
}
