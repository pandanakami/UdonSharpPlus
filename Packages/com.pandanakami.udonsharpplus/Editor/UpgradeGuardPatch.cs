using HarmonyLib;

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
    /// </summary>
    internal static class UpgradeGuardPatch
    {
        private static bool _skipNextLoad;

        /// <summary>UpgradeScripts に入った回数（パッチが当たっているかをテストで確かめる）</summary>
        internal static int EnteredCount { get; private set; }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(UdonSharpInternals.UpgradeScripts,
                prefix: new HarmonyMethod(typeof(UpgradeGuardPatch), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(UpgradeGuardPatch), nameof(Finalizer)));
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
            _skipNextLoad = true;
        }

        // 例外を握りつぶさない（null を返すと元の例外がそのまま投げられる）
        private static System.Exception Finalizer(System.Exception __exception)
        {
            _skipNextLoad = false;
            return __exception;
        }
    }
}
