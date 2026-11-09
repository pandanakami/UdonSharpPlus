namespace FeatureTests.Class
{
    // #65: 実装する汎用クラスが 4 個以上のインタフェース。呼び出しは型番号の switch（ジャンプテーブル）で振り分ける
    public interface ISwitchUnit
    {
        int Power();
        void Boost(int by);
    }

    // 実装する汎用クラスが 3 個以下のインタフェース。is を並べる形のまま
    public interface ISwitchLabel
    {
        string Label();
    }

    public class SwUnitA : ISwitchUnit, ISwitchLabel
    {
        public int power = 1;
        public int Power() { return power; }
        public void Boost(int by) { power += by; }
        public string Label() { return "a"; }
    }

    public class SwUnitB : ISwitchUnit
    {
        public int power = 2;
        public int Power() { return power * 2; }
        public void Boost(int by) { power += by; }
    }

    public class SwUnitC : ISwitchUnit, ISwitchLabel
    {
        public int power = 3;
        public int Power() { return power * 3; }
        public void Boost(int by) { power += by * 2; }
        public string Label() { return "c"; }
    }

    public class SwUnitD : ISwitchUnit
    {
        public int power = 4;
        public int Power() { return power + 10; }
        public void Boost(int by) { power -= by; }
    }

    public class SwUnitE : ISwitchUnit
    {
        public int power = 5;
        public int Power() { return power - 1; }
        public void Boost(int by) { power += by; }
    }

    public class SwUnitF : ISwitchUnit
    {
        public int power = 6;
        public int Power() { return power / 2; }
        public void Boost(int by) { power += by * 3; }
    }

    public class SwUnitG : ISwitchUnit
    {
        public int power = 7;
        public int Power() { return power % 4; }
        public void Boost(int by) { power += by; }
    }

    public class SwUnitH : ISwitchUnit
    {
        public int power = 8;
        public int Power() { return -power; }
        public void Boost(int by) { power += by; }
    }

    // ジェネリクスのコピー（SwBox<int>、SwBox<string>）も、それぞれ 1 クラスとして番号を持つ
    public class SwBox<T> : ISwitchUnit
    {
        public T item;
        public int power;
        public SwBox(T item, int power) { this.item = item; this.power = power; }
        public int Power() { return power; }
        public void Boost(int by) { power += by * 10; }
    }

    // ISwitchUnit を実装しないので、ISwitchUnit の実装クラスと同じ番号になってよい
    public class SwBanner : ISwitchLabel
    {
        public string Label() { return "banner"; }
    }

    // 汎用クラスの中からインタフェースを呼ぶ
    public class SwArmy
    {
        public int Total(ISwitchUnit[] units)
        {
            int sum = 0;
            foreach (ISwitchUnit u in units)
                sum += u.Power();
            return sum;
        }
    }
}
