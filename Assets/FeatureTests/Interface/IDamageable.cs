namespace FeatureTests.Interface
{
    public interface IDamageable
    {
        int Damage(int amount, string attacker);
        void ResetState();
    }
}
