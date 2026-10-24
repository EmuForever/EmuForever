using Framework.Configuration;
using System;

namespace Game.AI
{
    public sealed record SISettings(float QuestPickupRadius = 550, float TargetSearchRadius = 270, float GrindRadius = 320,
        int FoodAmount = 20, int DrinkAmount = 20, int MinFreeBagSlots = 2, float MinDurability = .4f, float QuestAreaRadius = 500, bool Enabled = true)
    {
        internal static float Range(float value, float fallback, float maximum) =>
            float.IsFinite(value) ? Math.Clamp(value, 10, maximum) : fallback;
        public static SISettings Load() => new(
            Range(ConfigMgr.GetDefaultValue("SI.QuestPickupRadius", 550f), 550, 1000),
            Range(ConfigMgr.GetDefaultValue("SI.TargetSearchRadius", 270f), 270, 550),
            Range(ConfigMgr.GetDefaultValue("SI.GrindRadius", 320f), 320, 1000),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.FoodAmount", 20), 0, 200),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.DrinkAmount", 20), 0, 200),
            Math.Clamp(ConfigMgr.GetDefaultValue("SI.MinFreeBagSlots", 2), 0, 16),
            float.IsFinite(ConfigMgr.GetDefaultValue("SI.MinDurability", .4f)) ? Math.Clamp(ConfigMgr.GetDefaultValue("SI.MinDurability", .4f), 0, 1) : .4f,
            Range(ConfigMgr.GetDefaultValue("SI.QuestAreaRadius", 500f), 500, 1000),
            ConfigMgr.GetDefaultValue("SI.Enabled", true));
    }
}
