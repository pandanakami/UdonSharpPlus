using Cysharp.Threading.Tasks;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace FeatureTests.ClientCheck
{
    // #227：拾える物。拾う・離す・使う・Interact・オーナー移譲を async にして、実際に起こす。
    // SetActive(false)・Destroy で、この上の async（数える async）が止まるかも確かめる
    public class ClientCheckItem : UdonSharpBehaviour
    {
        public ClientCheck owner;
        [HideInInspector] public int ticks;
        private bool _running;

        public override async void OnPickup()
        {
            owner.Add("C OnPickup " + name);
            await UniTask.Yield();
            owner.Add("C OnPickup continued");
        }

        public override async void OnDrop()
        {
            owner.Add("C OnDrop " + name);
            await UniTask.Yield();
            owner.Add("C OnDrop continued");
        }

        public override async void OnPickupUseDown()
        {
            owner.Add("C OnPickupUseDown " + name);
            await UniTask.Yield();
            owner.Add("C OnPickupUseDown continued");
        }

        public override async void OnPickupUseUp()
        {
            owner.Add("C OnPickupUseUp " + name);
            await UniTask.Yield();
            owner.Add("C OnPickupUseUp continued");
        }

        public override async void Interact()
        {
            owner.Add("C Interact " + name);
            await UniTask.Yield();
            owner.Add("C Interact continued");
        }

        public override async void OnOwnershipTransferred(VRCPlayerApi player)
        {
            owner.Add("E " + name + " OwnershipTransferred -> " + (Utilities.IsValid(player) ? player.displayName : "invalid"));
            await UniTask.Delay(200);
            owner.Add("E " + name + " OwnershipTransferred continued");
        }

        // 0.2 秒ごとに数える（SetActive(false)・Destroy で止まる）
        public void StartTicks()
        {
            if (_running)
                return;
            _running = true;
            TickLoop();
        }

        private async void TickLoop()
        {
            while (true)
            {
                await UniTask.Delay(200);
                ticks++;
                owner.Add("D " + name + " tick " + ticks);
            }
        }

        private void OnDisable()
        {
            _running = false;
        }

        public void TakeOwnerCheck()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            owner.Add("E TakeOwner requested for " + name + "  expect: OwnershipTransferred on both clients");
        }
    }
}
