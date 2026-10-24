using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class DeathKnightRoutine : SICombatRoutine
    {
        public override Class Class => Class.DeathKnight;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Self("defense", 120, SICombatCondition.Combat | SICombatCondition.LowHealth, "icebound fortitude", "rune tap", "anti-magic shell"),
            Enemy("self healing strike", 115, SICombatCondition.LowHealth | SICombatCondition.Melee, "death strike"),
            Enemy("interrupt", 110, SICombatCondition.CastingEnemy, "mind freeze", "strangulate"),
            Buff("presence", 90, "blood presence", "frost presence", "unholy presence"),
            Buff("horn", 85, "horn of winter"),
            Enemy("pull", 80, SICombatCondition.Pull | SICombatCondition.Distant, "death grip", "icy touch"),
            Dot("frost disease", 75, "icy touch"),
            Dot("blood disease", 74, "plague strike"),
            Enemy("primary", 65, SICombatCondition.Melee, "obliterate", "scourge strike", "heart strike", "death strike"),
            Enemy("rune filler", 40, SICombatCondition.Melee, "blood strike"),
            Enemy("runic power", 35, SICombatCondition.None, "frost strike", "death coil")
        };
    }
}
