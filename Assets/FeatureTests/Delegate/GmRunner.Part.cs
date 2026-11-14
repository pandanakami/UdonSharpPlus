using System;

namespace FeatureTests.Delegate
{
    // #134：partial の別のファイルで作る（クロージャ・匿名メソッド・ローカル関数）
    public partial class GmRunner
    {
        private Func<int, int> A6()
        {
            int local = 3;
            Func<int, int> f = x => x * local;
            local = 5;
            return f;
        }

        private Func<int, int> A7()
        {
            _f = 5;
            Func<int, int> f = x => x + _f * 2;
            _f = 6;
            return f;
        }

        private Func<int, int> A8(int p) => delegate (int x) { return x - p; };

        private Func<int, int> A9()
        {
            int Loc(int x) => x * 3 + 1;
            return Loc;
        }

        private Func<int, int> A10()
        {
            int acc = 2;
            int Loc(int x) { acc++; return x + acc; }
            acc = 10;
            return Loc;
        }

        private string PartA() =>
            "A6=" + Use(A6()) + ";A7=" + Use(A7()) + ";A8=" + Use(A8(4)) + ";A9=" + Use(A9()) + ";A10=" + Use(A10()) + ";";
    }
}
