namespace CustomUdonSharpTests.PropertyMember
{
    public interface IHealth
    {
        // プロパティは未対応（警告を出して無視する）
        int Hp { get; }

        void Heal(int amount);
    }
}
