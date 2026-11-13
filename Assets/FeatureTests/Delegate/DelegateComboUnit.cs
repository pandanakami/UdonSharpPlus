using UdonSharp;

namespace FeatureTests.Delegate
{
    // #126: インタフェースを実装する UdonSharpBehaviour（A1・A2）、ほかの UdonSharpBehaviour の static メソッド（B8）、
    // イベントのメソッドを入れる（B7）
    public class DelegateComboUnit : UdonSharpBehaviour, IComboTarget
    {
        public int v = 50;
        public int interactCount;
        private Calc _calc;

        public int Hit(int dmg) { v -= dmg; return v; }
        public void SetCalc(Calc c) { _calc = c; }
        public int UseCalc(int x) => _calc(x) + 2000;

        public static int Triple(int x) => x * 3;

        // A2：インタフェースの実装メソッドをデリゲートに入れる
        public int HitViaDelegate(int dmg)
        {
            Calc c = Hit;
            return c(dmg);
        }

        public override void Interact() { interactCount++; }

        // B7：イベントのメソッドを入れる
        public int InteractViaDelegate()
        {
            Act a = Interact;
            a();
            a.Invoke();
            return interactCount;
        }
    }
}
