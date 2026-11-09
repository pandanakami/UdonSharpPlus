using UdonSharp;

namespace FeatureTests.Interface
{
    // #76: GetComponent で取ったあと、インタフェース経由で呼ぶための相手。状態を変えない（ほかのテストに影響しない）
    public interface IGreeter
    {
        string Greet(string to);
    }

    public class GreetTarget : UdonSharpBehaviour, IGreeter
    {
        public string Greet(string to) { return "hi " + to; }
    }
}
