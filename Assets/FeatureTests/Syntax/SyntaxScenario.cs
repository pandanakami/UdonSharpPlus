using IntList = System.Collections.Generic.List<int>;
using StrIntMap = System.Collections.Generic.Dictionary<string, int>;
using AItem = FeatureTests.Syntax.Alpha.Item;
using IScore = FeatureTests.Syntax.Alpha.IScorer;

namespace FeatureTests.Syntax
{
    // #79: 書き方の違いのシナリオ。普通の C# でも Udon でも同じ結果になることを確かめる
    // （a〜d、n は #79 の注記の観点の記号）。このファイルには System.Collections.Generic などの using を書かない
    public static class SyntaxScenario
    {
        public static string Run(UdonSharp.UdonSharpBehaviour target)
        {
            string r = "";
            r += "a:" + FullyQualified() + ";";
            r += "b:" + Alias() + ";";
            r += "c:" + Namespaces() + ";";
            r += "d:" + Declarations() + ";";
            r += "n:" + Inheritance(target) + ";";
            return r;
        }

        // a: 完全修飾名
        private static string FullyQualified()
        {
            global::System.Collections.Generic.List<int> list = new global::System.Collections.Generic.List<int> { 3, 1, 2 };
            System.Collections.Generic.Dictionary<string, int> map = new System.Collections.Generic.Dictionary<string, int>();
            map["x"] = list[0];
            string kvs = "";
            foreach (System.Collections.Generic.KeyValuePair<string, int> kv in map)
                kvs += kv.Key + kv.Value;
            global::System.Collections.Generic.KeyValuePair<string, int> pair = new global::System.Collections.Generic.KeyValuePair<string, int>("p", 9);
            FeatureTests.Syntax.Alpha.Item item = new FeatureTests.Syntax.Alpha.Item(4);
            global::FeatureTests.Syntax.Alpha.IScorer scorer = item;
            return list.Count + "," + kvs + "," + pair.Key + pair.Value + "," + item.v + "," + scorer.Score(1) + "," + global::FeatureTests.Syntax.Alpha.Calc.Twice(5);
        }

        // b: using エイリアス
        private static string Alias()
        {
            IntList list = new IntList();
            list.Add(5);
            list.Add(6);
            StrIntMap map = new StrIntMap();
            map.Add("k", list.Count);
            AItem item = new AItem(2);
            IScore s = item;
            return list[1] + "," + map["k"] + "," + s.Score(3) + "," + item.Doubled;
        }

        // c: 名前空間（相対の名前空間、同じ名前のクラス、元のファイルの using に頼ったコピー）
        private static string Namespaces()
        {
            AItem a = new AItem(1);
            Beta.Item b = new Beta.Item("z");
            object o = b;
            Alpha.Counter c = new Alpha.Counter(10, 5);
            c.Add();
            c.Add(times: 2);
            Alpha.Counter d = new Alpha.Counter(step: 3);
            d.Add(2);
            return a.Score(2) + "," + b.Show() + "," + c.Last() + "," + c.Count + "," + d.Last() + "," + (o is Beta.Item) + "," + (o is AItem);
        }

        // d: var、new()、式形式のメンバー、名前付き引数
        private static string Declarations()
        {
            var list = new System.Collections.Generic.List<string>();
            list.Add("q");
            IntList nums = new();
            nums.Add(1);
            StrIntMap map = new();
            map["a"] = 1;
            AItem item = new(7);
            var counter = new Alpha.Counter();
            counter.Step = 4;
            counter.Add(times: 3);
            item.Bump();
            item.Bump();
            int twice = item.v;
            item.Reset();
            return list.Count + "," + nums.Count + "," + map["a"] + "," + item.v + "," + counter.Last() + "," + counter.Step + "," + twice;
        }

        // n: インタフェースの継承
        private static string Inheritance(UdonSharp.UdonSharpBehaviour target)
        {
            IDerived[] ds = { new D1(), new D2(), new D3(), new D4() };
            string s = "";
            foreach (IDerived d in ds)
            {
                IBase b = d;
                s += d.A(1) + "/" + d.B(1) + "/" + b.A(2) + " ";
            }

            IDerived t = (IDerived)target;
            IBase tb = t;
            s += t.A(1) + "/" + t.B(1) + "/" + tb.A(2);

            object ob = new OnlyBase();
            object od = ds[0];
            object ot = target;
            string checks = "" + (ob is IBase) + (ob is IDerived) + (od is IBase) + (ot is IBase) + (ot is IDerived);
            IBase asBase = ob as IBase;
            IDerived asDerived = ob as IDerived;
            checks += asBase.A(3) + "" + (asDerived == null);

            Both both = new Both();
            ILeft l = both;
            IRight rr = both;
            IBoth bo = both;
            return s + "," + checks + "," + l.Same(1) + "," + rr.Same(2) + "," + bo.Own() + "," + ((ILeft)bo).Same(3) + "," + ((IRight)bo).Same(4);
        }
    }
}
