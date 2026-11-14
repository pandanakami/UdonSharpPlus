using UdonSharp;

namespace FeatureTests.Delegate
{
    // #123: 別の UdonSharpBehaviour。ローカル関数・匿名メソッドから作ったデリゲートを受け取って呼ぶ（3）・
    // あとのフレームで呼ぶ（9）
    public class LocalComboKeeper : UdonSharpBehaviour
    {
        private Calc _stored;
        private Calc _later;

        public void Store(Calc c) { _stored = c; }

        public int CallStored(int x) => _stored(x);

        public void StoreLater(Calc c) { _later = c; }

        public int CallLater(int x) => _later(x);
    }
}
