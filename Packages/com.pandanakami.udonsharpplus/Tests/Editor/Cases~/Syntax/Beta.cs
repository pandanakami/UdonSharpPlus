namespace UdonSharpPlusTests.Syntax.Beta
{
    // #79 c: Alpha.Item と同じ名前のクラス
    public class Item
    {
        public string s;

        public Item(string s) { this.s = s; }

        public string Show() => "[" + s + "]";
    }
}
