using System.IO;
using NUnit.Framework;

namespace UdonSharpPlus.Tests
{
    /// <summary>
    /// null の普通のクラスのメンバーを呼んだときに、呼び出し側で止める確認（Redmine #104）。
    /// 最初に必ず this のフィールドを読むメンバーには入れず、そうでないメンバーの呼び出しにだけ入れる。
    /// 書き換え後のソースで確かめる（止まる位置そのものは FeatureTests の NullProbe で確かめる）
    /// </summary>
    public class ReceiverNullCheckTests
    {
        private const string Target = @"
using System.Collections.Generic;

namespace UdonSharpPlusTests.NullCheck
{
    public class T
    {
        public int v;
        public List<int> list = new List<int> { 0 };

        // 確認が要る（this を使う前に抜ける・ほかの処理をすることがある）
        public int Pure(int x) => x * 2;
        public int Branch(int x) { if (x > 0) return v; return 0; }
        public int Work(int[] log) { log[0] = 1; return v; }
        public int PassThis() => Helper(this);
        public int Loop(int n) { int s = 0; for (int i = 0; i < n; i++) s += v; return s; }
        public int Constant => 42;
        public int Q { get => 1; set { } }
        public int Creates() { var c = new T(); return c.v + v; }

        // 確認が要らない（最初に this のフィールドを読む・書く）
        public int ReadsFirst() => v;
        public int ArgFirst(int x) => Add(v, x);
        public void Hit() { list[0] += 1; }
        public void Assign(int x) { v = x; }
        public int Declared() { int a = v; return a; }
        public int P { get => v; set => v = value; }

        public static int Helper(T t) => 1;
        public static int Add(int a, int b) => a + b;
    }

    public static class TExtensions
    {
        public static int Ext(this T t) => 1;
    }
}";

        private const string User = @"
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.NullCheck
{
    public class User : UdonSharpBehaviour
    {
        private T t = new T();
        private T[] ts = new T[1];

        private T Make() => t;

        private void Start()
        {
            var log = new int[1];
            int r = t.Pure(1) + t.Branch(0) + t.Work(log) + t.PassThis() + t.Loop(0) + t.Constant + t.Q + t.Creates();
            r += t.ReadsFirst() + t.ArgFirst(1) + t.Declared() + t.P;
            t.Hit();
            t.Assign(1);
            t.Q = 1;
            t.Q += 1;
            t.P = 1;
            t.P++;
            r += ts[0].Pure(1);
            r += Make().Pure(2);
            r += new T().Pure(3);
            r += t.Ext() + T.Helper(t);
            Debug.Log(r);
        }
    }
}";

        private const string Key = "__cls_UdonSharpPlusTests_NullCheck_T__";
        private static string Checked(string recv) => $"((({recv}).Length < 0) ? null : ({recv}))";

        private string _dir;
        private string _user;

        [OneTimeSetUp]
        public void Compile()
        {
            _dir = Path.GetFullPath(Path.Combine("Temp", "UdonSharpPlusTests", "UdonSharpPlusNullCheck"));
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);

            CompileResult result = UdonSharpTestCompiler.Compile(UdonSharpTestCompiler.EditorDefines, _dir,
                new TestSource("T.cs", Target), new TestSource("User.cs", User));
            Assert.That(result.Diagnostics, Is.Empty);
            _user = File.ReadAllText(Path.Combine(_dir, "Assets", "UdonSharpPlusTests", "User.cs"));
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_dir != null && Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        [TestCase("Pure__int", "1")]
        [TestCase("Branch__int", "0")]
        [TestCase("Work__intArray", "log")]
        [TestCase("Loop__int", "0")]
        public void ChecksMethodsThatMayNotReadThis(string method, string arg)
        {
            Assert.That(_user, Does.Contain($"{Key}{method}({Checked("t")}, {arg})"));
        }

        [Test]
        public void ChecksOtherMembersThatMayNotReadThis()
        {
            Assert.That(_user, Does.Contain($"{Key}PassThis({Checked("t")})"));
            Assert.That(_user, Does.Contain($"{Key}Creates({Checked("t")})"), "new が先");
            Assert.That(_user, Does.Contain($"{Key}get_Constant({Checked("t")})"));
            Assert.That(_user, Does.Contain($"{Key}get_Q({Checked("t")})"));
            Assert.That(_user, Does.Contain($"{Key}set_Q({Checked("t")}, (int)(1))"), "セッター");
            Assert.That(_user, Does.Contain($"{Key}set_Q({Checked("t")}, (int)({Key}get_Q(t) + (1)))"), "複合代入は先に評価される側だけ");
        }

        [TestCase("ReadsFirst(t)")]
        [TestCase("ArgFirst__int(t, 1)")]
        [TestCase("Declared(t)")]
        [TestCase("Hit(t)")]
        [TestCase("Assign__int(t, 1)")]
        [TestCase("get_P(t)")]
        [TestCase("set_P(t, (int)(1))")]
        [TestCase("set_P(t, (int)(" + Key + "get_P(t) + 1))")]
        public void DoesNotCheckMembersThatReadThisFirst(string call)
        {
            Assert.That(_user, Does.Contain(Key + call));
        }

        [Test]
        public void ReceiverForms()
        {
            Assert.That(_user, Does.Contain($"{Key}Pure__int({Checked("ts[0]")}, 1)"), "配列の要素は、その場で確かめる");
            Assert.That(_user, Does.Contain($"{Key}Pure__int(__cls__notnull(Make()), 2)"), "呼び出しの結果は 2 回評価できないので、ヘルパーを通す");
            Assert.That(_user, Does.Contain("private static object[] __cls__notnull(object[] __o)"));
            Assert.That(_user, Does.Contain($"{Key}Pure__int(" + Key + "ctor(), 3)"), "new したものは null にならない");
        }

        [Test]
        public void StaticAndExtensionMethodsAreNotChecked()
        {
            // C# でも、拡張メソッドは null に対して呼べる
            Assert.That(_user, Does.Contain("__cls_UdonSharpPlusTests_NullCheck_TExtensions__Ext__UdonSharpPlusTests_NullCheck_T(t)"));
            Assert.That(_user, Does.Contain($"{Key}Helper__UdonSharpPlusTests_NullCheck_T(t)"));
        }
    }
}
