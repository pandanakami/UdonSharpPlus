using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Delegate
{
    // #125：Action・Func・Predicate・Comparison と、LINQ・List のメソッドにデリゲートの変数を渡すこと
    public class FuncRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の C# での結果（Scenario() を普通の C# として実行して確かめたもの）
        private const string Expected = "7,16,12,True,True,1,a1;21,40,4,8,4,xXY,22,True;3,-17,True,False,5,1,11,2,110,2,110;23,7,1,3,True,False,3,97421,3,2,2;-20,1,True,6,34,12,25,437,80;202,10,15;True,False,False,True,False,False,1,-1,abcdabc;True,9,-3;3,1,True,80,4,3,7,1,p1q2";

        public FuncPeer peer;
        public Text display;

        public Func<int, int> publicFn;   // public のフィールドは null で始まる（シリアライズしない）
        private bool _publicNullAtStart;

        private Func<int, bool> _isBig;
        private string _log = "";
        private int _count;

        private static int Square(int x) => x * x;
        private int AddCount(int x) => x + _count;
        private void Log(string t) { _log += t; }

        private void Start()
        {
            _publicNullAtStart = publicFn == null;
            Run();
        }

        public override void Interact() { Run(); }

        private string Scenario()
        {
            string s = "";
            _log = "";
            _count = 0;

            // 1：型ごと（引数なし〜3 つ・戻り値あり／なし・Predicate・Comparison）、d(x) と d.Invoke(x)
            Action tick = () => _count++;
            tick();
            tick.Invoke();
            Action<int> add = x => _count += x;
            add(5);
            Action<int, string> two = (n, t) => _log += t + n;
            two(1, "a");
            Func<int> getCount = () => _count;
            Func<int, int> sq = Square;
            Func<int, int, int> mul = (a, b) => a * b;
            Func<string, int, int, bool> between = (t, lo, hi) => t.Length >= lo && t.Length <= hi;
            Predicate<int> isOdd = x => x % 2 == 1;
            Comparison<int> desc = (a, b) => b - a;
            s += getCount() + "," + sq(4) + "," + mul.Invoke(3, 4) + "," + between("abc", 2, 3) + "," + isOdd(3) + "," + desc(1, 2) + "," + _log + ";";

            // 2：外の変数を使うラムダ・ローカル関数・匿名メソッド・メソッド名・普通のクラスのメソッド・+= / -=・null
            int k = 10;
            Func<int, int> addK = x => x + k;
            k = 20;
            int Local(int x) => x * k;
            Func<int, int> loc = Local;
            Func<int, int> anon = delegate (int x) { return x - 1; };
            Func<int, int> inst = AddCount;
            var box = new FnBox(3);
            Func<int, int> boxGet = box.Get;
            _log = "";
            Action<string> chain = Log;
            chain += t => _log += t.ToUpper();
            chain("x");
            chain -= Log;
            chain("y");
            Func<int, int> both = sq;
            both += addK;   // 戻り値は最後のもの
            Action nothing = null;
            s += addK(1) + "," + loc(2) + "," + anon(5) + "," + inst(1) + "," + boxGet(1) + "," + _log + "," + both(2) + "," + (nothing == null) + ";";

            // 3：LINQ にデリゲートの変数を渡す（ローカル変数・フィールド・メソッド名から作ったもの・普通のクラスのメソッドから作ったもの・配列）
            var nums = new List<int> { 5, 2, 8, 1, 4 };
            int limit = 3;
            Func<int, bool> big = x => x > limit;
            _isBig = big;
            Func<int, int> neg = x => -x;
            Func<int, int> key = x => x % 3;
            Func<int, int> plus = box.Get;   // 普通のクラスのインスタンスのメソッド（LINQ に直接書けない形）
            int[] arr = { 3, 6, 9 };
            s += nums.Where(big).Count() + "," + nums.Where(_isBig).Select(neg).Sum() + "," + nums.Any(big) + "," + nums.All(big) + "," + nums.First(big) + ","
                 + nums.OrderBy(key).First() + "," + nums.Select(plus).Max() + "," + arr.Count(_isBig) + "," + nums.Sum(sq) + ","
                 + nums.Count(x => x > 4) + "," + nums.Select(Square).Sum() + ";";   // 同じ型の Func があっても、直接書いたラムダ・メソッド名は今までどおり

            // 4：List のメソッドにデリゲートの変数を渡す（外の変数に書くラムダ・Predicate・Comparison）。渡したあとで外の変数を変えると、次の呼び出しに見える
            var list = new List<int> { 4, 7, 1, 9, 2 };
            Predicate<int> odd = isOdd;
            Comparison<int> cmp = desc;
            int total = 0;
            Action<int> acc = x => total += x;
            list.ForEach(acc);
            int found = list.Find(odd);
            int idx = list.FindIndex(odd);
            int lastIdx = list.FindLastIndex(odd);
            bool exists = list.Exists(odd);
            bool all = list.TrueForAll(odd);
            int oddCount = list.FindAll(odd).Count;
            list.Sort(cmp);
            string sorted = "";
            foreach (int v in list)
                sorted += v;
            int removed = list.RemoveAll(odd);
            limit = 4;
            s += total + "," + found + "," + idx + "," + lastIdx + "," + exists + "," + all + "," + oddCount + "," + sorted + "," + removed + "," + list.Count + "," + nums.Count(big) + ";";

            // 5：普通のクラスの中で LINQ・List に渡す・フィールドに入れる、Func を返す Func、ほかの UdonSharpBehaviour に渡す・そこで LINQ に渡す
            box.map = neg;
            var b2 = new FnBox(5);
            Func<int, Func<int, int>> curry = FnBox.Curry();
            Func<int, int> c3 = curry(3);
            peer.hp = 100;
            Func<int, int> hit = peer.Hit;
            peer.Store(sq);
            s += box.SumMapped(nums) + "," + b2.CountOver(nums) + "," + b2.MakeOver()(6) + "," + b2.MakeAdd()(1) + "," + c3(4) + "," + curry(1)(2) + ","
                 + peer.CallStored(5) + "," + peer.ApplyAll(nums, hit) + "," + peer.hp + ";";

            // 6：配列、ソースのデリゲートと混ぜる（Func の中でソースのデリゲートを使う・ソースのデリゲートを受け取る Func）
            Func<int, int>[] fs = new Func<int, int>[] { sq, neg, null };
            fs[2] = x => x * 100;
            int fsum = 0;
            foreach (Func<int, int> f in fs)
                fsum += f(2);
            Calc fromFunc = x => sq(x) + 1;
            Func<Calc, int> applyTo5 = c => c(5);
            s += fsum + "," + fromFunc(3) + "," + applyTo5(x => x * 3) + ";";

            // 7：同じ形の別の型（Func<int, bool> と Predicate<int>）は混ざらない。Predicate・Comparison の += / -=（全部呼ばれ、戻り値は最後のもの）
            Func<int, bool> fEven = x => x % 2 == 0;
            Predicate<int> pOdd = x => x % 2 == 1;
            string order = "";
            Predicate<int> pChain = x => { order += "a"; return true; };
            pChain += x => { order += "b"; return false; };
            bool pLast = pChain(1);
            pChain -= pOdd;   // 入っていないものは外れない
            Comparison<int> cChain = (a, b) => { order += "c"; return a - b; };
            Comparison<int> cRev = (a, b) => { order += "d"; return b - a; };
            cChain += cRev;
            int cLast = cChain(1, 2);
            cChain -= cRev;
            s += fEven(2) + "," + pOdd(2) + "," + fEven.Invoke(3) + "," + pOdd.Invoke(3) + "," + pLast + "," + pChain(1) + "," + cLast + "," + cChain.Invoke(1, 2) + "," + order + ";";

            // 8：public のフィールド（null で始まる・代入・上書き・呼び出し）
            publicFn = sq;
            int pf1 = publicFn(3);
            publicFn = neg;
            s += _publicNullAtStart + "," + pf1 + "," + publicFn(3) + ";";

            // 9：型引数（配列・Vector3・GameObject・UdonSharpBehaviour・普通のクラス・インタフェース・ジェネリッククラス・List・デリゲートを返す Func）
            Func<int[], int> len = a => a.Length;
            Func<Vector3, Vector3> up = v => v + Vector3.up;
            Func<GameObject, bool> same = g => g == gameObject;
            Func<FuncPeer, int> hpOf = p => p.hp;
            Func<FnBox, FnBox> grow = b => new FnBox(b.v + 1);
            Func<ICmShape, int> area = sh => sh.Area();
            Func<MtGen<int>, int> genN = g => g.n;
            Action<List<int>> addOne = l => l.Add(1);
            string made = "";
            Func<int, Action<string>> maker = n => t => made += t + n;
            maker(1)("p");
            maker(2)("q");
            var li = new List<int>();
            addOne(li);
            s += len(new int[3]) + "," + up(Vector3.zero).y + "," + same(gameObject) + "," + hpOf(peer) + "," + grow(box).v + "," + area(new MtShapeC()) + ","
                 + genN(new MtGen<int>(0, 7)) + "," + li.Count + "," + made;
            return s;
        }

        public void Run()
        {
            string actual = Scenario();
            bool ok = actual == Expected;
            string text = "#125 .NET delegates\n" + (ok ? "RESULT: OK (same as C#)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = actual.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#125 .NET delegates"; resultOk = ok; resultText = actual; resultReady = true;
            Debug.Log("[FeatureTests] #125 func " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = text;
        }
    }
}
