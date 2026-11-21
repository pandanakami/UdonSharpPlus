using System.Collections;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Coroutines
{
    // #107：Coroutine をフィールドに持つ普通のクラス
    public class CoBox
    {
        public Coroutine handle;
    }

    // #107：コルーチン。普通の Unity と同じ順・同じフレームで動くかを確かめる
    public class CoroutineRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // 普通の Unity のコルーチンでの結果（フレームは、そのコルーチンを開始したフレームからの数）
        // A：最初の yield までは開始の中で動く・yield return null は次のフレーム・同じコルーチンを 2 つ同時に動かす・開始した順に再開する
        // B：WaitForSeconds は短いほうが先に再開し、待った時間は指定より短くない
        // A の続き：yield break・終わった状態の使い回し（z は、x・y が終わったあとに開始する）
        // C：D・F の場面（yield をまたぐ変数・if・ループ・switch の中の yield）。E：foreach の中の yield（秒を待つので、フレームは見ない）
        private const string Expected = "A=x0@0;s;y0@0;e0;x1@1;y1@1;x2@2;y2@2;z0@0;z1@1;z2@2;|B=qok;pok;"
                                        + "|C=m0=3@1;k0=3@1;m2=26@2;k2=26@2;m3=59@3;kend26@4;B=e2o1d0d1two@4;mend59@5;|E=ab34p1;"
                                        + "|H=1a@1;1end@1;3a@1;3end@1;4a@1;4end@1;5a@1;5end@1;6a@1;6end@1;7s@1;8a@1;8end@1;9a@1;9end@1;10a@1;10end@1;11a1@1;11end@1;12a@1;12end@1;2f@2;2a@2;2end@2;|W=wTrueTrue;"
                                        + "|R=r1;|O=5,7;1,8;2,9;|I=b0;d0;b1;d1;derived;own;b0;b1;off;|K=20;churnTrue;|L=True1,1,-1;stoppedTrue;"
                                        + "|G=t0;u0;nTrueTrue;t1;u1;s0;s1;u2;end;v0;v1;fin;allTrue;inactiveTrue;disabledTrue;inactiveStartTrue;"
                                        + "|X=f0@0;s0@0;s0a@0;c@1;da@1;db@1;es@1;f0a@1;f1@1;s1@1;s1a@1;f2@1;a3@2;a2@2;a1@2;a@2;f1a@2;ev3@2;g3@2;bc@3;bp1@3;ei@3;ew@3;"
                                        + "|S=w-1@1;w0@1;wTiny@1;r-1@1;rTiny@1;rNaN@1;";
        // H：WaitUntil・WaitWhile・子のコルーチンを待つ形（7 は、待っている子を止めると親は再開しない）。W：WaitForSeconds・WaitForSecondsRealtime を変数に入れて使い回す
        // I：基底と派生の両方にコルーチンがある・virtual のコルーチンを override する・派生の OnDisable（Derived で確かめる）
        // L：Coroutine を List・Dictionary・配列・普通のクラスのフィールドに入れて、Contains・IndexOf・Remove と停止に使う
        // K：初めの大きさ（8）を超える数を同時に動かす・開始と停止を何度もくり返しても、古い番号で新しいものを止めない（秒を待つので、フレームは見ない）
        // O：out var で宣言した変数を yield のあとで使う（ループの中も）
        // R：デリゲートを通る、同じコルーチンへの再入（#107 の決めたこと 10）。実行時の確認でエラーをログに出して内側を止め、外側の変数は壊れない。
        //    ここだけは、わざと普通の Unity と違う（Unity では内側も動き、r1;r9; になる）
        // G：StopCoroutine（Coroutine・IEnumerator の値）・止めた番号でほかを止めない・自分を止めると次の yield で止まる・
        //    Coroutine の null との比較、StopAllCoroutines・GameObject を非アクティブにすると止まる・ビヘイビアを無効にしても止まらない・
        //    非アクティブの GameObject では開始しない（Peer で確かめる）
        // X：子→孫→ひ孫・一つの子を二つの親が待つ（2 つ目は再開しない）・自分を待つ・互いに待つ（再開しない）・親を止めても子は動く・
        //    終わったものを待つと次のフレーム、止めたものを待つとその場で続く・WaitUntil の評価回数（#107 の追加テスト案の 4・12）
        // S：WaitForSeconds・WaitForSecondsRealtime の負・0・極小・NaN・正の無限大（無限大・NaN の WaitForSeconds と、無限大の Realtime は再開しない）

        public Text display;
        public CoroutinePeer peer;
        public CoroutineDerived derived;

        private string _log = "";
        private string _timed = "";
        private string _loops = "";
        private string _lists = "";
        private float _t0;
        private Coroutine _y;
        private Coroutine _me;
        private string _stops = "";
        private string _nest = "";
        private string _re = "";
        private string _outs = "";
        private string _inherit = "";
        private string _many = "";
        private int _manyDone;
        private int _tick;
        private string _coll = "";
        private int[] _counts = new int[4];
        private System.Action _again;
        private string _cached = "";
        private int _n0;
        private bool _flag;
        private Coroutine _done;
        private Coroutine _child;

        private int F() => Time.frameCount;

        private void Start()
        {
            _t0 = Time.time;
            StartCoroutine(Steps("x", F()));
            _log += "s;";
            _y = StartCoroutine(Steps("y", F()));
            StartCoroutine(Timed("p", 0.3f));
            StartCoroutine(Timed("q", 0.1f));
            StartCoroutine(Early());
            StartCoroutine(Count("m", 4, F()));
            StartCoroutine(Count("k", 3, F()));
            StartCoroutine(Lists(F()));
            StartCoroutine(Branches(2, F()));
            StartCoroutine(Stopper());
            StartCoroutine(PeerTest());
            StartCoroutine(Nest());
            StartCoroutine(Cached());
            _again = () => StartCoroutine(Re(9));
            StartCoroutine(Re(1));
            StartCoroutine(Outs());
            StartCoroutine(Inherit());
            StartCoroutine(ManyTest());
            StartCoroutine(Collections());
            StartCoroutine(Graph());
            StartCoroutine(Judge());
        }

        // H：WaitUntil・WaitWhile・子のコルーチンを待つ形（普通の Unity で測ったものと同じ場面。どの待ち方も最低 1 フレーム待つ）
        private string N() => "@" + (Time.frameCount - _n0);

        private IEnumerator Nest()
        {
            _n0 = Time.frameCount;
            _done = StartCoroutine(Sync());
            StartCoroutine(Case("1", WaitTrue()));
            StartCoroutine(Case("2", WaitLater()));
            StartCoroutine(Case("3", ChildSync()));
            StartCoroutine(Case("4", ChildOne()));
            StartCoroutine(Case("5", EnumOne()));
            StartCoroutine(Case("6", Finished()));
            StartCoroutine(Case("7", StoppedChild()));
            StartCoroutine(Case("8", Realtime0()));
            StartCoroutine(Case("9", Seconds0()));
            StartCoroutine(Case("10", YieldInt()));
            StartCoroutine(Case("11", WaitWhileFalse()));
            StartCoroutine(Case("12", EnumSync()));
            yield return null;
        }

        private IEnumerator Case(string name, IEnumerator body)
        {
            yield return StartCoroutine(body);
            _nest += name + "end" + N() + ";";
        }

        private IEnumerator Sync() { if (_n0 < 0) yield return null; }
        private IEnumerator One() { yield return null; }
        private bool Flag() => _flag;

        private IEnumerator WaitTrue() { yield return new WaitUntil(() => true); _nest += "1a" + N() + ";"; }
        private IEnumerator WaitLater() { StartCoroutine(SetFlag()); yield return new WaitUntil(Flag); _nest += "2a" + N() + ";"; }
        private IEnumerator SetFlag() { yield return null; yield return null; _flag = true; _nest += "2f" + N() + ";"; }
        private IEnumerator ChildSync() { yield return StartCoroutine(Sync()); _nest += "3a" + N() + ";"; }
        private IEnumerator ChildOne() { yield return StartCoroutine(One()); _nest += "4a" + N() + ";"; }
        private IEnumerator EnumOne() { yield return One(); _nest += "5a" + N() + ";"; }
        private IEnumerator Finished() { yield return _done; _nest += "6a" + N() + ";"; }
        private IEnumerator StoppedChild() { _child = StartCoroutine(Long()); StartCoroutine(StopLater()); yield return _child; _nest += "7a" + N() + ";"; }
        private IEnumerator Long() { for (int i = 0; i < 100; i++) yield return null; }
        private IEnumerator StopLater() { yield return null; StopCoroutine(_child); _nest += "7s" + N() + ";"; }
        private IEnumerator Realtime0() { yield return new WaitForSecondsRealtime(0f); _nest += "8a" + N() + ";"; }
        private IEnumerator Seconds0() { yield return new WaitForSeconds(0f); _nest += "9a" + N() + ";"; }
        private IEnumerator YieldInt() { yield return 5; _nest += "10a" + N() + ";"; }
        private IEnumerator WaitWhileFalse() { int n = 0; yield return new WaitWhile(() => n++ < 0); _nest += "11a" + n + N() + ";"; }
        private IEnumerator EnumSync() { yield return Sync(); _nest += "12a" + N() + ";"; }

        // E の続き：WaitForSeconds・WaitForSecondsRealtime を変数に入れて使い回す（秒を待つので、フレームは見ない）
        private WaitForSeconds _short = new WaitForSeconds(0.05f);

        private IEnumerator Cached()
        {
            float t = Time.time;
            yield return _short;
            yield return _short;
            var real = new WaitForSecondsRealtime(0.05f);
            float r = Time.realtimeSinceStartup;
            yield return real;
            _cached += "w" + (Time.time - t >= 0.1f) + (Time.realtimeSinceStartup - r >= 0.05f) + ";";
        }

        private IEnumerator CountUp(int slot)
        {
            while (true)
            {
                _counts[slot]++;
                yield return null;
            }
        }

        private IEnumerator Collections()
        {
            var list = new System.Collections.Generic.List<Coroutine>();
            list.Add(StartCoroutine(CountUp(0)));
            Coroutine second = StartCoroutine(CountUp(1));
            list.Add(second);
            var map = new System.Collections.Generic.Dictionary<string, Coroutine> { { "x", StartCoroutine(CountUp(2)) } };
            var box = new CoBox();
            box.handle = StartCoroutine(CountUp(3));
            Coroutine[] arr = { list[0] };
            _coll = list.Contains(second) + "" + list.IndexOf(second) + ",";
            list.Remove(second);
            _coll += list.Count + "," + list.IndexOf(second) + ";";
            yield return null;
            StopCoroutine(arr[0]);
            StopCoroutine(second);
            StopCoroutine(map["x"]);
            StopCoroutine(box.handle);
            int total = _counts[0] + _counts[1] + _counts[2] + _counts[3];
            for (int k = 0; k < 3; k++) yield return null;
            _coll += "stopped" + (total == _counts[0] + _counts[1] + _counts[2] + _counts[3]) + ";";
        }

        private IEnumerator Many()
        {
            yield return null;
            yield return null;
            yield return new WaitForSeconds(0.02f);
            _manyDone++;
        }

        private IEnumerator Tick()
        {
            while (true)
            {
                _tick++;
                yield return null;
            }
        }

        private IEnumerator ManyTest()
        {
            for (int i = 0; i < 20; i++)
                StartCoroutine(Many());
            Coroutine[] old = new Coroutine[30];
            for (int i = 0; i < 30; i++)
            {
                old[i] = StartCoroutine(Tick());
                StopCoroutine(old[i]);
            }
            Coroutine live = StartCoroutine(Tick());
            for (int i = 0; i < 30; i++)
                StopCoroutine(old[i]);
            int before = _tick;
            for (int k = 0; k < 3; k++) yield return null;
            bool running = _tick > before;
            StopCoroutine(live);
            yield return new WaitForSeconds(0.3f);
            _many = _manyDone + ";churn" + running + ";";
        }

        private IEnumerator Inherit()
        {
            if (derived == null)
            {
                _inherit = "noderived;";
                yield break;
            }
            derived.Begin();
            derived.BeginMore();
            for (int k = 0; k < 4; k++) yield return null;
            derived.Begin();
            yield return null;
            derived.gameObject.SetActive(false);
            derived.gameObject.SetActive(true);
            for (int k = 0; k < 3; k++) yield return null;
            _inherit = derived.log;
        }

        private IEnumerator Outs()
        {
            int.TryParse("5", out int p);
            yield return null;
            _outs += p + ",";
            for (int i = 0; i < 3; i++)
            {
                int.TryParse((7 + i).ToString(), out var q);
                yield return null;
                _outs += q + ";";
                if (i < 2)
                    _outs += (i + 1) + ",";
            }
        }

        private IEnumerator Re(int n)
        {
            int x = n;
            yield return null;
            _again();
            _re += "r" + x + ";";
        }

        private IEnumerator Ticker(string tag)
        {
            for (int i = 0; ; i++)
            {
                _stops += tag + i + ";";
                yield return null;
            }
        }

        private IEnumerator SelfStop()
        {
            yield return null;
            _stops += "s0;";
            StopCoroutine(_me);
            _stops += "s1;";
            yield return null;
            _stops += "s2;";
        }

        private IEnumerator Stopper()
        {
            Coroutine none = null;
            Coroutine c = StartCoroutine(Ticker("t"));
            IEnumerator e = Ticker("u");
            StartCoroutine(e);
            _me = StartCoroutine(SelfStop());
            _stops += "n" + (none == null) + (c != null) + ";";
            yield return null;
            StopCoroutine(c);
            yield return null;
            StopCoroutine(e);
            yield return null;
            yield return null;
            _stops += "end;";
            Coroutine v = StartCoroutine(Ticker("v"));
            StopCoroutine(c);
            yield return null;
            StopCoroutine(v);
            yield return null;
            _stops += "fin;";
        }

        private IEnumerator PeerTest()
        {
            if (peer == null)
            {
                _stops += "nopeer;";
                yield break;
            }
            peer.Begin("a");
            for (int k = 0; k < 3; k++) yield return null;
            peer.StopAll();
            int n1 = peer.log.Length;
            for (int k = 0; k < 3; k++) yield return null;
            _stops += "all" + (n1 >= 3 && peer.log.Length == n1) + ";";

            peer.Begin("b");
            for (int k = 0; k < 3; k++) yield return null;
            peer.gameObject.SetActive(false);
            int n2 = peer.log.Length;
            for (int k = 0; k < 3; k++) yield return null;
            peer.gameObject.SetActive(true);
            for (int k = 0; k < 3; k++) yield return null;
            _stops += "inactive" + (peer.log.Length == n2) + ";";

            peer.Begin("c");
            for (int k = 0; k < 2; k++) yield return null;
            peer.enabled = false;
            int n3 = peer.log.Length;
            for (int k = 0; k < 3; k++) yield return null;
            _stops += "disabled" + (peer.log.Length > n3) + ";";
            peer.enabled = true;
            peer.StopAll();

            peer.gameObject.SetActive(false);
            bool none = peer.BeginReturnsNull("d");
            peer.gameObject.SetActive(true);
            for (int k = 0; k < 3; k++) yield return null;
            _stops += "inactiveStart" + (none && peer.log.IndexOf('d') < 0) + ";";
        }

        // D：yield をまたぐローカル変数（同じコルーチンを 2 つ同時に動かしても、変数は別々）・for の continue・break・入れ子のループ
        private IEnumerator Count(string tag, int n, int start)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == 1)
                    continue;
                int inner = 0;
                for (int j = 0; j < 5; j++)
                {
                    if (j == 3)
                        break;
                    inner += j;
                }
                sum += i * 10 + inner;
                yield return null;
                _loops += tag + i + "=" + sum + "@" + (F() - start) + ";";
                if (sum > 30)
                    break;
            }
            for (int i = 0; i < 2; i++)
                yield return null;
            _loops += tag + "end" + sum + "@" + (F() - start) + ";";
        }

        // E：List・配列・Dictionary の foreach の中の yield
        private IEnumerator Lists(int start)
        {
            var words = new System.Collections.Generic.List<string> { "a", "b" };
            string got = "";
            foreach (string w in words)
            {
                yield return null;
                got += w;
            }
            int[] nums = { 3, 4 };
            foreach (int x in nums)
            {
                got += x;
                yield return new WaitForSeconds(0.05f);
            }
            var map = new System.Collections.Generic.Dictionary<string, int> { { "p", 1 } };
            foreach (var kv in map)
            {
                yield return null;
                got += kv.Key + kv.Value;
            }
            _lists += got + ";";
        }

        // F：if / else・while・do・switch の中の yield と、引数の書き換え
        private IEnumerator Branches(int n, int start)
        {
            string s = "";
            while (n > 0)
            {
                if (n % 2 == 0)
                {
                    s += "e" + n;
                    yield return null;
                }
                else
                    s += "o" + n;
                n--;
            }
            do
            {
                yield return null;
                s += "d" + n;
                n++;
            } while (n < 2);
            switch (n)
            {
                case 1:
                    s += "one";
                    break;
                case 2:
                    yield return null;
                    s += "two";
                    break;
                default:
                    s += "other";
                    break;
            }
            _loops += "B=" + s + "@" + (F() - start) + ";";
        }

        // X：待つ形の異常系と秒の境界（#107 の追加テスト案の 4・12。普通の Unity で測ったものと同じ場面）
        private string _graph = "";
        private int _g0;
        private Coroutine _gShared;
        private Coroutine _gSelf;
        private Coroutine _gA;
        private Coroutine _gB;
        private int _gEvals;
        private int _gFlip;
        private string[] _gSecs = new string[9];

        private void X(string s) { _graph += s + "@" + (Time.frameCount - _g0) + ";"; }

        private IEnumerator Graph()
        {
            _g0 = Time.frameCount;
            StartCoroutine(GChain());
            _gShared = StartCoroutine(GLong(3, "bc"));
            StartCoroutine(GWait("bp1"));
            StartCoroutine(GWait("bp2"));
            _gSelf = StartCoroutine(GSelf());
            _gA = StartCoroutine(GCycleA());
            _gB = StartCoroutine(GCycleB());
            StartCoroutine(GStopParent());
            StartCoroutine(GHandle("f0", false, false));
            StartCoroutine(GHandle("s0", true, false));
            StartCoroutine(GHandle("f1", false, true));
            StartCoroutine(GHandle("s1", true, true));
            StartCoroutine(GSecs(0, "w-1", -1f));
            StartCoroutine(GSecs(1, "w0", 0f));
            StartCoroutine(GSecs(2, "wTiny", 1e-6f));
            StartCoroutine(GSecs(3, "wInf", float.PositiveInfinity));
            StartCoroutine(GSecs(4, "wNaN", NaN()));
            StartCoroutine(GReal(5, "r-1", -1f));
            StartCoroutine(GReal(6, "rTiny", 1e-6f));
            StartCoroutine(GReal(7, "rNaN", float.NaN));
            StartCoroutine(GReal(8, "rInf", float.PositiveInfinity));
            StartCoroutine(GEvals());
            StartCoroutine(GFlip());
            yield return null;
        }

        // 子→孫→ひ孫が終わると、同じフレームのうちに親まで順に再開する
        private IEnumerator GChain() { yield return StartCoroutine(GC1()); X("a"); }
        private IEnumerator GC1() { yield return StartCoroutine(GC2()); X("a1"); }
        private IEnumerator GC2() { yield return StartCoroutine(GC3()); X("a2"); }
        private IEnumerator GC3() { yield return null; yield return null; X("a3"); }
        private IEnumerator GLong(int k, string n) { for (int i = 0; i < k; i++) yield return null; X(n); }
        // 一つの子を二つの親が待つ：2 つ目はエラーになり、二度と再開しない
        private IEnumerator GWait(string n) { yield return _gShared; X(n); }
        // 自分を待つ・互いに待つ：二度と再開しない
        private IEnumerator GSelf() { yield return null; X("c"); yield return _gSelf; X("cNever"); }
        private IEnumerator GCycleA() { yield return null; X("da"); yield return _gB; X("daNever"); }
        private IEnumerator GCycleB() { yield return null; X("db"); yield return _gA; X("dbNever"); }
        // 親を止めても、親が開始した子（待っていたものも）は動く
        private IEnumerator GStopParent() { Coroutine p = StartCoroutine(GParent()); yield return null; StopCoroutine(p); X("es"); }
        private IEnumerator GParent() { StartCoroutine(GLong(3, "ei")); yield return StartCoroutine(GLong(3, "ew")); X("eNever"); }
        // 終わったものを待つと次のフレーム、止めたものを待つとその場で続く
        private IEnumerator GHandle(string n, bool stop, bool late)
        {
            if (late) yield return null;
            X(n);
            Coroutine c = stop ? StartCoroutine(GLong(100, "never")) : StartCoroutine(Sync());
            if (stop) StopCoroutine(c);
            yield return c;
            X(n + "a");
        }
        // 秒の境界：0 以下・極小は次のフレーム、正の無限大・NaN の WaitForSeconds は二度と再開しない。
        // 同じフレームに再開するものの中の順番は、Unity と違うことがある（Unity は秒と yield return null を期限の順に 1 つの列に並べる）ので、場面ごとの場所に書いて、順番は比べない
        private IEnumerator GSecs(int i, string n, float t) { yield return new WaitForSeconds(t); _gSecs[i] = n + "@" + (Time.frameCount - _g0) + ";"; }
        private IEnumerator GReal(int i, string n, float t) { yield return new WaitForSecondsRealtime(t); _gSecs[i] = n + "@" + (Time.frameCount - _g0) + ";"; }
        // WaitUntil の条件は、yield のときと、そのあと毎フレーム 1 回ずつ評価する
        private bool GEval() { _gEvals++; return _gEvals >= 3; }
        private IEnumerator GEvals() { yield return new WaitUntil(GEval); X("ev" + _gEvals); }
        private IEnumerator GFlip()
        {
            StartCoroutine(GFlipper());
            yield return new WaitUntil(() => _gFlip == 1 || _gFlip == 3);
            X("f" + _gFlip);
            yield return new WaitUntil(() => _gFlip == 3);
            X("g" + _gFlip);
        }
        private IEnumerator GFlipper() { _gFlip = 1; yield return null; _gFlip = 2; yield return null; _gFlip = 3; yield return null; _gFlip = 4; }
        private float NaN() => float.NaN;

        private IEnumerator Steps(string tag, int start)
        {
            _log += tag + "0@" + (F() - start) + ";";
            yield return null;
            _log += tag + "1@" + (F() - start) + ";";
            yield return null;
            _log += tag + "2@" + (F() - start) + ";";
        }

        private IEnumerator Timed(string tag, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _timed += tag + (Time.time - _t0 >= seconds ? "ok" : "early") + ";";
        }

        private IEnumerator Early()
        {
            _log += "e0;";
            if (_t0 >= 0f)
                yield break;
            yield return null;
            _log += "e1;";
        }

        private IEnumerator Judge()
        {
            yield return new WaitForSeconds(1f);
            StartCoroutine(Steps("z", F()));
            yield return new WaitForSeconds(0.5f);
            Finish();
        }

        private void Finish()
        {
            string actual = "A=" + _log + "|B=" + _timed + "|C=" + _loops + "|E=" + _lists + "|H=" + _nest + "|W=" + _cached + "|R=" + _re + "|O=" + _outs + "|I=" + _inherit + "|K=" + _many + "|L=" + _coll + "|G=" + _stops + "|X=" + _graph + "|S=" + string.Join("", _gSecs);
            bool ok = actual == Expected;
            resultName = "#107 coroutines";
            resultOk = ok;
            resultText = actual;
            resultReady = true;
            Debug.Log("[FeatureTests] #107 coroutines " + (ok ? "OK" : "NG") + " | " + actual);
            if (display != null)
                display.text = "#107 coroutines\n" + (ok ? "RESULT: OK (same as Unity)" : "RESULT: NG") + "\n\n" + actual.Replace(";", ";\n") + (ok ? "" : "\n\nexpected\n" + Expected.Replace(";", ";\n"));
        }
    }
}
