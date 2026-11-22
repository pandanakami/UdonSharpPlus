using System;
using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace FeatureTests.Async
{
    // #207 のパネル：UniTask の async / await（UdonSharp Plus がコルーチンに書き換える）。
    // 待つ種類ごとの再開するフレーム（Yield は次のフレーム、NextFrame も、DelayFrame(n) は n フレーム、DelayFrame(0) は 1 フレーム）、
    // 秒の待ち、WaitUntil・WaitWhile、async を await する（子を待つ）、待たずに呼ぶ（並行）、async void、Start・Interact の async を、
    // 1 つの流れ（Main）で確かめる。ログは「名前 + 始めてからのフレーム数」で、コルーチンの待ち方（#107・#174）と同じ値になる
    public class AsyncRunner : UdonSharpBehaviour
    {
        // #106: 結果。FeatureTestResults（エディタ）が GetProgramVariable で読み、全パネルの結果をまとめる
        [HideInInspector] public bool resultReady;
        [HideInInspector] public bool resultOk;
        [HideInInspector] public string resultName = "";
        [HideInInspector] public string resultText = "";

        // s：始まり（0 フレーム目）。y・n：Yield・NextFrame（1 フレームずつ）。d：DelayFrame(3)（3 フレーム）。z：DelayFrame(0)（1 フレーム）
        // t・m・r：Delay(200)・Delay(TimeSpan)・Delay(ignoreTimeScale) が指定より短くない。u・w：WaitUntil・WaitWhile
        // c1〜c3：async を await（子が終わったら、同じフレームのうちに親が再開）。sum3：引数のある async。pa・pb・v：待たずに呼ぶ（並行。待った順に再開）
        // v3x!10/40：値を返す async（UniTask<T>）の値（int・string・return await・await のない async）。w5y!：WhenAll で値をタプルで受け取る。
        // a0・b107：WhenAny（先に終わったものの番号と、それぞれの値。終わったもの以外は既定の値）。h9・f8・g12：変数・フィールドに入れた UniTask を await・WhenAll に渡す
        // p11・q21：同じメソッドを並行して動かしても、戻り値は混ざらない（開始ごとの置き場）
        private const string Expected = "s0;y1;n2;d5;z6;t;m;r;u;w;c1;c2;c3;sum3;v3x!10/40;w5y!;a0;b107;h9;f8;g12;pa0;pb0;v0;p11;q21;pa1;pb1;v1;e;";

        public Text display;

        private string _log = "";
        private int _f0;
        private int _n;
        private int _sum;
        private UniTask<int> _held;

        private void L(string name) { _log += name + (Time.frameCount - _f0) + ";"; }

        private void Update() { _n++; }

        // Start・Interact は Udon がイベントの名前で呼ぶ。async のままでも、同じ名前の void のメソッドがコルーチンを始める
        private async UniTaskVoid Start()
        {
            await Main();
            Report();
        }

        public override async void Interact()
        {
            await Main();
            Report();
        }

        private async UniTask Main()
        {
            _log = "";
            _f0 = Time.frameCount;
            _sum = 0;
            L("s");
            await UniTask.Yield();
            L("y");
            await UniTask.NextFrame();
            L("n");
            await UniTask.DelayFrame(3);
            L("d");
            await UniTask.DelayFrame(0);
            L("z");

            float t0 = Time.time;
            await UniTask.Delay(200);
            _log += (Time.time - t0 >= 0.2f ? "t" : "T") + ";";
            t0 = Time.time;
            await UniTask.Delay(TimeSpan.FromMilliseconds(100));
            _log += (Time.time - t0 >= 0.1f ? "m" : "M") + ";";
            float r0 = Time.realtimeSinceStartup;
            await UniTask.Delay(100, ignoreTimeScale: true);
            _log += (Time.realtimeSinceStartup - r0 >= 0.1f ? "r" : "R") + ";";

            int target = _n + 3;
            await UniTask.WaitUntil(() => _n >= target);
            _log += (_n >= target ? "u" : "U") + ";";
            int target2 = _n + 2;
            await UniTask.WaitWhile(() => _n < target2);
            _log += (_n >= target2 ? "w" : "W") + ";";

            await Child();
            _log += "c3;";
            await AddLater(1, 2);
            _log += "sum" + _sum + ";";

            int v = await Add(1, 2);
            string nm = await Name("x");
            int t2 = await Twice(5);
            int f = await Fast(4);
            _log += "v" + v + nm + t2 + "/" + f + ";";
            var (ra, rb) = await UniTask.WhenAll(Add(2, 3), Name("y"));
            _log += "w" + ra + rb + ";";

            int first = await UniTask.WhenAny(Wait(1), Wait(3));
            _log += "a" + first + ";";
            var (wi, wv1, wv2) = await UniTask.WhenAny(Slow(5, 3), Slow(7, 1));
            _log += "b" + wi + wv1 + wv2 + ";";
            UniTask h = Wait(2);
            var hv = Add(4, 5);
            await h;
            int hx = await hv;
            _log += "h" + hx + ";";
            _held = Slow(8, 2);
            int fv = await _held;
            _log += "f" + fv + ";";
            UniTask<int> x1 = Slow(1, 2);
            UniTask<int> x2 = Slow(2, 1);
            var (g1, g2) = await UniTask.WhenAll(x1, x2);
            _log += "g" + g1 + g2 + ";";

            Conc("p", 10);
            Conc("q", 20);
            Par("pa");
            Par("pb");
            Both();
            await UniTask.DelayFrame(2);
            _log += "e;";
        }

        private async UniTask Child()
        {
            _log += "c1;";
            await UniTask.Yield();
            _log += "c2;";
        }

        private async UniTask AddLater(int a, int b)
        {
            await UniTask.Yield();
            _sum += a + b;
        }

        private async UniTask<int> Add(int a, int b)
        {
            await UniTask.Yield();
            return a + b;
        }

        private async UniTask Wait(int frames)
        {
            await UniTask.DelayFrame(frames);
        }

        private async UniTask<int> Slow(int value, int frames)
        {
            await UniTask.DelayFrame(frames);
            return value;
        }

        private async UniTask<string> Name(string s)
        {
            await UniTask.Yield();
            return s + "!";
        }

        private async UniTask<int> Twice(int a)
        {
            return await Add(a, a);
        }

        // await のない async：その場で終わる
        private async UniTask<int> Fast(int a)
        {
            return a * 10;
        }

        private async UniTaskVoid Conc(string id, int k)
        {
            int r = await Add(k, 1);
            _log += id + r + ";";
        }

        private async UniTaskVoid Par(string id)
        {
            _log += id + "0;";
            await UniTask.Yield();
            _log += id + "1;";
        }

        private async void Both()
        {
            _log += "v0;";
            await UniTask.Yield();
            _log += "v1;";
        }

        private void Report()
        {
            bool ok = _log == Expected;
            string text = "#207 UniTask\n" + (ok ? "RESULT: OK (same as the coroutine rules)" : "RESULT: NG") + "\n\n";
            string[] e = Expected.Split(';');
            string[] a = _log.Split(';');
            int n = Mathf.Max(e.Length, a.Length);
            for (int i = 0; i < n; i++)
            {
                string ei = i < e.Length ? e[i] : "<none>";
                string ai = i < a.Length ? a[i] : "<none>";
                text += ei == ai ? $"OK  {ai}\n" : $"NG  {ai}\n    expected {ei}\n";
            }
            resultName = "#207 UniTask"; resultOk = ok; resultText = _log; resultReady = true;
            Debug.Log("[FeatureTests] #207 UniTask " + (ok ? "OK" : "NG") + " | " + _log);
            if (display != null)
                display.text = text;
        }
    }
}
