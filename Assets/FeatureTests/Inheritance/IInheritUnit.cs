namespace FeatureTests.Inheritance
{
    // #92: UdonSharpBehaviour の継承とインタフェースの組み合わせ
    public interface IInheritUnit
    {
        int Hp();
        string Kind();
        void Receive(int value);
    }
}
