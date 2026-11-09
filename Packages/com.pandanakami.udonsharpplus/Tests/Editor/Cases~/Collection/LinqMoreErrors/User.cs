using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLinqMoreErrors
{
    // #63：ドキュメントの未対応の書き方で、テストがなかったもの
    public class User : UdonSharpBehaviour
    {
        private List<string> _names = new List<string> { "a" };
        private Dictionary<string, int> _scores = new Dictionary<string, int> { { "a", 1 } };

        private void Start()
        {
            bool a = _names.Contains("A", System.StringComparer.OrdinalIgnoreCase);       // Contains の比較子
            bool b = _scores.Contains(new KeyValuePair<string, int>("a", 1));             // Dictionary の要素の Contains
            Debug.Log(a + " " + b);
        }
    }
}
