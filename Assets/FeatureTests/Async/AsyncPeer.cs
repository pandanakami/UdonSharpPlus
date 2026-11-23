using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace FeatureTests.Async
{
    // #207 の AsyncRunner から操作される相手（別の GameObject）。GameObject を非アクティブにしたときの async の停止、
    // イベント（Interact）・引数のある async の多重呼び出しを確かめる（追加テスト案の 1・5）
    public class AsyncPeer : UdonSharpBehaviour
    {
        // tick：1 フレームごとに tag を足す（動き続ける async）。ilog：Interact の前半・後半。alog：引数のある async。wlog：WhenAll の終わり
        [HideInInspector] public string tick = "";
        [HideInInspector] public string ilog = "";
        [HideInInspector] public string alog = "";
        [HideInInspector] public string wlog = "";

        // 継承・イベントの引数：派生（AsyncPeerDerived）が Calc・Interact を override する。joined：OnPlayerJoined の async（引数のあるイベント）
        [HideInInspector] public int calc;
        [HideInInspector] public string joined = "";

        public virtual async UniTask<int> Calc(int k)
        {
            await UniTask.Yield();
            return k;
        }

        public void BeginCalc(int k) { StoreCalc(k); }

        private async UniTask StoreCalc(int k) { calc = await Calc(k); }

        public override async void OnPlayerJoined(VRCPlayerApi player)
        {
            await UniTask.Yield();
            joined += player != null && player.isLocal ? "L" : "R";
        }

        // virtual の async（値なし）。基底のメソッドから呼んでも、派生の override が動く。InteractByEvent：Interact を、イベントの名前で 2 回呼ぶ
        [HideInInspector] public string hlog = "";

        public virtual async UniTask Hit(int k)
        {
            hlog += "b" + k;
            await UniTask.Yield();
            hlog += "B" + k;
        }

        public void HitTwice()
        {
            Hit(1);
            Hit(2);
        }

        public void InteractByEvent()
        {
            SendCustomEvent("_interact");
            SendCustomEvent("_interact");
        }

        // 名前で呼ばれる public な async void・UniTaskVoid（UI のボタン・SendCustomEvent・SendCustomEventDelayedSeconds。#226）
        [HideInInspector] public string nlog = "";
        public async void Named() { nlog += "a"; await UniTask.Yield(); nlog += "b"; }
        public async UniTaskVoid NamedV() { nlog += "c"; await UniTask.Yield(); nlog += "d"; }
        public void NamedDelayed() { SendCustomEventDelayedSeconds("Named", 0.05f); }

        // SendCustomNetworkEvent で名前を指定して呼ぶ（引数なし・[NetworkCallable] の引数あり。#226）
        [HideInInspector] public string nnet = "";
        [HideInInspector] public string nnet2 = "";
        public async void NamedNet0() { nnet += "e"; await UniTask.Yield(); nnet += "f"; }
        [NetworkCallable]
        public async void NamedNet(int n) { nnet2 += "n" + n; await UniTask.Yield(); nnet2 += "N"; }

        // ワールドのロード直後：OnEnable・Start を async にする（普通の Unity は、OnEnable → Start → 続きの順）
        [HideInInspector] public string elog = "";
        private async void OnEnable() { elog += "e"; await UniTask.Yield(); elog += "E"; }
        private async void Start() { elog += "s"; await UniTask.Yield(); elog += "S"; }

        public void Begin(string tag) { Tick(tag); }

        private async UniTask Tick(string tag)
        {
            for (int i = 0; i < 1000; i++)
            {
                tick += tag;
                await UniTask.Yield();
            }
        }

        // イベントの async（override）：同じ名前の void のメソッドがコルーチンを始める。続けて 2 回呼んでも、混ざらない
        public override async void Interact()
        {
            ilog += "I";
            await UniTask.Yield();
            ilog += "J";
        }

        public void InteractTwice()
        {
            Interact();
            Interact();
        }

        // 引数の違う呼び出しを続けても、それぞれの引数・ローカルが混ざらない
        public void ArgsTwice()
        {
            _ = Arg("a", 1);   // 破棄（警告を消す書き方）で待たずに呼ぶ（#212）
            Arg("b", 2);
        }

        private async UniTask Arg(string t, int k)
        {
            string mine = t + k;
            alog += t;
            await UniTask.Yield();
            alog += k;
            await UniTask.Yield();
            alog += mine;
        }

        // WhenAll の途中で止めると、そのあと再開されない
        public void BeginBoth() { Both(); }

        private async UniTask Both()
        {
            await UniTask.WhenAll(Wait(6), Wait(8));
            wlog += "done";
        }

        private async UniTask Wait(int frames) { await UniTask.DelayFrame(frames); }
    }
}
