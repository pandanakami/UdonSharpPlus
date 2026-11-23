using UdonSharp;

namespace FeatureTests.ClientCheck
{
    // #227：押す（Interact）と、ClientCheck.Run(id) を呼ぶ
    public class ClientCheckButton : UdonSharpBehaviour
    {
        public ClientCheck target;
        public int id;

        public override void Interact()
        {
            target.Run(id);
        }
    }
}
