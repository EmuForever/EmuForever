namespace Game.AI
{
    public static class SIRecoveryConsumables
    {
        public static (bool Health, bool Mana) Needs(float health, float mana, bool usesMana, bool healthAura, bool manaAura, float threshold=50) =>
            (health < threshold && !healthAura, usesMana && mana < threshold && !manaAura);
    }
}
