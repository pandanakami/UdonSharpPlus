using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// デリゲートの追加テスト（Redmine #126）：継承・partial・[NetworkCallable]・オーバーロード・外部メソッド・名前空間・
    /// フィールドの既定値（#127）・自動アップグレード。動きは FeatureTests の DelegateCombo パネルで確かめる
    /// </summary>
    public class DelegateMoreTests
    {
        private static CompileResult Compile(params TestSource[] sources) => UdonSharpTestCompiler.Compile(sources);

        private static string Dump(string name, params TestSource[] sources)
        {
            string dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusDelegateMore_" + name));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            try
            {
                CompileResult r = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, dir, sources);
                Assert.That(r.Diagnostics, Is.Empty);
                return string.Join("\n", sources.Select(s => File.ReadAllText(Path.Combine(dir, "Assets", "UdonSharpPlusTests", s.FileName))));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        private const string Head = "using UdonSharp;\nusing UnityEngine;\n";

        // ---- A7 継承 -----------------------------------------------------------

        /// <summary>基底と派生の両方で同じデリゲート型を作るとき、引数・戻り値のフィールドは基底にだけ置く（派生は継承したものを使う）</summary>
        [Test]
        public void InheritedTrampolineFieldsAreDeclaredOnceInBase()
        {
            const string types = "public delegate int Calc(int x);\npublic delegate int NoArgs();";
            const string b = Head + "public abstract class Base : UdonSharpBehaviour { public virtual int V(int x) => x; public int Make() { Calc c = V; return c(1); } }";
            const string d = Head + "public class Derived : Base { public override int V(int x) => x + 1; int S() => 7; void Start() { Calc c = V; NoArgs n = S; Debug.Log(c(1) + n()); } }";
            string src = Dump("Inherit", new TestSource("Types.cs", types), new TestSource("Base.cs", b), new TestSource("Derived.cs", d));
            string baseSrc = src.Split(new[] { "public class Derived" }, StringSplitOptions.None)[0];
            string derivedSrc = src.Substring(src.IndexOf("public class Derived", StringComparison.Ordinal));
            Assert.That(baseSrc, Does.Contain("protected int __dlg_Calc__p0;"));
            Assert.That(derivedSrc, Does.Not.Contain("protected int __dlg_Calc__p0;"), "派生では Calc のフィールドを継承したものを使う");
            Assert.That(derivedSrc, Does.Contain("protected int __dlg_NoArgs__ret;"), "派生だけが使う型のフィールドは派生に置く");
            // 中継メソッドの名前にクラスの完全名が入るので、基底と派生で重ならない
            Assert.That(baseSrc, Does.Contain("public void __dlg_Calc__Base_V__Base()"));
            Assert.That(derivedSrc, Does.Contain("public void __dlg_Calc__Derived_V__Derived()"));
        }

        // ---- A8 partial --------------------------------------------------------

        [Test]
        public void PartialClassPutsTrampolinesInFirstDeclaration()
        {
            const string types = "public delegate int Calc(int x);";
            const string p1 = Head + "public partial class Part : UdonSharpBehaviour { static int A(int x) => x; void Start() { Calc c = A; Debug.Log(c(1) + Other()); } }";
            const string p2 = Head + "public partial class Part { static int B(int x) => x * 2; int Other() { Calc c = B; Calc d = A; return c(2) + d(3); } }";
            CompileResult r = Compile(new TestSource("Types.cs", types), new TestSource("Part.cs", p1), new TestSource("PartOther.cs", p2));
            Assert.That(r.Diagnostics, Is.Empty);
            var entries = r.Programs.Single().EntryPoints.ToList();
            Assert.That(entries, Does.Contain("__dlg_Calc__Part_A__Part"));
            Assert.That(entries, Does.Contain("__dlg_Calc__Part_B__Part"));
        }

        // ---- A10 [NetworkCallable] ---------------------------------------------

        [Test]
        public void NetworkCallableWithDelegateParameterIsAnError()
        {
            const string src = Head + "using VRC.SDK3.UdonNetworkCalling;\npublic delegate int Calc(int x);\npublic class Net : UdonSharpBehaviour { [NetworkCallable] public void Recv(Calc c) { } }";
            CompileResult r = Compile(new TestSource("Net.cs", src));
            Assert.That(r.Diagnostics.Any(d => d.Contains("[NetworkCallable]") && d.Contains("デリゲート")), string.Join("\n", r.Diagnostics));
        }

        // ---- B4 オーバーロード・B5 引数の型の違い・B10 ---------------------------

        [Test]
        public void OverloadsAndConversionsCompile()
        {
            const string src = Head + @"
public delegate int Calc(int x);
public delegate string TextMap(string s);
public delegate void LogText(string s);
public class Ov : UdonSharpBehaviour
{
    static int F(int x) => x;
    static string F(string s) => s;
    void Log(object o) { Debug.Log(o); }
    Calc Pick(bool b) { if (b) return F; return null; }
    void Start()
    {
        Calc c = F; TextMap t = F; LogText l = Log;
        Calc d = c != null ? c : (Calc)F;
        l(t(""a"") + c(1) + Pick(true)(2) + d(3));
    }
}";
            CompileResult r = Compile(new TestSource("Ov.cs", src));
            Assert.That(r.Diagnostics, Is.Empty);
            var entries = r.Programs.Single().EntryPoints.ToList();
            Assert.That(entries, Does.Contain("__dlg_Calc__Ov_F__Ov"));
            Assert.That(entries, Does.Contain("__dlg_TextMap__Ov_F__Ov"), "同じ名前のメソッドでも、デリゲート型ごとに中継メソッドが分かれる");
        }

        // ---- B6 外部メソッド ---------------------------------------------------

        [Test]
        public void ExternMethodCompiles()
        {
            const string src = Head + "public delegate float FloatMap(float f);\npublic class Ext : UdonSharpBehaviour { void Start() { FloatMap a = Mathf.Abs; Debug.Log(a(-1f)); } }";
            CompileResult r = Compile(new TestSource("Ext.cs", src));
            Assert.That(r.Diagnostics, Is.Empty);
        }

        /// <summary>Udon に公開されていないメソッドを入れると、UdonSharp のエラーが、デリゲートを作った行に出る（中継メソッドの中ではなく）</summary>
        [Test]
        public void NotExposedMethodErrorPointsToCreation()
        {
            const string src = Head + "public delegate bool PathCheck(string p);\npublic class NotExposed : UdonSharpBehaviour\n{\n    void Start()\n    {\n        PathCheck p = System.IO.File.Exists;\n        Debug.Log(p(\"a\"));\n    }\n}";
            CompileResult r = Compile(new TestSource("NotExposed.cs", src));
            Assert.That(r.Diagnostics.Count, Is.EqualTo(1), string.Join("\n", r.Diagnostics));
            Assert.That(r.Diagnostics[0], Does.Contain("NotExposed.cs(8,"), "デリゲートを作った行（8 行目）");
            Assert.That(r.Diagnostics[0], Does.Contain("not exposed"));
        }

        // ---- B9 名前空間 -------------------------------------------------------

        [Test]
        public void SameNameInDifferentNamespacesAndAliases()
        {
            const string types = "namespace N1 { public delegate int Calc(int x); }\nnamespace N2 { public delegate string Calc(string s); }\npublic delegate int GlobalCalc(int x);";
            const string user = Head + "using C1 = N1.Calc;\nnamespace N3 { public class Ns : UdonSharpBehaviour { static int I(int x) => x; static string S(string s) => s;\n"
                                + "void Start() { C1 a = I; N2.Calc b = S; GlobalCalc g = I; global::N1.Calc q = a; Debug.Log(a(1) + b(\"x\") + g(2) + q(3)); } } }";
            CompileResult r = Compile(new TestSource("Types.cs", types), new TestSource("Ns.cs", user));
            Assert.That(r.Diagnostics, Is.Empty);
            var entries = r.Programs.Single().EntryPoints.ToList();
            Assert.That(entries, Does.Contain("__dlg_N1_Calc__N3_Ns_I__N3_Ns"));
            Assert.That(entries, Does.Contain("__dlg_N2_Calc__N3_Ns_S__N3_Ns"));
            Assert.That(entries, Does.Contain("__dlg_GlobalCalc__N3_Ns_I__N3_Ns"));
        }

        // ---- #127 フィールドの既定値 ---------------------------------------------

        /// <summary>
        /// object[] になるフィールド（普通のクラス型・デリゲート型と、その配列）は、public でも [OdinSerialize] があっても null で始まる。
        /// UdonSharp はシリアライズする配列のフィールドの既定値を空の配列にするため、シリアライズさせない
        /// </summary>
        [Test]
        public void LoweredFieldsStartAsNull()
        {
            const string src = Head + @"
public delegate int Calc(int x);
public interface IThing { int A(); }
public class Item { public int v; }
public class Defaults : UdonSharpBehaviour
{
    public Item item;
    public Item[] items;
    public Calc calc;
    public Calc[] calcs;
    public IThing thing;
    public int[] ints;
    [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */ public Item odinItem;
    [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] public Calc odinCalc;
    [VRC.Udon.Serialization.OdinSerializer.OdinSerialize]
    private Item odinPrivate;
    void Start() { Debug.Log(item == null); Debug.Log(items); Debug.Log(calc == null); Debug.Log(calcs); Debug.Log(thing); Debug.Log(odinItem == null); Debug.Log(odinCalc == null); Debug.Log(odinPrivate == null); }
}";
            CompileResult r = Compile(new TestSource("Defaults.cs", src));
            Assert.That(r.Diagnostics, Is.Empty);
            var defaults = r.Programs.Single().HeapDefaults;
            foreach (string name in new[] { "item", "items", "calc", "calcs", "thing", "odinItem", "odinCalc", "odinPrivate" })
                Assert.That(defaults.ContainsKey(name), Is.False, $"{name} = {(defaults.TryGetValue(name, out string v) ? v : "")}");
            Assert.That(defaults["ints"], Is.EqualTo("[]"), "書き換えない型の配列は、UdonSharp のとおり空の配列");
        }

        [Test]
        public void RemovingOdinSerializeKeepsLines()
        {
            const string src = Head + @"
public class Item { public int v; }
public class Lines : UdonSharpBehaviour
{
    [VRC.Udon.Serialization.OdinSerializer.OdinSerialize]
    public Item a;
    [VRC.Udon.Serialization.OdinSerializer.OdinSerialize, HideInInspector] public Item b;
    void Start() { Debug.Log(a == null || b == null); }
}";
            string lowered = Dump("Lines", new TestSource("Lines.cs", src));
            Assert.That(lowered.Split('\n').Length, Is.EqualTo(src.Split('\n').Length));
            Assert.That(lowered, Does.Not.Contain("OdinSerialize"));
            Assert.That(lowered, Does.Contain("[HideInInspector]"));
        }

        // ---- D1 自動アップグレード --------------------------------------------------

        /// <summary>
        /// UdonSharp の自動アップグレードの書き換え（SerializationUpdateSyntaxRewriter）は、UdonSharp Plus が書き換える型
        /// （普通のクラス・インタフェース・デリゲートと、その配列）のフィールドに [OdinSerialize] を足さない（#127）。
        /// それ以外の、Unity がシリアライズしない型（object など）には、これまでどおり足す
        /// </summary>
        [Test]
        public void UpgradeDoesNotAddOdinSerializeToLoweredFields()
        {
            const string src = @"
using UdonSharp;
public delegate int Calc(int x);
public interface IThing { int A(); }
public class Item { }
public class Box<T> { }
public class Up : UdonSharpBehaviour
{
    public Item item;
    public Item[] items;
    public Calc calc;
    public Calc[] calcs;
    public IThing thing;
    public Box<int> box;
}";
            // 外したフィールドは、UdonSharp の TryGetSystemType まで行かない（アップグレードの外で呼ぶと例外になるので、
            // 外れていなければこのテストが例外で落ちる。そのため、ほかの型のフィールドは置かない）
            SyntaxTree tree = CSharpSyntaxTree.ParseText(src);
            var compilation = CSharpCompilation.Create("UpgradeProbe", new[] { tree },
                (System.Collections.Generic.IEnumerable<MetadataReference>)UdonSharpInternals.GetMetadataReferences.Invoke(null, null));
            Type rewriterType = UdonSharpInternals.UpgradeVisitField.DeclaringType;
            var rewriter = (CSharpSyntaxRewriter)Activator.CreateInstance(rewriterType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { compilation.GetSemanticModel(tree) }, null);
            string result = rewriter.Visit(tree.GetRoot()).ToFullString();

            foreach (string field in new[] { "item", "items", "calc", "calcs", "thing", "box" })
            {
                string line = result.Split('\n').First(l => l.TrimEnd('\r').EndsWith(" " + field + ";"));
                Assert.That(line, Does.Not.Contain("OdinSerialize"), field);
            }
            Assert.That(result, Is.EqualTo(tree.GetRoot().ToFullString()));
        }
    }
}
