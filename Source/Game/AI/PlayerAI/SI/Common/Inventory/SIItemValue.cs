using Framework.Constants;
using Game.Entities;
using System;

namespace Game.AI;

// How much an item is worth wearing, comparable within one equipment slot.
public static class SIItemValue
{
    // Equipping needs at least this much more value, so equal items never swap back and forth.
    public const float UpgradeMargin = .25f;

    public static float Score(ItemTemplate template)
    {
        if (template == null) return 0;
        float stats = 0;
        for (int i = 0; i < ItemConst.MaxStats; ++i) stats += Math.Max(0, (int)template.GetStatModifierBonusAmount(i));
        // Roughly a stat point per eight armor, three per point of weapon damage per second. Item level and
        // quality only break ties: they already show up in the armor, damage and stats.
        return stats + template.GetArmor() / 8f + template.GetDPS() * 3 + template.GetItemLevel() * .25f + (int)template.GetQuality() * .5f;
    }
}
