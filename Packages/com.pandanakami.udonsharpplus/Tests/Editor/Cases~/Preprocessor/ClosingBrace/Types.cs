namespace UdonSharpPlusTests.PreprocessorClosingBrace
{
    public interface IUnit { int Hp(); }

    public class Unit : IUnit
    {
        public int hp = 3;
        public int Hp() { return hp; }
    }
}
