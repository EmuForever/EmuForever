using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class HunterRoutine : SICombatRoutine
    {
        public override Class Class => Class.Hunter;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull | SICombatCondition.Distant, "serpent sting", "arcane shot", "auto shot"),
            Self("defense", 120, SICombatCondition.Combat | SICombatCondition.LowHealth, "deterrence"),
            Self("pet revive", 100, SICombatCondition.Peace | SICombatCondition.PetDead, "revive pet"),
            Self("pet summon", 95, SICombatCondition.Peace | SICombatCondition.PetAbsent, "call pet", "call pet 1"),
            new("pet heal", 90, SICombatActionTarget.Pet, SICombatCondition.PetHurt, new[] { "mend pet" }, 1000, true, Stage: SICombatActionStage.Heal),
            Buff("aspect", 85, "aspect of the hawk", "aspect of the monkey"),
            Dot("mark", 80, "hunter's mark"),
            Enemy("snare", 75, SICombatCondition.Combat | SICombatCondition.MovingEnemy, "concussive shot"),
            Enemy("melee fallback", 110, SICombatCondition.Melee, "mongoose bite", "raptor strike", "wing clip"),
            Enemy("execute", 69, SICombatCondition.Execute, "kill shot"),
            Dot("sting", 65, "serpent sting"),
            Enemy("ranged strike", 60, SICombatCondition.Distant, "chimera shot", "explosive shot", "aimed shot", "arcane shot"),
            Enemy("ranged filler", 30, SICombatCondition.Distant, "steady shot", "auto shot", "shoot")
        };
    }
}
