using System.Collections.Generic;
using static UdonSharpPlusTests.Syntax.Alpha.Calc;

namespace UdonSharpPlusTests.Syntax.Alpha
{
    // #79: 元のファイルの using（using static を含む）に頼った書き方。使う側（SyntaxScenario.cs）にはこの using がない
    public static class Calc
    {
        public static int Twice(int x) => x * 2;
    }

    public interface IScorer
    {
        int Score(int x);
    }

    // c: 同じ名前のクラスが Beta にもある
    public class Item : IScorer
    {
        public int v;

        public Item(int v) => this.v = v;               // d: 式形式のコンストラクタ

        public int Score(int x) => v + Twice(x);        // c: using static に頼る

        public int Doubled => Twice(v);                 // d: 式形式のプロパティ

        public void Reset() => v = 0;                   // d: 戻り値なしの式形式のメソッドでの代入（#86）

        public void Bump() => v++;                      // d: 戻り値なしの式形式のメソッドでのインクリメント（#86）
    }

    // c: 元のファイルの using に頼った型名（List）。d: 名前付き引数・省略できる引数、this.、式形式の get/set
    public class Counter
    {
        private List<int> _values = new List<int>();
        private int _step;

        public int Step { get => _step; set => _step = value; }

        public Counter(int start = 0, int step = 1)
        {
            _values.Add(start);
            _step = step;
        }

        public void Add(int times = 1)
        {
            for (int i = 0; i < times; i++)
                this._values.Add(this._values[this._values.Count - 1] + this.Step);
        }

        public int Last() => _values[_values.Count - 1];

        public int Count => _values.Count;
    }
}
