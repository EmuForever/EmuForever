using System;

namespace Game.Systems.Legacy
{
    public static class LegacyEffectEngine
    {
        [ThreadStatic] public static int GatheringBonus;
        public static float GatheringChance(int itemId, float chance) =>
            itemId is 249391 or 249399 or 249427 or 268415 or 268418 ? chance * (1 + GatheringBonus / 100.0f) : chance;
        // Avoid unsigned wrap and preserve the core's one-copper minimum for priced services.
        public static ulong Price(ulong price, int percent)
        {
            if (price == 0) return 0;
            decimal adjusted = price * Math.Max(0m, 100m + percent) / 100m;
            return (ulong)Math.Clamp(adjusted, 1m, ulong.MaxValue);
        }
        public static int SkillChance(int chance, int percent) => chance <= 0 ? 0 :
            (int)Math.Clamp((long)chance + (long)chance * percent / 100, 0, 1000);
        public static double DurabilityLoss(double fraction, int percent) => Math.Max(0, fraction * (1 + percent / 100.0));
        public static int TalentPoints(int level, int normalPoints, int startLevel, int cap) =>
            Math.Min(cap, normalPoints + (startLevel > 0 && startLevel < 10 ? Math.Max(0, Math.Min(level, 9) - startLevel + 1) : 0));
    }
}
