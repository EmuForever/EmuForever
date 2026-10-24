using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class RogueRoutine : SICombatRoutine
    {
        public override Class Class => Class.Rogue;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "throw", "shoot"),
            Self("evasion", 120, SICombatCondition.Combat | SICombatCondition.LowHealth, "evasion", "combat readiness"),
            Self("spell defense", 119, SICombatCondition.Combat | SICombatCondition.CriticalHealth, "cloak of shadows"),
            Enemy("interrupt", 110, SICombatCondition.CastingEnemy | SICombatCondition.Melee, "kick"),
            Enemy("stealth opener", 100, SICombatCondition.Stealthed | SICombatCondition.Behind | SICombatCondition.Melee, "garrote", "ambush"),
            Enemy("stealth stun", 95, SICombatCondition.Stealthed | SICombatCondition.Melee, "cheap shot"),
            new("attack speed finisher", 85, SICombatActionTarget.Self, SICombatCondition.Combo, new[] { "slice and dice" }, 5000, true, Stage: SICombatActionStage.CombatBuff),
            Enemy("finish dying target", 84, SICombatCondition.Finish | SICombatCondition.Melee, "envenom", "eviscerate"),
            Enemy("finisher", 80, SICombatCondition.Combo | SICombatCondition.Melee, "envenom", "eviscerate", "rupture"),
            Enemy("positioned builder", 65, SICombatCondition.Behind | SICombatCondition.Melee | SICombatCondition.Energy, "backstab"),
            Enemy("builder", 60, SICombatCondition.Melee, "mutilate", "sinister strike", "hemorrhage", "ghostly strike"),
            Self("energy", 50, SICombatCondition.Combat, "adrenaline rush")
        };
    }
}
