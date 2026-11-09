using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;

namespace UdonSharpPlusTests.CollectionLambdaExpressionAssignmentErrors
{
    public class Unit
    {
        public int hp;
    }

    // #73: 戻り値のあるラムダの本体での代入は、代入の値を式として使うことになるので、今までどおりエラー
    public class User : UdonSharpBehaviour
    {
        private List<Unit> _units = new List<Unit>();
        private List<int> _ints = new List<int> { 1, 2 };
        private Dictionary<int, int> _dict = new Dictionary<int, int>();

        private void Start()
        {
            List<int> set = _units.Select(u => u.hp = 1).ToList();
            List<int> set2 = _ints.Select(x => _dict[x] = 1).ToList();
            List<int> set3 = _ints.Select(x => _dict[x]++).ToList();
            Debug.Log(set.Count + set2.Count + set3.Count);
        }
    }
}
