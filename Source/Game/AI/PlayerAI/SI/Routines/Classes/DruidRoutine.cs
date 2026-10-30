using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class DruidRoutine : SICombatRoutine
    {
        public override Class Class => Class.Druid;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "wrath", "moonfire"),
            Self("defense", 125, SICombatCondition.Combat | SICombatCondition.LowHealth, "barkskin", "survival instincts"),
            Self("healing", 115, SICombatCondition.LowHealth | SICombatCondition.Caster, "rejuvenation", "swiftmend", "regrowth", "healing touch"),
            Self("bear heal", 110, SICombatCondition.LowHealth | SICombatCondition.Bear, "frenzied regeneration"),
            Self("cleanse", 105, SICombatCondition.SelfDebuff | SICombatCondition.Caster, "remove corruption", "abolish poison", "cure poison", "remove curse"),
            Buff("mark", 90, "mark of the wild", "gift of the wild"),
            Buff("thorns", 85, "thorns"),
            Enemy("cat opener", 80, SICombatCondition.Cat | SICombatCondition.Stealthed | SICombatCondition.Behind, "pounce", "ravage"),
            Enemy("cat finisher", 75, SICombatCondition.Cat | SICombatCondition.Combo, "ferocious bite", "rip"),
            Dot("cat bleed", 70, "rake"),
            Enemy("cat builder", 65, SICombatCondition.Cat | SICombatCondition.Melee, "mangle (cat)", "shred", "claw"),
            Enemy("bear strike", 65, SICombatCondition.Bear | SICombatCondition.Melee, "mangle (bear)", "lacerate", "maul", "swipe"),
            Dot("moonfire", 65, "moonfire"),
            Dot("insect swarm", 64, "insect swarm"),
            new("eclipse lunar", 63, SICombatActionTarget.Enemy, SICombatCondition.Caster, new[] { "starfire" }, RequiredAura: "eclipse (lunar)"),
            new("eclipse solar", 63, SICombatActionTarget.Enemy, SICombatCondition.Caster, new[] { "wrath" }, RequiredAura: "eclipse (solar)"),
            Enemy("caster filler", 40, SICombatCondition.Caster, "wrath", "starfire")
        };
    }
}
