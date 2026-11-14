using UdonSharp;

namespace FeatureTests.Delegate
{
    // #124: ほかの UdonSharpBehaviour のメソッドを入れる（インタフェースの実装でもある）
    public class CmTarget : UdonSharpBehaviour, ICmShape
    {
        public int hp = 100;

        public int Hit(int d) { hp -= d; return hp; }
        public virtual int Kind(int x) => x + 1;
        public int Area() => hp;
    }
}
