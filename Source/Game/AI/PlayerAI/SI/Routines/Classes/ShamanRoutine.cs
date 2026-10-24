using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class ShamanRoutine : SICombatRoutine
    {
        public override Class Class => Class.Shaman;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "lightning bolt", "flame shock"),
            Self("emergency heal", 120, SICombatCondition.CriticalHealth, "riptide", "lesser healing wave", "healing wave"),
            Self("heal", 110, SICombatCondition.LowHealth, "lesser healing wave", "healing wave"),
            Enemy("interrupt", 105, SICombatCondition.CastingEnemy, "wind shear", "earth shock"),
            Self("cleanse", 100, SICombatCondition.SelfDebuff, "cleanse spirit", "cure toxins", "cure poison", "cure disease"),
            Enemy("purge", 95, SICombatCondition.EnemyBuff, "purge"),
            Buff("shield", 85, "lightning shield", "water shield"),
            Dot("flame shock", 75, "flame shock"),
            Enemy("enhancement", 65, SICombatCondition.Melee, "stormstrike", "lava lash"),
            Enemy("shock", 55, SICombatCondition.None, "earth shock", "frost shock"),
            Enemy("elemental", 50, SICombatCondition.None, "lava burst", "lightning bolt", "chain lightning"),
            Self("mana", 80, SICombatCondition.Combat | SICombatCondition.LowMana, "shamanistic rage", "thunderstorm")
        };
    }
}
