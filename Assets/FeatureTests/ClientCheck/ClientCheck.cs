using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace FeatureTests.ClientCheck
{
    // #227：VRChat のクライアント（実機）で、手で確かめる項目のパネル。
    // ClientCheckButton（Interact）が Run(番号) を呼ぶ。結果は、パネルと、ログ（[FeatureTests] #227 ...）に、行で出る。
    // 各行の「expect」と見比べて、OK・NG を、Redmine #227 の注記に書く。ClientSim でも動く（ネットワークは 1 クライアントぶんだけ）
    // 同期は手動：連続だと OnDeserialization が数百 ms おきに届き続けて、パネルが埋まる（RequestSerialization を呼んだときだけ届く）
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ClientCheck : UdonSharpBehaviour
    {
        public Text display;
        public ClientCheckItem item;      // 拾う・Interact・SetActive(false) の対象
        public ClientCheckItem victim;    // Destroy の対象

        [UdonSynced] private int _sv;
        private string _lines = "";
        private int _lineCount;
        private int _nb0;
        private int _nc0;
        private int _nb1;
        private int _nc1;
        private int _nb2;
        private int _nc2;
        private int _nb3;
        private int _nc3;
        private int _nb4;
        private int _nc4;
        private int _nb5;
        private int _nc5;
        private CancellationTokenSource _cts;
        private bool _mOn;
        private float _mAcc;
        private int _mN;

        private const string Header = "#227 client check (see Redmine #227)\nA: auto (Start / Joined / Left)  B: net events  C: pickup, interact\nD: time, cancel, SetActive, Destroy  E: sync, owner  F: load\n\n";

        public void Add(string s)
        {
            _lines += Time.realtimeSinceStartup.ToString("F2") + " " + s + "\n";
            _lineCount++;
            if (_lineCount > 20)
            {
                // パネルからはみ出さないよう、新しい 20 行だけを残す（全部は、ログの [FeatureTests] #227 の行にある）
                _lines = _lines.Substring(_lines.IndexOf('\n') + 1);
                _lineCount--;
            }
            Debug.Log("[FeatureTests] #227 " + s);
            Show();
        }

        // このクライアントの番号（playerId。先に入った側が 1・あとが 2）。同じアカウントで 2 つ動かすと名前では区別できない
        private string Me()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me))
                return "?";
            return me.playerId + (me.isMaster ? " (master)" : "");
        }

        // 先頭に、ネットワークで呼ぶ async の begin・continued の数を出す（合わないときは、再開が取りこぼされている。#231）
        private void Show()
        {
            if (display != null)
                display.text = Header + "[this client: player " + Me() + "]  NetNoArg begin/continued " + _nb0 + "/" + _nc0 + "   NetArg " + _nb1 + "/" + _nc1 + "\n[17: delayed frame " + _nb2 + "/" + _nc2 + "  callable delayed frame " + _nb4 + "/" + _nc4 + "]  [18: coroutine " + _nb3 + "/" + _nc3 + "  callable delayed seconds " + _nb5 + "/" + _nc5 + "]\n" + _lines;
        }

        private void Update()
        {
            if (_mOn)
            {
                _mAcc += Time.unscaledDeltaTime;
                _mN++;
            }
        }

        // ---- A：ロード直後・プレイヤーの出入り（自動）----
        private async void Start()
        {
            int f = Time.frameCount;
            Add("A Start async: begin");
            await UniTask.Yield();
            Add("A Start async: resumed at +" + (Time.frameCount - f) + " frame  expect: +1");
        }

        public override async void OnPlayerJoined(VRCPlayerApi player)
        {
            string who = Utilities.IsValid(player) ? player.displayName + " local=" + player.isLocal : "invalid";
            Add("A Joined " + who + "  expect: the other client has local=False");
            float t = Time.realtimeSinceStartup;
            await UniTask.Delay(500);
            Add("A Joined +0.5s " + who + " (" + (Time.realtimeSinceStartup - t).ToString("F2") + "s)  expect: about 0.50");
        }

        public override async void OnPlayerLeft(VRCPlayerApi player)
        {
            string who = Utilities.IsValid(player) ? player.displayName + " local=" + player.isLocal : "invalid";
            Add("A Left " + who);
            await UniTask.Delay(300);
            Add("A Left +0.3s continued");
        }

        public override async void OnPlayerRespawn(VRCPlayerApi player)
        {
            string who = Utilities.IsValid(player) ? player.displayName + " local=" + player.isLocal : "invalid";
            Add("C Respawn " + who + "  expect: only on the respawning client (VRChat docs: local player only)");
            await UniTask.Yield();
            Add("C Respawn continued");
        }

        public override async void OnOwnershipTransferred(VRCPlayerApi player)
        {
            string who = Utilities.IsValid(player) ? player.displayName : "invalid";
            Add("E panel OwnershipTransferred -> " + who + " sv=" + _sv);
            await UniTask.Yield();
            Add("E panel OwnershipTransferred continued");
        }

        public override async void OnDeserialization()
        {
            int v = _sv;
            Add("E OnDeserialization sv=" + v);
            await UniTask.Yield();
            Add("E OnDeserialization continued (sv now " + _sv + ")  expect: same");
        }

        // ---- B：名前で呼ばれる async ----
        public async void NamedA()
        {
            float t = Time.realtimeSinceStartup;
            Add("B NamedA: begin");
            await UniTask.Delay(500);
            Add("B NamedA: continued (" + (Time.realtimeSinceStartup - t).ToString("F2") + "s)  expect: about 0.50");
        }

        public async void NetNoArg()
        {
            _nb0++;
            Add("B NetNoArg: begin");
            await UniTask.Yield();
            _nc0++;
            Add("B NetNoArg: continued");
        }

        [NetworkCallable]
        public async void NetArg(int n)
        {
            _nb1++;
            Add("B NetArg(" + n + "): begin");
            await UniTask.Yield();
            _nc1++;
            Add("B NetArg(" + n + "): continued");
        }

        // ---- 対照（#231）：UniTask・async の書き換えを使わない。取りこぼしが、VRChat の遅れのイベントそのものか、書き換えかを分ける ----
        public void NetCtl()
        {
            _nb2++;
            _cf2 = Time.frameCount;
            Add("B ctl delayedFrame: begin");
            SendCustomEventDelayedFrames("CtlDone", 1);
        }

        private int _cf2;

        public void CtlDone()
        {
            _nc2++;
            Add("B ctl delayedFrame: continued (+" + (Time.frameCount - _cf2) + " frame)");
        }

        // [NetworkCallable] の中で SendCustomEventDelayedFrames・DelayedSeconds を呼ぶと、最初の 1 回が動かないか（UdonVM の不具合の疑い。#231）。
        // 上の NetCtl（[NetworkCallable] でない）と、begin・continued の数を比べる。ボタン 17 が NetCtl と NetCtlCall、ボタン 18 が NetCo と NetCtlCallSec を呼ぶ
        [NetworkCallable]
        public void NetCtlCall(int n)
        {
            _nb4++;
            _cf = Time.frameCount;
            Add("B callable delayedFrame(" + n + "): begin");
            SendCustomEventDelayedFrames("CtlCallDone", 1);
        }

        private int _cf;

        public void CtlCallDone()
        {
            _nc4++;
            // 届いたフレームが、呼んだフレームの何フレーム後か（+1 が正常。+0 は、同じフレームで届いた）
            Add("B callable delayedFrame: continued (+" + (Time.frameCount - _cf) + " frame)");
        }

        [NetworkCallable]
        public void NetCtlCallSec(int n)
        {
            _nb5++;
            Add("B callable delayedSeconds(" + n + "): begin");
            SendCustomEventDelayedSeconds("CtlCallSecDone", 0.1f);
        }

        public void CtlCallSecDone()
        {
            _nc5++;
            Add("B callable delayedSeconds: continued");
        }

        public void NetCo()
        {
            StartCoroutine(CoNet());
        }

        private IEnumerator CoNet()
        {
            _nb3++;
            Add("B ctl coroutine: begin");
            yield return null;
            _nc3++;
            Add("B ctl coroutine: continued");
        }

        // ---- 押された ----
        public void Run(int id)
        {
            switch (id)
            {
                case 0: _lines = ""; _lineCount = 0; Add("log cleared"); break;
                case 1: SendCustomNetworkEvent(NetworkEventTarget.All, "NetNoArg"); break;
                case 2: SendCustomNetworkEvent(NetworkEventTarget.Owner, "NetNoArg"); break;
                case 3: SendCustomNetworkEvent(NetworkEventTarget.Self, "NetNoArg"); break;
                case 4: SendCustomNetworkEvent(NetworkEventTarget.All, "NetArg", 7); break;
                case 5: SendCustomEventDelayedSeconds("NamedA", 1f); Add("B delayed 1s: NamedA  expect: NamedA begin after 1s"); break;
                case 6: TimeCheck(); break;
                case 7: CancelCheck(); break;
                case 8: SetActiveCheck(); break;
                case 9: DestroyCheck(); break;
                case 10: DestroyTokenCheck(); break;
                case 11: SyncCheck(); break;
                case 12: item.TakeOwnerCheck(); break;
                case 13: LoadCheck(100); break;
                case 14: LoadCheck(1000); break;
                case 15: StartCoroutine(CoWait()); break;
                case 16: RawStart(); break;
                case 17:
                    SendCustomNetworkEvent(NetworkEventTarget.All, "NetCtl");
                    SendCustomNetworkEvent(NetworkEventTarget.All, "NetCtlCall", 1);
                    break;
                case 18:
                    SendCustomNetworkEvent(NetworkEventTarget.All, "NetCo");
                    SendCustomNetworkEvent(NetworkEventTarget.All, "NetCtlCallSec", 1);
                    break;
            }
        }

        // ---- 16：SendCustomEventDelayedSeconds(0.1) だけを 20 回続けて、届いたときの Time.time・実時間・フレームの差を測る（#228。コルーチンの修正に関係しない）----
        private int _rawN;
        private float _rawT;
        private float _rawR;
        private int _rawF;
        private float _rawMin;
        private float _rawMax;
        private float _rawSum;
        private float _rawRMin;
        private float _rawRMax;
        private int _rawFSum;

        private void RawStart()
        {
            _rawN = 0;
            _rawMin = 999f;
            _rawMax = -999f;
            _rawSum = 0f;
            _rawRMin = 999f;
            _rawRMax = -999f;
            _rawFSum = 0;
            RawNext();
        }

        private void RawNext()
        {
            _rawT = Time.time;
            _rawR = Time.realtimeSinceStartup;
            _rawF = Time.frameCount;
            SendCustomEventDelayedSeconds("RawArrive", 0.1f);
        }

        public void RawArrive()
        {
            float dt = (Time.time - _rawT - 0.1f) * 1000f;
            float dr = (Time.realtimeSinceStartup - _rawR - 0.1f) * 1000f;
            if (dt < _rawMin) _rawMin = dt;
            if (dt > _rawMax) _rawMax = dt;
            if (dr < _rawRMin) _rawRMin = dr;
            if (dr > _rawRMax) _rawRMax = dr;
            _rawSum += dt;
            _rawFSum += Time.frameCount - _rawF;
            _rawN++;
            if (_rawN < 20)
            {
                RawNext();
                return;
            }
            Add("raw Delayed(0.1) x20: Time.time - 0.1 = min " + _rawMin.ToString("F1") + " / avg " + (_rawSum / 20f).ToString("F1") + " / max " + _rawMax.ToString("F1") + " ms; realtime min " + _rawRMin.ToString("F1") + " / max " + _rawRMax.ToString("F1") + " ms; avg frames " + (_rawFSum / 20f).ToString("F1") + "  expect: min >= 0 (negative = early)");
        }

        // ---- D ----
        private async void TimeCheck()
        {
            float t = Time.realtimeSinceStartup;
            await UniTask.Delay(1000);
            Add("D Delay(1000): " + (Time.realtimeSinceStartup - t).ToString("F2") + "s  expect: about 1.00 (not shorter)");
        }

        private IEnumerator CoWait()
        {
            float t = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(1f);
            Add("D coroutine WaitForSeconds(1): " + (Time.realtimeSinceStartup - t).ToString("F2") + "s  expect: about 1.00");
        }

        private async void CancelCheck()
        {
            _cts = new CancellationTokenSource();
            CancelWait(_cts.Token);
            Add("D Cancel: waiting Delay(5000) with a token; Cancel() after 0.5s");
            await UniTask.Delay(500);
            _cts.Cancel();
            Add("D Cancel() called (the 5 s wait would end in 4.5 s)");
            await UniTask.Delay(5000);
            Add("D Cancel check done (5 s after Cancel, past the original wait)  expect: no line starting with D Cancel: resumed");
        }

        private async UniTask CancelWait(CancellationToken token)
        {
            await UniTask.Delay(5000, cancellationToken: token);
            Add("D Cancel: resumed  <-- NG: the wait should have been canceled");
        }

        private async void SetActiveCheck()
        {
            item.ticks = 0;
            item.StartTicks();
            await UniTask.Delay(1000);
            int at = item.ticks;
            Add("D SetActive(false) now: ticks=" + at);
            item.gameObject.SetActive(false);
            await UniTask.Delay(500);
            item.gameObject.SetActive(true);
            await UniTask.Delay(1000);
            Add("D SetActive(true) +1s: ticks=" + item.ticks + "  expect: same as " + at + " (not resumed)");
        }

        private async void DestroyCheck()
        {
            if (victim == null)
            {
                Add("D Destroy: victim is already destroyed (reload the world to retry)");
                return;
            }
            victim.StartTicks();
            await UniTask.Delay(500);
            Add("D Destroy now: ticks=" + victim.ticks);
            Destroy(victim.gameObject);
            await UniTask.Delay(1000);
            Add("D Destroy check done  expect: no more 'tick' lines from victim");
        }

        private async void DestroyTokenCheck()
        {
            float t = Time.realtimeSinceStartup;
            await UniTask.Delay(500, cancellationToken: destroyCancellationToken);
            Add("D destroyCancellationToken: continued (" + (Time.realtimeSinceStartup - t).ToString("F2") + "s)  expect: about 0.50");
        }

        // ---- E ----
        private async void SyncCheck()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            _sv = 10;
            Add("E Sync: sv=10, await 0.3s, then sv=20 + RequestSerialization");
            await UniTask.Delay(300);
            _sv = 20;
            RequestSerialization();
            Add("E Sync: sent sv=20  expect: the other client logs OnDeserialization sv=20");
        }

        // ---- F ----
        private async void LoadCheck(int n)
        {
            float before = await Measure(60);
            for (int i = 0; i < n; i++)
                Wait(4000);
            float with = await Measure(120);
            Add("F " + n + " Delay(4000): frame " + (before * 1000f).ToString("F1") + "ms -> " + (with * 1000f).ToString("F1") + "ms  (editor: 1000 -> about 37ms)");
        }

        private async UniTask Wait(int ms) { await UniTask.Delay(ms); }

        private async UniTask<float> Measure(int frames)
        {
            _mAcc = 0f;
            _mN = 0;
            _mOn = true;
            await UniTask.DelayFrame(frames);
            _mOn = false;
            return _mN > 0 ? _mAcc / _mN : 0f;
        }
    }
}
