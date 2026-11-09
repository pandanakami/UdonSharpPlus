using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace FeatureTests.Collection
{
    // #63: collections.md の「注意」に書いた、Udon の実行時例外になる書き方を 1 つ実行する。
    // 例外が起きるとこの UdonBehaviour は止まるので、ケースごとに 1 つずつ置き、caseId で選ぶ。
    // started は実行の前、finished は実行の後に立てる。RuntimeNoteRunner がそれを読んで判定する
    public class RuntimeNoteProbe : UdonSharpBehaviour
    {
        public const int Count = 11;

        public int caseId;
        public bool started;
        public bool finished;
        public string observed = "";

        public static string NameOf(int id)
        {
            switch (id)
            {
                case 0: return "First() on empty";
                case 1: return "Last() on empty";
                case 2: return "Min() on empty";
                case 3: return "Max() on empty";
                case 4: return "Average() on empty";
                case 5: return "ToDictionary duplicate key";
                case 6: return "dict[missing key]";
                case 7: return "FindIndex(start > Count)";
                case 8: return "foreach + Add";
                case 9: return "foreach + Remove";
                case 10: return "foreach + Clear";
            }
            return "?";
        }

        private void Start()
        {
            started = true;
            Debug.Log("[FeatureTests] #63 note " + caseId + " " + NameOf(caseId) + ": start");
            var empty = new List<int>();
            var list = new List<int> { 1, 2, 3 };
            switch (caseId)
            {
                case 0: observed = "" + empty.First(); break;
                case 1: observed = "" + empty.Last(); break;
                case 2: observed = "" + empty.Min(); break;
                case 3: observed = "" + empty.Max(); break;
                case 4: observed = "" + empty.Average(); break;
                case 5:
                    var words = new List<string> { "a", "b", "a" };
                    observed = "" + words.ToDictionary(w => w).Count;
                    break;
                case 6:
                    var dict = new Dictionary<string, int> { { "a", 1 } };
                    observed = "" + dict["zz"];
                    break;
                case 7: observed = "" + list.FindIndex(10, x => x > 0); break;
                case 8:
                    int loops = 0;
                    foreach (int x in list)
                    {
                        if (loops == 0)
                            list.Add(9);
                        loops++;
                    }
                    observed = loops + "," + list.Count;
                    break;
                case 9:
                    foreach (int x in list)
                        list.Remove(x);
                    observed = "" + list.Count;
                    break;
                case 10:
                    foreach (int x in list)
                        list.Clear();
                    observed = "" + list.Count;
                    break;
            }
            finished = true;
            Debug.Log("[FeatureTests] #63 note " + caseId + " " + NameOf(caseId) + ": finished " + observed);
        }
    }
}
