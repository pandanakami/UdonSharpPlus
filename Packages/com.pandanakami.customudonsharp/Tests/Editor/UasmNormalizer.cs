using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomUdonSharp.Tests
{
    /// <summary>
    /// UdonAssembly（AssemblyCode）から、コードのアドレスに依存する部分を取り除く（Redmine #43）。
    ///
    ///   JUMP, 0x000000D8         → JUMP, &lt;__if_Sample_IDamageable__is(object)&gt;   （クラス内のメソッド呼び出し）
    ///   JUMP_IF_FALSE, 0x00000130 → JUMP_IF_FALSE, L2                           （メソッド内の連番ラベル。分岐先に "L2:" を挿入）
    ///   PUSH, __const_SystemString_1 → PUSH, __const_SystemString_1  ; "Damage__int__string__p0"
    ///   PUSH, __gintnl_SystemUInt32_0 → PUSH, __gintnl_SystemUInt32_0  ; -> L3     （戻り先の番地）
    ///
    /// ラベルはメソッドごとにアドレス順で振るので、命令が増減しても番号が変わるのはそのメソッドの中だけ。
    /// （まずラベルで運用し、変化が多いようならジャンプ先自体を捨てる。#43 の決定事項）
    /// </summary>
    internal static class UasmNormalizer
    {
        // Udon の命令ごとのバイト数（VRC.Udon.VM.Common.OpCode）
        private static readonly Dictionary<string, uint> InstructionSizes = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["NOP"] = 4,
            ["PUSH"] = 8,
            ["POP"] = 4,
            ["JUMP_IF_FALSE"] = 8,
            ["JUMP"] = 8,
            ["EXTERN"] = 8,
            ["ANNOTATION"] = 8,
            ["JUMP_INDIRECT"] = 8,
            ["COPY"] = 4,
        };

        // U# がメソッドの先頭に出す見出しコメント（"# " / "# Ns.Type.Method(args)" / "# "）
        private static readonly Regex HeaderComment = new Regex(@"^#\s(?!Calling\s)(?<name>\S.*\))\s*$", RegexOptions.Compiled);

        private sealed class Line
        {
            public string Text;          // 元の行（右端の空白と \r を除いたもの）
            public string Opcode;        // 命令なら命令名
            public string Operand;       // 命令の引数
            public uint Address;         // 命令のアドレス
            public int Region = -1;      // 所属するメソッド（ラベル番号の単位）
        }

        private sealed class Region
        {
            public string Name;          // 見出しコメントのメソッド名（なければ export ラベル）
            public uint Start;
            public readonly SortedDictionary<uint, string> Labels = new SortedDictionary<uint, string>();
        }

        public sealed class Result
        {
            public string Uasm;

            /// <summary>コードのアドレスを「メソッド名:ラベル」で表す（ヒープの初期値の戻り先番地用）。該当がなければ null</summary>
            public Func<uint, string> DescribeAddress;
        }

        public static Result Normalize(string uasm, IReadOnlyDictionary<string, string> heapDefaults)
        {
            List<Line> lines = uasm.Replace("\r\n", "\n").Split('\n').Select(t => new Line { Text = t.TrimEnd() }).ToList();

            int codeStart = lines.FindIndex(l => l.Text.Trim() == ".code_start");
            int codeEnd = lines.FindIndex(l => l.Text.Trim() == ".code_end");
            if (codeStart < 0 || codeEnd < codeStart)
                return new Result { Uasm = string.Join("\n", lines.Select(l => l.Text)), DescribeAddress = _ => null };

            // ---- 1 周目：アドレスとメソッドの区切り ----------------------------------------
            var regions = new List<Region>();
            var methodStarts = new Dictionary<uint, string>();   // JUMP で呼ばれるアドレス → メソッド名
            uint address = 0;
            bool regionOpenedByLabel = false;   // export ラベルで始まり、まだ見出しコメントが来ていない
            string pendingHeader = null;        // 見出しの直後の命令がメソッドの入口

            for (int i = codeStart + 1; i < codeEnd; i++)
            {
                Line line = lines[i];
                string text = line.Text.Trim();

                if (text.Length == 0 || text.StartsWith(".export", StringComparison.Ordinal))
                    continue;

                if (text.StartsWith("#", StringComparison.Ordinal))
                {
                    Match header = HeaderComment.Match(text);
                    if (header.Success && IsHeaderBlock(lines, i))
                    {
                        string name = ShortMethodName(header.Groups["name"].Value);
                        if (regionOpenedByLabel)
                        {
                            regions[regions.Count - 1].Name = name;
                            regionOpenedByLabel = false;
                        }
                        else
                        {
                            regions.Add(new Region { Name = name, Start = address });
                        }
                        pendingHeader = name;
                    }
                    continue;
                }

                if (text.EndsWith(":", StringComparison.Ordinal) && !text.Contains(","))
                {
                    regions.Add(new Region { Name = text.TrimEnd(':'), Start = address });
                    regionOpenedByLabel = true;
                    continue;
                }

                int comma = text.IndexOf(',');
                string opcode = (comma < 0 ? text : text.Substring(0, comma)).Trim();
                if (!InstructionSizes.TryGetValue(opcode, out uint size))
                    continue;   // 未知の行はそのまま出す

                line.Opcode = opcode;
                line.Operand = comma < 0 ? null : text.Substring(comma + 1).Trim();
                line.Address = address;
                line.Region = Math.Max(0, regions.Count - 1);
                if (regions.Count == 0)
                    regions.Add(new Region { Name = "<top>", Start = 0 });

                if (pendingHeader != null)
                {
                    methodStarts[address] = pendingHeader;
                    pendingHeader = null;
                }

                address += size;
            }

            uint codeSize = address;

            int RegionOf(uint target)
            {
                int found = 0;
                for (int r = 0; r < regions.Count; r++)
                    if (regions[r].Start <= target)
                        found = r;
                return found;
            }

            bool IsCodeAddress(uint target) => target <= codeSize;

            void AddLabel(uint target)
            {
                if (!IsCodeAddress(target))
                    return;
                Region region = regions[RegionOf(target)];
                if (!region.Labels.ContainsKey(target))
                    region.Labels[target] = null;
            }

            // ---- 2 周目：ジャンプ先と戻り先にラベルを付ける ----------------------------------
            foreach (Line line in lines.Where(l => l.Opcode == "JUMP" || l.Opcode == "JUMP_IF_FALSE"))
                if (TryParseAddress(line.Operand, out uint target) && !(line.Opcode == "JUMP" && methodStarts.ContainsKey(target)))
                    AddLabel(target);

            foreach (var pair in heapDefaults)
                if (IsReturnAddressSymbol(pair.Key) && uint.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint target))
                    AddLabel(target);

            foreach (Region region in regions)
            {
                int n = 0;
                foreach (uint key in region.Labels.Keys.ToList())
                    region.Labels[key] = "L" + n++;
            }

            string LabelOf(uint target) =>
                IsCodeAddress(target) && regions[RegionOf(target)].Labels.TryGetValue(target, out string label) ? label : null;

            // ---- 出力 -----------------------------------------------------------------------
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];

                if (line.Opcode == null)
                {
                    sb.Append(line.Text).Append('\n');
                    continue;
                }

                string indent = line.Text.Substring(0, line.Text.Length - line.Text.TrimStart().Length);
                string label = LabelOf(line.Address);
                if (label != null)
                    sb.Append(indent.Length >= 4 ? indent.Substring(4) : indent).Append(label).Append(":\n");

                sb.Append(indent).Append(line.Opcode);
                if (line.Operand != null)
                {
                    sb.Append(", ");
                    if ((line.Opcode == "JUMP" || line.Opcode == "JUMP_IF_FALSE") && TryParseAddress(line.Operand, out uint target))
                    {
                        if (line.Opcode == "JUMP" && methodStarts.TryGetValue(target, out string method))
                            sb.Append('<').Append(method).Append('>');
                        else
                            sb.Append(LabelOf(target) ?? line.Operand);
                    }
                    else
                    {
                        sb.Append(line.Operand);
                        if (line.Opcode == "PUSH" && heapDefaults.TryGetValue(line.Operand, out string value))
                        {
                            if (IsReturnAddressSymbol(line.Operand) && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint ret))
                                sb.Append("  ; -> ").Append(LabelOf(ret) ?? value);
                            else if (line.Operand.StartsWith("__const_", StringComparison.Ordinal))
                                sb.Append("  ; ").Append(value);
                        }
                    }
                }
                sb.Append('\n');
            }

            // 末尾の 1 個の改行は元の Split で付いた分なので落とす
            string normalized = sb.ToString();
            if (normalized.EndsWith("\n", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);

            return new Result
            {
                Uasm = normalized,
                DescribeAddress = target =>
                {
                    string l = LabelOf(target);
                    return l == null ? null : $"{regions[RegionOf(target)].Name}:{l}";
                },
            };
        }

        /// <summary>U# がメソッド呼び出しの戻り先番地を入れておく変数（__gintnl_SystemUInt32_N）</summary>
        internal static bool IsReturnAddressSymbol(string symbol) =>
            symbol.StartsWith("__gintnl_SystemUInt32_", StringComparison.Ordinal);

        // "# " / "# Name(...)" / "# " の 3 行組か
        private static bool IsHeaderBlock(List<Line> lines, int index) =>
            index > 0 && index + 1 < lines.Count
            && lines[index - 1].Text.Trim() == "#"
            && lines[index + 1].Text.Trim() == "#";

        /// <summary>"Sample.Attacker.__if_Foo__call(UdonSharp.UdonSharpBehaviour, int)" → "__if_Foo__call(UdonSharp.UdonSharpBehaviour, int)"</summary>
        private static string ShortMethodName(string fullName)
        {
            int paren = fullName.IndexOf('(');
            string head = paren < 0 ? fullName : fullName.Substring(0, paren);
            string tail = paren < 0 ? "" : fullName.Substring(paren);
            int dot = head.LastIndexOf('.');
            return (dot < 0 ? head : head.Substring(dot + 1)) + tail;
        }

        private static bool TryParseAddress(string operand, out uint address)
        {
            address = 0;
            return operand != null
                   && operand.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                   && uint.TryParse(operand.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }
    }
}
