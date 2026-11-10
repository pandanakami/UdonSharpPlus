using FeatureTests.Syntax.Alpha;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Syntax
{
    // #79 i・f: null のインタフェース変数・普通のクラスの変数のメンバーを使う。C# では NullReferenceException になるので、
    // Udon でも実行時例外で止まる（黙って進まない）ことを確かめる。
    // 例外が起きるとこの UdonBehaviour は止まるので、ケースごとに 1 つずつ置き、caseId で選ぶ。
    // started は実行の前、finished は実行の後に立てる。NullProbeRunner がそれを読んで判定する
    public class NullProbe : UdonSharpBehaviour
    {
        public const int Count = 6;

        public int caseId;
        public bool started;
        public bool finished;
        public string observed = "";

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
            }
            finished = true;
            Debug.Log("[FeatureTests] #79 null " + caseId + " " + NameOf(caseId) + ": finished " + observed);
        }
    }
}
