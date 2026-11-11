using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// 本パッケージが使う UdonSharp の内部 API が全部見つかり、パッチが当たっていること（Redmine #39）。
    /// VRChat SDK を上げたときは、まずこのテストで内部 API の変化を確かめる（DEVELOPMENT.md）
    /// </summary>
    public class UdonSharpInternalsTests
    {
        [Test]
        public void AllInternalsFound()
        {
            bool found = UdonSharpInternals.Resolve();
            Assert.That(UdonSharpInternals.Missing, Is.Empty,
                $"VRChat SDK {UdonSharpInterfacePatch.SdkVersion()} で見つからない UdonSharp の内部 API があります");
            Assert.That(found, Is.True);
        }

        [Test]
        public void PatchesApplied()
        {
            Assert.That(UdonSharpInterfacePatch.Enabled, Is.True, "パッチが当たっていません（コンソールの [UdonSharpPlus] のエラーを見てください）");
        }

        /// <summary>
        /// UdonSharp の自動アップグレードに入ると印が立ち、読み込む前に抜けても印が残らない（#84）。
        /// 残ると、次のコンパイルで本パッケージの書き換えが行われなくなる
        /// </summary>
        [Test]
        public void UpgradeGuardClearsWhenUpgradeReturnsEarly()
        {
            int before = UpgradeGuardPatch.EnteredCount;
            var empty = System.Array.CreateInstance(UdonSharpInternals.UpgradeScripts.GetParameters()[0].ParameterType.GetElementType(), 0);
            UdonSharpInternals.UpgradeScripts.Invoke(null, new object[] { empty });   // 空なら読み込む前に false で抜ける

            Assert.That(UpgradeGuardPatch.EnteredCount, Is.EqualTo(before + 1), "UdonSharpUpgrader.UpgradeScripts にパッチが当たっていません");
            Assert.That(UpgradeGuardPatch.ConsumeSkip(), Is.False, "アップグレードを抜けたあとも、書き換えを止める印が残っています");
        }

        // ---- #102 ------------------------------------------------------------------------------

        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;

        private static void EnterUpgrade() => typeof(UpgradeGuardPatch).GetMethod("Prefix", Static).Invoke(null, null);

        private static object LeaveUpgrade(Exception exception) =>
            typeof(UpgradeGuardPatch).GetMethod("Finalizer", Static).Invoke(null, new object[] { exception });

        /// <summary>ジェネリッククラスの T 型・T[] 型のフィールドの型</summary>
        private static ITypeSymbol[] TypeParameterFieldTypes()
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText("class Box<T> { public T value; public T[] values; }");
            var compilation = CSharpCompilation.Create("UpgradeGuardTypeParameter", new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
            INamedTypeSymbol box = compilation.GetTypeByMetadataName("Box`1");
            return box.GetMembers().OfType<IFieldSymbol>().Select(f => f.Type).ToArray();
        }

        private static bool TryGetSystemType(ITypeSymbol type)
        {
            var args = new object[] { type, null };
            return (bool)UdonSharpInternals.TryGetSystemType.Invoke(null, args);
        }

        /// <summary>
        /// アップグレードの間は、INamedTypeSymbol でない型（T）を UdonSharp の TryGetSystemType に渡しても、例外にならず false になる。
        /// 例外になると、アップグレードが毎フレーム例外を出し続ける（#102）
        /// </summary>
        [Test]
        public void TryGetSystemTypeSkipsTypeParameterDuringUpgrade()
        {
            ITypeSymbol[] types = TypeParameterFieldTypes();
            EnterUpgrade();
            try
            {
                foreach (ITypeSymbol type in types)
                    Assert.That(TryGetSystemType(type), Is.False, type.ToDisplayString());
            }
            finally
            {
                LeaveUpgrade(null);
            }
        }

        /// <summary>アップグレードの外では、UdonSharp の元の動きのまま（書き換え後のソースには T 型が残らないので、変えない）</summary>
        [Test]
        public void TryGetSystemTypeUnchangedOutsideUpgrade()
        {
            ITypeSymbol type = TypeParameterFieldTypes()[0];
            var e = Assert.Throws<TargetInvocationException>(() => TryGetSystemType(type));
            Assert.That(e.InnerException, Is.InstanceOf<InvalidCastException>());
        }

        /// <summary>アップグレードが例外で抜けたら、アップグレードの印を消して、毎フレームやり直さないようにする（#102）</summary>
        [Test]
        public void UpgradeExceptionStopsUpgradeLoop()
        {
            object cache = UdonSharpInternals.EditorCacheInstance.GetValue(null);
            cache.GetType().GetMethod("QueueUpgradePass").Invoke(cache, null);
            UdonSharpInternals.NeedsProgramUpgradePass.SetValue(null, true);

            LogAssert.Expect(LogType.Error, new Regex("自動アップグレードが例外で止まりました"));
            EnterUpgrade();
            var thrown = new InvalidCastException("test");
            object returned = LeaveUpgrade(thrown);

            Assert.That(returned, Is.SameAs(thrown), "例外を握りつぶしています");
            Assert.That(UdonSharpInternals.NeedsProgramUpgradePass.GetValue(null), Is.False, "UdonSharpUpgrader._needsProgramUpgradePass が残っています");
            object info = cache.GetType().GetProperty("Info").GetValue(cache);
            Assert.That(info.GetType().GetField("projectNeedsUpgrade").GetValue(info), Is.False, "UdonSharpEditorCache の projectNeedsUpgrade が残っています");
            Assert.That(UpgradeGuardPatch.ConsumeSkip(), Is.False);
        }
    }
}
