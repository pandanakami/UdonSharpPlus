using System;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// UdonSharp の自動アップグレード（UdonSharpUpgrader.UpgradeScripts）が読み込むソースは、本パッケージで書き換えない（Redmine #84）。
    ///
    /// アップグレードは、版の古いプログラムアセット（新しく作ったものは必ず該当）のスクリプトについて、
    /// CompilationContext.LoadSyntaxTreesAndCreateModules で読んだ tree を見てフィールドに [OdinSerialize] を足し、
    /// tree 全体をスクリプトのファイルに書き戻す。本パッケージはそこで tree を書き換えるので、そのままだと
    /// 書き換え後のコード（インタフェース型が object になる、など）がユーザーのソースに書き込まれてしまう。
    ///
    /// UpgradeScripts は最初に 1 回だけ読み込み、最後に自分の中から UdonSharp のコンパイル（CompileSync）を呼ぶ。
    /// そのコンパイルでは書き換える必要があるので、止めるのは UpgradeScripts に入ってから最初の読み込み 1 回だけにする
    /// （読み込む前に抜けたときは、抜けるときに解除する）。
    ///
    /// 書き換えないので、アップグレードには UdonSharp が知らない書き方（ジェネリッククラスの T 型のフィールドなど）が見える。
    /// アップグレードはフィールドの型を TypeSymbol.TryGetSystemType に渡し、T（INamedTypeSymbol でない型）で
    /// InvalidCastException になる。例外で抜けるとアップグレードの印が消えず、毎フレーム繰り返す（#102）。そこで、
    ///   - アップグレードの間は、TryGetSystemType が INamedTypeSymbol でない型に false を返すようにする（UdonSharp の
    ///     「取れなければ飛ばす」という意図どおり。アップグレードはそのフィールドに手を付けない）
    ///   - それでも例外で抜けたときは、アップグレードの印を消して、毎フレーム繰り返さないようにする（エラーは 1 回だけ出る）
    /// </summary>
    internal static class UpgradeGuardPatch
    {
        private static bool _skipNextLoad;
        private static int _upgradeDepth;

        /// <summary>UpgradeScripts に入った回数（パッチが当たっているかをテストで確かめる）</summary>
        internal static int EnteredCount { get; private set; }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(UdonSharpInternals.UpgradeScripts,
                prefix: new HarmonyMethod(typeof(UpgradeGuardPatch), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(UpgradeGuardPatch), nameof(Finalizer)));
            harmony.Patch(UdonSharpInternals.TryGetSystemType,
                prefix: new HarmonyMethod(typeof(UpgradeGuardPatch), nameof(TryGetSystemTypePrefix)));
        }

        /// <summary>次の読み込みを書き換えないなら true を返し、印を消す</summary>
        public static bool ConsumeSkip()
        {
            if (!_skipNextLoad)
                return false;
            _skipNextLoad = false;
            return true;
        }

        private static void Prefix()
        {
            EnteredCount++;
            _upgradeDepth++;
            _skipNextLoad = true;
        }

        // 例外を握りつぶさない（返した例外がそのまま投げられる。null なら投げない）
        private static Exception Finalizer(Exception __exception)
        {
            _upgradeDepth--;
            _skipNextLoad = false;
            if (__exception != null)
                StopUpgradeLoop(__exception);
            return __exception;
        }

        /// <summary>アップグレードが例外で抜けたとき、毎フレームやり直さないよう、アップグレードの印を消す</summary>
        private static void StopUpgradeLoop(Exception exception)
        {
            try
            {
                UdonSharpInternals.NeedsProgramUpgradePass.SetValue(null, false);
                object cache = UdonSharpInternals.EditorCacheInstance.GetValue(null);
                if (cache != null)
                    UdonSharpInternals.ClearUpgradePassQueue.Invoke(cache, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UdonSharpPlus] UdonSharp の自動アップグレードの印を消せませんでした：{e}");
            }
            Debug.LogError("[UdonSharpPlus] UdonSharp の自動アップグレードが例外で止まりました（" + exception.GetType().Name + "）。" +
                           "毎フレームやり直さないよう、アップグレードを止めました。メニューの VRChat SDK > Udon Sharp > Force Upgrade でやり直せます");
        }

        /// <summary>
        /// アップグレードの間だけ：INamedTypeSymbol でない型（ジェネリッククラスの T など）は、例外にせず「取れない」（false）にする
        /// </summary>
        private static bool TryGetSystemTypePrefix(ITypeSymbol typeSymbol, ref Type systemType, ref bool __result)
        {
            if (_upgradeDepth <= 0 || typeSymbol == null)
                return true;

            ITypeSymbol element = typeSymbol;
            while (element is IArrayTypeSymbol array)
                element = array.ElementType;
            if (element is INamedTypeSymbol)
                return true;

            systemType = null;
            __result = false;
            return false;
        }
    }
}
