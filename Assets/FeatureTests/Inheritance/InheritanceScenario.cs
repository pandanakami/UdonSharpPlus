using UdonSharp;

namespace FeatureTests.Inheritance
{
    // #92: Udon 上と、エディタの普通の C# の両方で実行して結果を比べる
    public static class InheritanceScenario
    {
        public static string Run(UdonSharpBehaviour[] targets, InheritUnitBase baseTyped, UdonSharpBehaviour notUnit)
        {
            string r = "";
            foreach (UdonSharpBehaviour t in targets)
                ((InheritUnitBase)t).hp = 10;

            // a: インタフェース経由の呼び出しが、override の方（Knight）・基底の方（Archer）に届く
            foreach (UdonSharpBehaviour t in targets)
            {
                if (t is IInheritUnit)
                {
                    IInheritUnit u = (IInheritUnit)t;
                    r += u.Hp() + "," + u.Kind() + ",";
                    u.Receive(1);
                }
                IInheritUnit a = t as IInheritUnit;
                r += (a != null ? a.Hp().ToString() : "null") + " ";
            }
            r += ";";

            // b: 実装していない UdonSharpBehaviour の is / as
            IInheritUnit none = notUnit as IInheritUnit;
            r += (notUnit is IInheritUnit) + "," + (none == null) + ";";

            // c: 基底の型で受けたものをインタフェースにして呼ぶ・基底の型のまま呼ぶ
            IInheritUnit fromBase = baseTyped;
            r += fromBase.Hp() + "," + baseTyped.Hp() + ",";
            fromBase.Receive(5);
            r += baseTyped.hp + ";";

            // d: インタフェース型の配列に入れて回す
            IInheritUnit[] units = new IInheritUnit[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                units[i] = (IInheritUnit)targets[i];
            int sum = 0;
            foreach (IInheritUnit u in units)
                sum += u.Hp();
            r += sum + ";";
            return r;
        }
    }
}
