using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class WarriorRoutine : SICombatRoutine
    {
        public override Class Class => Class.Warrior;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Self("fear break", 120, SICombatCondition.Fear, "berserker rage"),
            Self("survival", 115, SICombatCondition.Combat | SICombatCondition.CriticalHealth, "shield wall", "enraged regeneration", "last stand"),
            Enemy("interrupt", 110, SICombatCondition.CastingEnemy, "pummel", "shield bash"),
            Enemy("charge", 90, SICombatCondition.Pull | SICombatCondition.Distant, "charge"),
            Self("rage generation", 80, SICombatCondition.Combat | SICombatCondition.Healthy, "bloodrage"),
            Buff("battle shout", 78, "battle shout", "commanding shout"),
            Buff("stance", 77, "battle stance"),
            Enemy("execute", 76, SICombatCondition.Execute, "execute"),
            Dot("bleed", 70, "rend"),
            Enemy("primary strike", 65, SICombatCondition.Melee, "mortal strike", "bloodthirst", "devastate", "shield slam"),
            Enemy("reactive strike", 60, SICombatCondition.Melee, "overpower", "revenge", "victory rush"),
            Enemy("armor", 30, SICombatCondition.Melee, "sunder armor"),
            Enemy("rage dump", 25, SICombatCondition.Melee | SICombatCondition.HighRage, "heroic strike", "cleave"),
            Enemy("slam", 20, SICombatCondition.Melee, "slam")
        };
    }
}
