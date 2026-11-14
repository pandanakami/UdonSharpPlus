using FeatureTests.Syntax.Alpha;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Syntax
{
    // #79 i・f: null のインタフェース変数・普通のクラスの変数のメンバーを使う。C# では NullReferenceException になるので、
    // Udon でも実行時例外で止まる（黙って進まない）ことを確かめる。
    // 例外が起きるとこの UdonBehaviour は止まるので、ケースごとに 1 つずつ置き、caseId で選ぶ。
    // started は実行の前、finished は実行の後に立てる。NullProbeRunner がそれを読んで判定する
    // #104: this を最初に使わないメンバー。null に対して呼ぶと、C# では呼び出しの時点で NullReferenceException になる
    public class NullTarget
    {
        public int v;

        public int Pure(int x) => x * 2;                       // this を使わない

        public int Branch(int x)                               // 一部の分岐でだけ this を使う
        {
            if (x > 0)
                return v;
            return 0;
        }

        public int MarkThen(NullProbe p)                        // this を使う前に、ほかの処理がある
        {
            p.observed = "side effect";
            return v;
        }

        public int PassThis() => Helper(this);                  // this を渡すだけ

        private static int Helper(NullTarget t) => 1;

        public int Constant => 42;                              // this を使わない本体付きプロパティ
    }

    public class NullProbe : UdonSharpBehaviour
    {
        public const int Count = 27;

        public int caseId;
        public bool started;
        public bool finished;
        public string observed = "";

        // #126: デリゲートのケース
        public FeatureTests.Delegate.DelegateHolder victim;   // 12：Destroy してから、そのメソッドのデリゲートを呼ぶ
        private FeatureTests.Delegate.Calc _pending;
        private FeatureTests.Delegate.Calc[] _calcs = new FeatureTests.Delegate.Calc[1];
        private FeatureTests.Delegate.Calc NullCalc => null;
        private FeatureTests.Delegate.Calc GetNullCalc() => null;
        private int Divide(int x) => 10 / x;

        public static string NameOf(int id)
        {
            switch (id)
            {
                case 0: return "null interface call (switch, 4+ classes)";
                case 1: return "null interface call (tag compare, 1 class)";
                case 2: return "null base interface call";
                case 3: return "null plain class field read";
                case 4: return "null plain class field write";
                case 5: return "null plain class method call";
                case 6: return "null plain class method not using this";
                case 7: return "null plain class method using this in a branch";
                case 8: return "null plain class method with work before this";
                case 9: return "null plain class method passing this";
                case 10: return "null plain class property not using this";
                case 11: return "null delegate call";
                case 12: return "delegate call after target destroyed";
                case 13: return "exception inside delegate target";
                case 14: return "null delegate array element call";
                case 15: return "null delegate returned by method call";
                case 16: return "null delegate property call";
                case 17: return "exception inside lambda";
                case 18: return "exception inside closure";
                case 19: return "exception inside local function";
                case 20: return "exception inside local function in anonymous method";
                case 21: return "delegate from method of null instance";
                case 22: return "exception inside lambda made in plain class";
                case 23: return "exception inside local function in plain class";
                case 24: return "null Predicate variable passed to List method";
                case 25: return "exception inside lambda passed to List method via variable";
                case 26: return "null Func variable passed to LINQ on empty list";
            }
            return "?";
        }

        private void Start()
        {
            started = true;
            Debug.Log("[FeatureTests] #79 null " + caseId + " " + NameOf(caseId) + ": start");
            IDerived derived = null;
            IScorer scorer = null;
            IBase baseRef = derived;
            Link link = null;
            Item item = null;
            NullTarget target = null;
            FeatureTests.Delegate.Calc calc = null;   // #119
            switch (caseId)
            {
                case 0: observed = "" + derived.A(1); break;
                case 1: observed = "" + scorer.Score(1); break;
                case 2: observed = "" + baseRef.A(1); break;
                case 3: observed = "" + link.v; break;
                case 4:
                    link.v = 1;
                    observed = "written";
                    break;
                case 5: observed = "" + item.Score(1); break;
                case 6: observed = "" + target.Pure(1); break;
                case 7: observed = "" + target.Branch(0); break;
                case 8: observed = "" + target.MarkThen(this); break;
                case 9: observed = "" + target.PassThis(); break;
                case 10: observed = "" + target.Constant; break;
                case 11: observed = "" + calc(1); break;
                case 12:
                    _pending = victim.MakeAdder();
                    Destroy(victim.gameObject);
                    SendCustomEventDelayedFrames(nameof(CallDestroyed), 2);
                    return;   // 結果はあとで
                case 13:
                    FeatureTests.Delegate.Calc divide = Divide;
                    observed = "" + divide(0);
                    break;
                case 14: observed = "" + _calcs[0](1); break;
                case 15: observed = "" + GetNullCalc()(1); break;
                case 16: observed = "" + NullCalc(1); break;
                case 17:
                    FeatureTests.Delegate.Calc lambda = x =>
                    {
                        int zero = x - x;
                        return 10 / zero;   // ここ（#120：ラムダの中の例外は元の行）
                    };
                    observed = "" + lambda(3);
                    break;
                case 18:
                {
                    int zero18 = 0;
                    FeatureTests.Delegate.Calc closure = x =>
                    {
                        x += zero18;
                        return 10 / zero18;   // ここ（#129：外の変数を使うラムダの中の例外も元の行）
                    };
                    observed = "" + closure(3);
                    break;
                }
                case 19:
                {
                    int zero19 = 0;
                    int Divide(int x)
                    {
                        x += zero19;
                        return 10 / zero19;   // ここ（#123：ローカル関数の中の例外も元の行）
                    }
                    observed = "" + Divide(3);
                    break;
                }
                case 20:
                {
                    int zero20 = 0;
                    FeatureTests.Delegate.Calc anon = delegate (int x)
                    {
                        int Inner(int y)
                        {
                            y += zero20;
                            return 10 / zero20;   // ここ（#123：匿名メソッドの中のローカル関数の中の例外も元の行）
                        }
                        return Inner(x);
                    };
                    observed = "" + anon(3);
                    break;
                }
                case 21:
                {
                    // #124：null のインスタンスのメソッドからデリゲートを作ると、C# と同じく作るときに止まる
                    FeatureTests.Delegate.CmCounter none = null;
                    FeatureTests.Delegate.Calc fromNull = none.Peek;
                    observed = "created";
                    break;
                }
                case 22:
                {
                    FeatureTests.Delegate.Calc div = new FeatureTests.Delegate.MtBox(1).MakeDiv();
                    observed = "" + div(3);
                    break;
                }
                case 23: observed = "" + new FeatureTests.Delegate.MtBox(1).LocalDiv(3); break;
                case 24:
                {
                    // #125：null のデリゲートの変数を List のメソッドに渡すと、要素があれば止まる。止まる位置は渡した行
                    System.Predicate<int> nullPred = null;
                    observed = "" + new System.Collections.Generic.List<int> { 1 }.Exists(nullPred);   // ここ
                    break;
                }
                case 25:
                {
                    System.Predicate<int> divPred = x =>
                    {
                        int zero = x - x;
                        return 10 / zero > 0;   // ここ（#125：変数に入れて List のメソッドに渡したラムダの中の例外も元の行）
                    };
                    observed = "" + new System.Collections.Generic.List<int> { 1 }.Exists(divPred);
                    break;
                }
                case 26:
                {
                    // #125：要素がなくても、null のデリゲートを渡すと C# と同じく止まる（C# では ArgumentNullException）
                    System.Func<int, bool> nullFn = null;
                    observed = "" + System.Linq.Enumerable.Count(new System.Collections.Generic.List<int>(), nullFn);   // ここ
                    break;
                }
            }
            finished = true;
            Debug.Log("[FeatureTests] #79 null " + caseId + " " + NameOf(caseId) + ": finished " + observed);
        }

        public void CallDestroyed()
        {
            observed = "" + _pending(1);
            finished = true;
            Debug.Log("[FeatureTests] #79 null " + caseId + " " + NameOf(caseId) + ": finished " + observed);
        }
    }
}
