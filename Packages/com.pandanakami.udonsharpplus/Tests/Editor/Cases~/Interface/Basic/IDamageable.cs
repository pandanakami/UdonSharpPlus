namespace UdonSharpPlusTests.Basic
{
    public interface IDamageable
    {
        int Damage(int amount, string attacker);
        void ResetState();
    }
}
