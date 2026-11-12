using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UdonSharp.Serialization;
using UnityEditor.Compilation;
using UnityEngine;

namespace UdonSharpPlus
{
    /// <summary>
    /// 型を書き換えたフィールド（List・Dictionary（#56）、汎用クラス（#49）、インタフェース（#50）と、それらの配列）を、
    /// U# がエディタ上の C# のコンポーネント（プロキシ）と Udon の変数の間で写さないようにする（Redmine #60）。
    ///
    /// U# はフィールドの C# の型から写し方（Serializer）を決めるが、Udon 側の変数は書き換え後の型（DataList・object[]・object）なので、
    /// 既定の写し方の型チェックで「not compatible with serializer」のエラーが Play モードに入るたびに出る。
    /// Serializer.Create に Prefix を当て、これらの型には何もしない写し方を返す。
    /// （値は Udon 側の初期化子・コードで作られるので、写さなくても動作は変わらない）
    /// </summary>
    internal static class LoweredFieldSerializerPatch
    {
        /// <summary>
        /// ソースからコンパイルされたアセンブリの名前。パッチを当てるとき（[InitializeOnLoad]、メインスレッド）に作る。
        /// Serializer.Create は U# のコンパイルの並列処理（ワーカースレッド）からも呼ばれ、
        /// CompilationPipeline.GetAssemblies はメインスレッドでしか呼べないため、最初に使うときに作ってはいけない（#112）。
        /// アセンブリの構成が変わるのはスクリプトのコンパイルのあとで、そのときはドメインリロードで作り直される
        /// </summary>
        private static HashSet<string> _sourceAssemblies;

        internal static void LoadSourceAssemblies()
        {
            _sourceAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies().Select(a => a.name));
        }

        public static void Apply(Harmony harmony)
        {
            LoadSourceAssemblies();
            harmony.Patch(UdonSharpInternals.SerializerCreate, prefix: new HarmonyMethod(typeof(LoweredFieldSerializerPatch), nameof(Prefix)));
        }

        private static int _warned;

        private static bool Prefix(TypeSerializationMetadata typeMetadata, ref Serializer __result)
        {
            try
            {
                Type type = typeMetadata?.cSharpType;
                if (type == null || UdonStorageType(type) == null)
                    return true;

                __result = (Serializer)Activator.CreateInstance(typeof(LoweredFieldSerializer<>).MakeGenericType(type), typeMetadata);
                return false;
            }
            catch (Exception e)
            {
                // U# のコンパイルの並列処理の中からも呼ばれるので、例外を外に出すとコンパイル全体が失敗する（#116）。
                // U# の元の写し方に戻す（型を書き換えたフィールドでは、Play モードに入るときに「not compatible with serializer」が出うる）
                if (System.Threading.Interlocked.Exchange(ref _warned, 1) == 0)
                    Debug.LogWarning($"[UdonSharpPlus] フィールドの写し方を決めるところで例外が起きたため、UdonSharp の元の写し方に戻しました：{e.GetType().Name}: {e.Message}");
                return true;
            }
        }

        /// <summary>書き換え後の Udon 側の型。書き換えない型なら null</summary>
        internal static Type UdonStorageType(Type type)
        {
            if (type.IsArray)
            {
                Type element = UdonStorageType(type.GetElementType());
                return element == null ? null : element.MakeArrayType();
            }

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>))
                    return UdonSharpInternals.DataList;
                if (definition == typeof(Dictionary<,>))
                    return UdonSharpInternals.DataDictionary;
                if (definition == typeof(KeyValuePair<,>))
                    return typeof(object[]);
            }

            if (!IsFromSource(type))
                return null;
            if (type.IsInterface)
                return typeof(object);
            if (IsPlainClass(type))
                return typeof(object[]);
            return null;
        }

        /// <summary>汎用クラスか：基底をたどって、object までソースのクラスだけが続くもの（ClassLowering.IsPlainClass と同じ考え方）</summary>
        private static bool IsPlainClass(Type type)
        {
            if (!type.IsClass || typeof(Delegate).IsAssignableFrom(type))
                return false;
            for (Type t = type.BaseType; t != null; t = t.BaseType)
            {
                if (t == typeof(object))
                    return true;
                if (!IsFromSource(t))
                    return false;
            }
            return false;
        }

        /// <summary>プロジェクトのソースからコンパイルされたアセンブリの型か（U# が書き換えるのはソース上の型だけ）</summary>
        private static bool IsFromSource(Type type)
        {
            if (_sourceAssemblies == null)
                LoadSourceAssemblies();   // Apply の前（テストなど）。メインスレッドのときだけ
            return _sourceAssemblies.Contains(type.Assembly.GetName().Name);
        }
    }

    /// <summary>何もしない写し方（#60）。C# 側のフィールドも Udon 側の変数も変えない</summary>
    internal sealed class LoweredFieldSerializer<T> : Serializer<T>
    {
        public LoweredFieldSerializer(TypeSerializationMetadata typeMetadata) : base(typeMetadata)
        {
        }

        public override Type GetUdonStorageType() => LoweredFieldSerializerPatch.UdonStorageType(typeof(T)) ?? typeof(object);

        protected override bool HandlesTypeSerialization(TypeSerializationMetadata typeMetadata) => false;

        protected override Serializer MakeSerializer(TypeSerializationMetadata typeMetadata) =>
            (Serializer)Activator.CreateInstance(typeof(LoweredFieldSerializer<>).MakeGenericType(typeMetadata.cSharpType), typeMetadata);

        public override void Write(IValueStorage targetObject, in T sourceObject)
        {
        }

        public override void Read(ref T targetObject, IValueStorage sourceObject)
        {
        }
    }
}
