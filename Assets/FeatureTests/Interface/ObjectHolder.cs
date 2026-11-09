using UdonSharp;

namespace FeatureTests.Interface
{
    // #33: 別の Behaviour へ object を渡して戻す確認用（SetProgramVariable / GetProgramVariable）
    public class ObjectHolder : UdonSharpBehaviour
    {
        [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */ public object value;
    }
}
