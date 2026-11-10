using System.Text;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// インタフェース経由の呼び出しを、型番号の switch（ジャンプテーブル）にするかどうかの境界（Redmine #79 の観点 j、#65）。
    /// 実装する普通のクラスが 4 個以上で、型番号がどれも 256 以下なら switch。3 個以下か、型番号が 256 を超えるなら型タグを順に比べる。
    /// 型番号は 0 から振るので、1 つのインタフェースだけなら、257 個（0〜256）までが switch、258 個からが順に比べる形になる
    /// </summary>
    public class InterfaceSwitchBoundaryTests
    {
        // U# の switch のジャンプテーブル：表（uint[]）から飛び先を引いて JUMP_INDIRECT する
        private const string JumpTableLookup = "SystemUInt32Array.__Get__SystemInt32__SystemUInt32";

        private static TestSource Source(int classCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using UdonSharp;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("namespace UdonSharpPlusTests.SwitchBoundary");
            sb.AppendLine("{");
            sb.AppendLine("    public interface IUnit { int Hp(); }");
            for (int i = 0; i < classCount; i++)
                sb.AppendLine($"    public class C{i} : IUnit {{ public int Hp() {{ return {i}; }} }}");
            sb.AppendLine("    public class User : UdonSharpBehaviour");
            sb.AppendLine("    {");
            sb.AppendLine("        private void Start()");
            sb.AppendLine("        {");
            sb.Append("            IUnit[] units = { ");
            for (int i = 0; i < classCount; i++)
                sb.Append($"new C{i}(), ");
            sb.AppendLine("};");
            sb.AppendLine("            int sum = 0;");
            sb.AppendLine("            foreach (IUnit u in units) sum += u.Hp();");
            sb.AppendLine("            Debug.Log(sum);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return new TestSource("User.cs", sb.ToString());
        }

        [TestCase(3, false)]
        [TestCase(4, true)]
        [TestCase(257, true)]
        [TestCase(258, false)]
        public void UsesJumpTableOnlyWithinBounds(int classCount, bool jumpTable)
        {
            CompileResult result = UdonSharpTestCompiler.Compile(Source(classCount));

            Assert.That(result.Diagnostics, Is.Empty);
            string uasm = result["User"].Uasm;
            Assert.That(uasm.Contains(JumpTableLookup), Is.EqualTo(jumpTable),
                jumpTable ? $"実装クラス {classCount} 個で switch になっていません" : $"実装クラス {classCount} 個で switch になっています");
        }
    }
}
