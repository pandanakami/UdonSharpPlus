using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;

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
            Arg("a", 1);
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
