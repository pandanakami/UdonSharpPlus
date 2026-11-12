using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UdonSharpPlus.Tests
{
    /// <summary>型を書き換えたフィールドを、U# がプロキシと Udon の変数の間で写さないこと（Redmine #60）</summary>
    public class LoweredFieldSerializerTests
    {
        // ソース上の汎用クラス・インタフェース（このテストのアセンブリもソースからコンパイルされる）
        public interface ISample { }
        public class Sample : ISample { }
        public class Box<T> { }
        public class SampleBehaviour : MonoBehaviour { }

        private static IEnumerable<TestCaseData> Lowered()
        {
            yield return new TestCaseData(typeof(List<string>), "VRC.SDK3.Data.DataList");
            yield return new TestCaseData(typeof(Dictionary<string, int>), "VRC.SDK3.Data.DataDictionary");
            yield return new TestCaseData(typeof(List<Sample>), "VRC.SDK3.Data.DataList");
            yield return new TestCaseData(typeof(KeyValuePair<string, int>), "System.Object[]");
            yield return new TestCaseData(typeof(Sample), "System.Object[]");
            yield return new TestCaseData(typeof(Box<int>), "System.Object[]");
            yield return new TestCaseData(typeof(ISample), "System.Object");
            yield return new TestCaseData(typeof(Sample[]), "System.Object[][]");
            yield return new TestCaseData(typeof(ISample[]), "System.Object[]");
            yield return new TestCaseData(typeof(List<int>[]), "VRC.SDK3.Data.DataList[]");
        }

        [TestCaseSource(nameof(Lowered))]
        public void UsesNoOpSerializer(Type type, string udonType)
        {
            object serializer = CreatePooled(type);
            Assert.That(serializer.GetType().Name, Does.StartWith("LoweredFieldSerializer"));
            Type storage = (Type)serializer.GetType().GetMethod("GetUdonStorageType").Invoke(serializer, null);
            Assert.That(storage.FullName, Is.EqualTo(udonType));
        }

        /// <summary>
        /// U# のコンパイルの並列処理（ワーカースレッド）から呼ばれても、例外にならない（#112）。
        /// ドメインリロード直後と同じく、キャッシュを作り直したあと、最初の呼び出しをワーカースレッドで行う
        /// </summary>
        [Test]
        public void WorksFromWorkerThreadAfterLoad()
        {
            LoweredFieldSerializerPatch.LoadSourceAssemblies();   // パッチを当てるとき（メインスレッド）と同じ
            Type storage = null;
            Exception error = null;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { storage = LoweredFieldSerializerPatch.UdonStorageType(typeof(Sample)); }
                catch (Exception e) { error = e; }
            }).Wait();
            Assert.That(error, Is.Null);
            Assert.That(storage, Is.EqualTo(typeof(object[])));
        }

        [TestCase(typeof(int))]
        [TestCase(typeof(string))]
        [TestCase(typeof(int[]))]
        [TestCase(typeof(GameObject))]
        [TestCase(typeof(SampleBehaviour))]
        [TestCase(typeof(IDisposable))]
        public void KeepsUdonSharpSerializer(Type type)
        {
            object serializer = CreatePooled(type);
            Assert.That(serializer.GetType().Name, Does.Not.StartWith("LoweredFieldSerializer"));
        }

        /// <summary>U# の Serializer.CreatePooled(Type)（テストのアセンブリは U# を参照しないので、リフレクションで呼ぶ）</summary>
        private static object CreatePooled(Type type)
        {
            Type serializerType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                serializerType = serializerType ?? assembly.GetType("UdonSharp.Serialization.Serializer");
            MethodInfo create = serializerType.GetMethod("CreatePooled", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
            return create.Invoke(null, new object[] { type });
        }
    }
}
