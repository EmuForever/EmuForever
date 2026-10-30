using Framework.Constants;
using System.Collections.Generic;

namespace Game.AI
{
    // Basic solo rotation. Specialization-specific routing can replace this routine later.
    public sealed class MageRoutine : SICombatRoutine
    {
        public override Class Class => Class.Mage;
        public override IReadOnlyList<SICombatRule> Rules => Rotation;
        static readonly SICombatRule[] Rotation =
        {
            Enemy("pull", 90, SICombatCondition.Pull, "frostbolt", "fireball", "frostfire bolt"),
            Self("immunity", 125, SICombatCondition.Combat | SICombatCondition.CriticalHealth, "ice block"),
            Self("shield", 115, SICombatCondition.Combat | SICombatCondition.LowHealth, "ice barrier", "mana shield"),
            Enemy("interrupt", 110, SICombatCondition.CastingEnemy, "counterspell"),
            Self("cleanse", 105, SICombatCondition.SelfDebuff, "remove curse", "remove lesser curse"),
            Buff("armor", 90, "ice armor", "frost armor", "mage armor", "molten armor"),
            Buff("intellect", 85, "arcane intellect", "arcane brilliance"),
            Buff("magic protection", 60, "dampen magic", "amplify magic"),
            Self("mana", 80, SICombatCondition.LowMana, "evocation"),
            new("fingers of frost", 78, SICombatActionTarget.Enemy, SICombatCondition.None, new[] { "deep freeze", "ice lance" }, RequiredAura: "fingers of frost"),
            new("brain freeze", 77, SICombatActionTarget.Enemy, SICombatCondition.None, new[] { "frostfire bolt", "fireball" }, RequiredAura: "brain freeze"),
            Enemy("frozen target", 75, SICombatCondition.RootedEnemy, "ice lance", "deep freeze"),
            // Nova + separation is a retained controller action before damage rules.
            Enemy("close defense", 70, SICombatCondition.Combat | SICombatCondition.Melee, "cone of cold"),
            Dot("fire dot", 60, "living bomb"),
            Enemy("primary", 50, SICombatCondition.None, "frostbolt", "fireball", "arcane blast", "frostfire bolt"),
            Enemy("instant", 40, SICombatCondition.None, "fire blast", "ice lance"),
            Enemy("filler", 20, SICombatCondition.None, "arcane missiles", "scorch", "shoot")
        };
    }
}
