using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FeatureTests.Async
{
    // #207：AsyncPeer を継承して、値を返す virtual の async（Calc）と、イベントの async（Interact）を override する。base を await する
    public class AsyncPeerDerived : AsyncPeer
    {
        public override async UniTask<int> Calc(int k)
        {
            int b = await base.Calc(k);
            await UniTask.Yield();
            return b * 10 + 1;
        }

        public override async UniTask Hit(int k)
        {
            hlog += "d" + k;
            await base.Hit(k);
            hlog += "D" + k;
        }

        public override async void Interact()
        {
            ilog += "D";
            await UniTask.Yield();
            ilog += "E";
        }
    }
}
