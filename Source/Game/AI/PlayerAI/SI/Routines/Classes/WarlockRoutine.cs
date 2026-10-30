using Framework.Constants;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class WarlockRoutine : SICombatRoutine
    {
        public override Class Class => Class.Warlock;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        public static bool IsHealthFunnel(SpellInfo spell) => spell != null &&
            (spell.GetFirstRankSpell().Id == 755 ||
             string.Equals(spell.SpellName?[Locale.enUS]?.Trim(), "health funnel", StringComparison.OrdinalIgnoreCase));

        // Funnel spends the caster's health. Recheck throughout the channel,
        // not just when selecting a spell, and keep the normal Healthy reserve.
        public static bool CanHealthFunnel(bool hasPet, bool petAlive, float petHealth, float casterHealth) =>
            hasPet && petAlive && petHealth > 0 && petHealth < .25f && casterHealth >= .7f;

        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "shadow bolt", "immolate"),
            Enemy("heal through damage", 115, SICombatCondition.LowHealth, "drain life", "death coil"),
            new("emergency pet heal", 110, SICombatActionTarget.Pet,
                SICombatCondition.PetCriticalHealth | SICombatCondition.Healthy, new[] { "health funnel" },
                Stage: SICombatActionStage.Heal),
            Self("mana conversion", 90, SICombatCondition.LowMana | SICombatCondition.Healthy, "life tap", "dark pact"),
            Self("pet summon", 85, SICombatCondition.Peace | SICombatCondition.PetAbsent, "summon voidwalker", "summon imp", "summon felguard", "summon felhunter", "summon succubus"),
            Buff("armor", 84, "fel armor", "demon armor", "demon skin"),
            Dot("curse", 75, "curse of agony", "curse of the elements", "curse of weakness"),
            Dot("corruption", 74, "corruption"),
            Dot("immolate", 73, "immolate", "unstable affliction"),
            Enemy("execute", 70, SICombatCondition.Execute, "drain soul", "shadowburn"),
            Enemy("primary", 60, SICombatCondition.None, "chaos bolt", "haunt", "conflagrate", "shadow bolt", "incinerate"),
            Enemy("filler", 20, SICombatCondition.None, "searing pain", "shoot")
        };
    }
}
