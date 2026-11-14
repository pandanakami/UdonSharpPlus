using System;
using System.Collections.Generic;
using System.Linq;

namespace FeatureTests.Delegate
{
    // #125 の追加テスト：partial の別のファイル（同じ型の Func を、別のファイル・別の UdonSharpBehaviour でも使う）
    public partial class FuncComboRunner
    {
        private int _partBase = 1000;

        private int PartAdd(int x) => x + _partBase;

        private string PartUse()
        {
            var l = new List<int> { 1, 2 };
            Func<int, int> add = PartAdd;
            Func<int, int> twice = x => x * 2;
            peer.Store(add);
            return l.Select(add).Sum() + "," + l.Select(twice).Sum() + "," + peer.CallStored(1) + "," + peer.ApplyAll(l, twice);
        }
    }
}
