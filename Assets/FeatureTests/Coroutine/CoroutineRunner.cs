using System.Collections;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Coroutines
{
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
                                        + "|G=t0;u0;nTrueTrue;t1;u1;s0;s1;u2;end;v0;v1;fin;allTrue;inactiveTrue;disabledTrue;inactiveStartTrue;";
        // H：WaitUntil・WaitWhile・子のコルーチンを待つ形（7 は、待っている子を止めると親は再開しない）。W：WaitForSeconds・WaitForSecondsRealtime を変数に入れて使い回す
        // G：StopCoroutine（Coroutine・IEnumerator の値）・止めた番号でほかを止めない・自分を止めると次の yield で止まる・
        //    Coroutine の null との比較、StopAllCoroutines・GameObject を非アクティブにすると止まる・ビヘイビアを無効にしても止まらない・
        //    非アクティブの GameObject では開始しない（Peer で確かめる）

        public Text display;
        public CoroutinePeer peer;

        private string _log = "";
        private string _timed = "";
        private string _loops = "";
        private string _lists = "";
        private float _t0;
        private Coroutine _y;
        private Coroutine _me;
        private string _stops = "";
        private string _nest = "";
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
            string actual = "A=" + _log + "|B=" + _timed + "|C=" + _loops + "|E=" + _lists + "|H=" + _nest + "|W=" + _cached + "|G=" + _stops;
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
