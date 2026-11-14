using UdonSharp;
using UnityEngine;

namespace FeatureTests.Delegate
{
    // #124 の追加テスト：メソッドを入れられる UdonSharpBehaviour（public・private・protected、呼び返し、インタフェースの実装）
    public class MtTarget : UdonSharpBehaviour, ICmShape
    {
        public int hp = 10;

        public int Pub(int x) => hp + x;
        private int Priv(int x) => hp * x;
        protected int Prot(int x) => hp - x;
        public int Hit(int d) { hp -= d; return hp; }

        public Calc MakePriv() => Priv;
        public Calc MakeProt() => Prot;

        public Vector3 Move(Vector3 v, GameObject go) => v + Vector3.up * hp;

        public int CallBack(Calc c, int x) => c(x) * 2;

        public int Area() => hp;
    }
}
