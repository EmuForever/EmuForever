using Framework.Configuration;
using System;

namespace Game.AI
{
    public sealed record SISettings(float QuestPickupRadius = 550, float TargetSearchRadius = 270, float GrindRadius = 320,
        int FoodAmount = 20, int DrinkAmount = 20, int MinFreeBagSlots = 2, float MinDurability = .4f, bool Enabled = true,
        int MageFoodAmount = 80, int MageDrinkAmount = 120)
    {
        internal static float Range(float value, float fallback, float maximum) =>
            float.IsFinite(value) ? Math.Clamp(value, 10, maximum) : fallback;
        static float Fraction(float value, float fallback) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
        public static SISettings Load() => new(
            Range(ConfigMgr.GetDefaultValue("SI.QuestPickupRadius", 550f), 550, 1000),
            Range(ConfigMgr.GetDefaultValue("SI.TargetSearchRadius", 270f), 270, 550),
            Range(ConfigMgr.GetDefaultValue("SI.GrindRadius", 320f), 320, 1000),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.FoodAmount", 20), 0, 200),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.DrinkAmount", 20), 0, 200),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.MinFreeBagSlots", 2), 0, 16),
            Fraction(ConfigMgr.GetDefaultValue("SI.MinDurability", .4f), .4f),
            ConfigMgr.GetDefaultValue("SI.Enabled", true),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.MageFoodAmount", 80), 0, 200),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.MageDrinkAmount", 120), 0, 200));
    }
}
