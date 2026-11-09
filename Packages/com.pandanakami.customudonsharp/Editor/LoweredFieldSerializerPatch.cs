using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UdonSharp.Serialization;
using UnityEditor.Compilation;
using UnityEngine;

namespace CustomUdonSharp
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
        private static HashSet<string> _sourceAssemblies;

        public static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Serializer), "Create", new[] { typeof(TypeSerializationMetadata) });
            if (target == null)
            {
                Debug.LogError("[CustomUdonSharp] Serializer.Create not found. UdonSharp internals may have changed.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(LoweredFieldSerializerPatch), nameof(Prefix)));
        }

        private static bool Prefix(TypeSerializationMetadata typeMetadata, ref Serializer __result)
        {
            Type type = typeMetadata?.cSharpType;
            if (type == null || UdonStorageType(type) == null)
                return true;

            __result = (Serializer)Activator.CreateInstance(typeof(LoweredFieldSerializer<>).MakeGenericType(type), typeMetadata);
            return false;
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
                    return AccessTools.TypeByName("VRC.SDK3.Data.DataList");
                if (definition == typeof(Dictionary<,>))
                    return AccessTools.TypeByName("VRC.SDK3.Data.DataDictionary");
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
                _sourceAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies().Select(a => a.name));
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
