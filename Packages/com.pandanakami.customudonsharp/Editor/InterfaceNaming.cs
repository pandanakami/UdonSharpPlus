using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CustomUdonSharp
{
    /// <summary>
    /// インタフェース実装メソッドの Udon 上の名前を決める。
    ///
    ///   int Damage(int amount, string attacker)
    ///     メソッド : Damage                     （元の名前。同じクラスでの同名メソッドとの共存は禁止。#51）
    ///     引数     : Damage__int__string__p0, __p1   （実装側で引数名を変えても揃うよう位置で付ける）
    ///     戻り値   : Damage__int__string__ret
    ///
    /// U# の通常の名前は "__{連番}_" で始まるので、この形式とは衝突しない。
    /// </summary>
    internal static class InterfaceNaming
    {
        /// <summary>
        /// 名前を差し替える対象メソッドのキー（"含む型::シグネチャ名"）。
        /// SyntaxTree 書き換え時（LoadSyntaxTreesAndCreateModules の Postfix）に毎回作り直される。
        /// </summary>
        private static volatile HashSet<string> _implementationKeys = new HashSet<string>();

        public static HashSet<string> ImplementationKeys
        {
            get => _implementationKeys;
            set => _implementationKeys = value ?? new HashSet<string>();
        }

        /// <param name="isInterfaceToErase">U# 用に object へ置き換えられるインタフェースかどうか。
        /// 書き換え前のシンボルから名前を作るときも、書き換え後と同じ名前になるようにするため。
        /// 汎用クラス（#49）は、いつも object[] として名前を作る（インタフェースの段の後で object[] に置き換わるため。#50）</param>
        public static string SignatureName(IMethodSymbol method, Func<INamedTypeSymbol, bool> isInterfaceToErase)
        {
            if (method.Parameters.Length == 0)
                return method.Name;

            return method.Name + "__" + string.Join("__", method.Parameters.Select(p => ParameterToken(p, isInterfaceToErase)));
        }

        public static string ParameterName(string signatureName, int index) => $"{signatureName}__p{index}";

        public static string ReturnName(string signatureName) => signatureName + "__ret";

        public static string Key(IMethodSymbol method, Func<INamedTypeSymbol, bool> isInterfaceToErase) =>
            TypeToken(method.ContainingType, _ => false) + "::" + SignatureName(method, isInterfaceToErase);

        private static string ParameterToken(IParameterSymbol parameter, Func<INamedTypeSymbol, bool> isInterfaceToErase)
        {
            string prefix = parameter.RefKind == RefKind.None ? "" : parameter.RefKind.ToString().ToLowerInvariant() + "_";
            return prefix + TypeToken(parameter.Type, isInterfaceToErase);
        }

        private static string TypeToken(ITypeSymbol type, Func<INamedTypeSymbol, bool> isInterfaceToErase)
        {
            if (type is IArrayTypeSymbol array)
                return TypeToken(array.ElementType, isInterfaceToErase) + "Array" + (array.Rank > 1 ? array.Rank.ToString() : "");

            if (type is INamedTypeSymbol named && isInterfaceToErase(named))
                return "object";

            // 汎用クラスは object[]（ClassLowering.InstanceType）になる
            if (type is INamedTypeSymbol cls && cls.Locations.Any(l => l.IsInSource) && ClassLowering.IsPlainClass(cls))
                return "objectArray";

            // 完全修飾名（int/string 等は C# キーワード表記）を識別子に使える文字へ
            string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
            return Sanitize(name);
        }

        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
