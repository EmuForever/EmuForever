using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class PriestRoutine : SICombatRoutine
    {
        public override Class Class => Class.Priest;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "holy fire", "smite", "mind blast"),
            Self("emergency heal", 125, SICombatCondition.CriticalHealth, "desperate prayer", "flash heal", "lesser heal"),
            Self("shield", 115, SICombatCondition.Combat | SICombatCondition.LowHealth, "power word: shield"),
            Self("heal", 110, SICombatCondition.LowHealth, "flash heal", "heal", "greater heal", "lesser heal"),
            Self("cleanse", 105, SICombatCondition.SelfDebuff, "dispel magic", "abolish disease", "cure disease"),
            Enemy("interrupt", 100, SICombatCondition.CastingEnemy, "silence"),
            Buff("fortitude", 85, "power word: fortitude", "prayer of fortitude"),
            Buff("inner armor", 84, "inner fire"),
            Buff("spirit", 83, "divine spirit"),
            Self("mana recovery", 80, SICombatCondition.LowMana, "shadowfiend", "hymn of hope"),
            Dot("pain", 70, "shadow word: pain"),
            Dot("vampiric touch", 69, "vampiric touch"),
            Enemy("execute", 68, SICombatCondition.Execute | SICombatCondition.Healthy, "shadow word: death"),
            Enemy("primary", 60, SICombatCondition.None, "mind blast", "holy fire"),
            Enemy("filler", 30, SICombatCondition.None, "mind flay", "smite", "shoot")
        };
    }
}
