using UdonSharp;

namespace FeatureTests.Delegate
{
    // #123 3: 基底。派生のローカル関数から、override と base.Value の両方を呼ぶ
    public abstract class LocalComboBase : UdonSharpBehaviour
    {
        public virtual int Value(int x) => x + 1;
    }
}
