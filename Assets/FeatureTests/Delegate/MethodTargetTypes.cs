using UnityEngine;

namespace FeatureTests.Delegate
{
    // #124 の追加テスト：クラスのメソッドをデリゲートに入れる

    public delegate void MtVoid(int x);
    public delegate int MtMany(int a, int b, int c);
    public delegate MtBox MtMakeBox(MtBox b, ICmShape s, int[] arr);
    public delegate Vector3 MtVec(Vector3 v, GameObject go);
    public delegate Calc MtCalcOut(Calc c);
    public delegate string MtText(string s);

    public class MtBox
    {
        public int v;
        public string log = "";

        public MtBox(int v) { this.v = v; }

        public int Add(int x) { v += x; return v; }
        public int Get(int x) => v + x;
        public void Note(int x) { log += x; }
        public int Sum(int a, int b, int c) => v + a + b + c;
        public MtBox Merge(MtBox b, ICmShape s, int[] arr) => new MtBox(v + b.v + s.Area() + arr.Length);
        public Calc Wrap(Calc c) => x => c(x) + v;
        public string Over(string s) => "s" + s + v;
        public int Over(int x) => x + 1000;

        // this のフィールド・引数・ローカル変数を同時に使うラムダ
        public Calc MakeMix(int p)
        {
            int local = p * 10;
            return x => { local++; return x + v + p + local; };
        }

        // ほかの普通のクラス・UdonSharpBehaviour も使うラムダ
        public Calc MakeCapture(int p, MtBox other, MtTarget t)
        {
            int local = 1;
            return x => { local++; return x + v + p + local + other.v + t.hp; };
        }

        // ローカル関数・匿名メソッドから、インスタンスのメソッドを呼ぶ
        public Calc MakeViaLocal()
        {
            int Twice(int x) => Get(x) * 2;
            return Twice;
        }

        public Calc MakeViaAnon() => delegate (int x) { return Add(x); };

        // NullProbe のケース 22：普通のクラスの中のラムダの中の実行時例外
        public Calc MakeDiv() => x =>
        {
            int zero = x - x;
            return v / zero;   // ここ（#124：普通のクラスの中のラムダの中の例外も元の行）
        };

        // NullProbe のケース 23：普通のクラスの中のローカル関数の中の実行時例外（#133）
        public int LocalDiv(int x)
        {
            int Inner(int y)
            {
                int zero = y - y;
                return v / zero;   // ここ（#133：普通のクラスの中のローカル関数の中の例外も元の行）
            }
            return Inner(x);
        }
    }

    // 呼び返し：UdonSharpBehaviour → 普通のクラス → UdonSharpBehaviour → 最初の UdonSharpBehaviour
    public class MtBridge
    {
        public int Through(Calc c, MtTarget t, int x) => t.CallBack(c, x) + 1;
    }

    // ジェネリッククラス（具体型 2 つ）
    public class MtGen<T>
    {
        public T value;
        public int n;

        public MtGen(T value, int n) { this.value = value; this.n = n; }

        public int Count(int x) => n + x;
        public Calc Make() => x => x + n;
    }

    // インタフェースの普通のクラスの実装を 4 つ（振り分けが switch になる）
    public class MtShapeA : ICmShape { public int Area() => 1; }
    public class MtShapeB : ICmShape { public int Area() => 2; }
    public class MtShapeC : ICmShape { public int Area() => 3; }
    public class MtShapeD : ICmShape { public int Area() => 4; }
}
