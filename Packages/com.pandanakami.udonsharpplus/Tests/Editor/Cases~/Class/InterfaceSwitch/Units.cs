namespace UdonSharpPlusTests.ClassInterfaceSwitch
{
    // 実装する汎用クラスが 4 個以上：型番号の switch で振り分ける（#65）
    public interface IUnit
    {
        int Power();
        void Boost(int by);
    }

    // 実装する汎用クラスが 3 個以下：is を並べる形のまま
    public interface ILabel
    {
        string Label();
    }

    public class Archer : IUnit, ILabel
    {
        public int power = 1;
        public int Power() { return power; }
        public void Boost(int by) { power += by; }
        public string Label() { return "archer"; }
    }

    public class Knight : IUnit
    {
        public int power = 2;
        public int Power() { return power * 2; }
        public void Boost(int by) { power += by; }
    }

    public class Mage : IUnit, ILabel
    {
        public int power = 3;
        public int Power() { return power * 3; }
        public void Boost(int by) { power += by * 2; }
        public string Label() { return "mage"; }
    }

    public class Thief : IUnit
    {
        public int power = 4;
        public int Power() { return power + 10; }
        public void Boost(int by) { power -= by; }
    }

    // IUnit を実装しないので、IUnit の実装クラス（Knight）と同じ番号になってよい
    public class Banner : ILabel
    {
        public string Label() { return "banner"; }
    }

    // 汎用クラスの中からインタフェースを使う
    public class Army
    {
        public int Total(IUnit[] units)
        {
            int sum = 0;
            foreach (IUnit u in units)
                sum += u.Power();
            return sum;
        }
    }
}
