using UdonSharp;

namespace UdonSharpPlusTests.PartialMethodBehaviour
{
    // #117: UdonSharpBehaviour の partial メソッドは、UdonSharp のエラーのまま（本パッケージは何もしない）
    public partial class User : UdonSharpBehaviour
    {
        partial void OnHit();
        public void Hit() { OnHit(); }
    }

    public partial class User
    {
        partial void OnHit() { }
    }
}
