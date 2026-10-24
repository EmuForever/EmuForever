using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;

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
    public sealed partial class SIPlayer
    {
        // Polymorph crowd control: with two or more attackers, one extra attacker is kept sheeped (and re-sheeped if it
        // breaks) until every other attacker is dead; only then does the encounter turn to it.
        ObjectGuid _magePolymorphTarget;
        long _magePolymorphRetryAt;

        SpellInfo MagePolymorphSpell() => me.GetClass() == Class.Mage ? _knownSpells.FirstOrDefault(s => SpellName(s) == "polymorph") : null;

        bool IsMagePolymorphed(Unit unit) => unit != null && unit.IsAlive() &&
            unit.GetAuraEffectsByType(AuraType.ModConfuse).Any(effect => effect.GetCasterGUID() == me.GetGUID());

        Unit MagePolymorphHeld()
        {
            if (_magePolymorphTarget.IsEmpty()) return null;
            Unit sheep = Resolve(_magePolymorphTarget);
            if (sheep == null || !sheep.IsAlive() || !CanFight(sheep)) { _magePolymorphTarget = ObjectGuid.Empty; return null; }
            return sheep;
        }

        // The sheep stays off-limits while any other attacker is still fighting.
        bool HeldByMagePolymorph(Unit unit)
        {
            if (unit == null || unit.GetGUID() != _magePolymorphTarget) return false;
            return ActiveMobAttackers().Any(other => other != unit && CanFight(other));
        }

        // Damage on the sheep, or area damage near it, would break the Polymorph early.
        bool BreaksMagePolymorph(SpellInfo spell, Unit target)
        {
            if (spell == null || SpellName(spell) == "polymorph" || spell.IsPositive()) return false;
            Unit sheep = MagePolymorphHeld();
            if (sheep == null || !IsMagePolymorphed(sheep) || !HeldByMagePolymorph(sheep)) return false;
            if (target == sheep) return true;
            return spell.IsAffectingArea() && (me.GetExactDist(sheep) <= 15 || (target != null && target.GetExactDist(sheep) <= 12));
        }

        bool TickMagePolymorph(Unit current)
        {
            SpellInfo polymorph = MagePolymorphSpell();
            if (polymorph == null || !me.IsInCombat()) { _magePolymorphTarget = ObjectGuid.Empty; return false; }

            Unit[] enemies = ActiveMobAttackers().Where(CanFight).ToArray();
            Unit sheep = MagePolymorphHeld();
            if (sheep != null)
            {
                Unit[] others = enemies.Where(enemy => enemy != sheep).ToArray();
                // Last one standing: release it so the encounter kills it.
                if (others.Length == 0) { _magePolymorphTarget = ObjectGuid.Empty; return false; }

                // Never fight the sheep while others are alive.
                if (current == null || current == sheep || !current.IsAlive())
                {
                    BeginEncounter(others.OrderBy(enemy => me.GetExactDist(enemy)).First());
                    return true;
                }

                // Broken or expired: sheep it again.
                if (!IsMagePolymorphed(sheep) && Now >= _magePolymorphRetryAt)
                {
                    _magePolymorphRetryAt = Now + 1000;
                    if (Cast(polymorph, sheep)) { Status = $"Polymorph: re-sheeping {sheep.GetName()}"; return true; }
                }
                return false;
            }

            if (enemies.Length < 2 || Now < _magePolymorphRetryAt) return false;
            // Two attackers and the current one is almost dead: finishing it is faster than a 1.5 s cast.
            if (enemies.Length == 2 && current != null && current.IsAlive() && current.HealthBelowPct(20)) return false;

            _magePolymorphRetryAt = Now + 1000;
            foreach (Unit candidate in enemies.Where(enemy => enemy != current && !enemy.HasBreakableByDamageCrowdControlAura())
                .OrderByDescending(enemy => enemy.GetHealthPct()))
            {
                if (!Cast(polymorph, candidate)) continue; // Native checks reject immune creature types, range and sight.
                _magePolymorphTarget = candidate.GetGUID();
                Status = $"Polymorph: holding {candidate.GetName()} until the others are dead";
                return true;
            }
            return false;
        }

        long _mageRetreatUntil, _mageNovaConfirmUntil;
        ObjectGuid _mageRetreatTarget;
        Position _mageRetreatPoint;
        bool TickMageRetreat(Unit enemy)
        {
            if (me.GetClass() != Class.Mage || enemy == null || !enemy.IsAlive()) return false;
            bool rooted = enemy.HasAuraType(AuraType.ModRoot);
            if (_mageRetreatUntil > Now && _mageRetreatTarget == enemy.GetGUID())
            {
                if (!rooted && Now < _mageNovaConfirmUntil) return true;
                if (!rooted || me.GetExactDist(enemy) >= 14)
                {
                    CancelTravel(); _mageRetreatUntil = 0; _mageRetreatPoint = null;
                    if (!me.HasUnitState(UnitState.CannotTurn)) me.SetFacingToObject(enemy);
                    return false;
                }
                if (_mageRetreatPoint == null)
                {
                    float away = MathF.Atan2(me.GetPositionY() - enemy.GetPositionY(), me.GetPositionX() - enemy.GetPositionX());
                    float retreat = Math.Max(4, 16 - me.GetExactDist(enemy));
                    var hazards = RecoveryHazards();
                    foreach (float angle in new[] { 0f, .7f, -.7f, 1.4f, -1.4f })
                    {
                        var candidate = Ground(me.GetPositionX() + MathF.Cos(away + angle) * retreat, me.GetPositionY() + MathF.Sin(away + angle) * retreat, me.GetPositionZ());
                        if (candidate == null || candidate.GetExactDist(enemy) < 14 || !MovementSight(Here, V(candidate)) ||
                            !SICorpseRecovery.AvoidsNewThreats(me.GetPosition(), candidate, hazards)) continue;
                        _mageRetreatPoint = candidate; break;
                    }
                    if (_mageRetreatPoint == null) { _mageRetreatUntil = 0; return false; }
                }
                if (Travel(_mageRetreatPoint, "Moving to casting range after Frost Nova", 1) == SIBehaviorResult.Failure)
                { CancelTravel(); _mageRetreatUntil = 0; _mageRetreatPoint = null; return false; }
                return true; // No offensive cast may break the root before separation.
            }
            if (_mageRetreatUntil != 0) { CancelTravel(); _mageRetreatUntil = 0; _mageRetreatPoint = null; }
            if (!me.IsInCombat() || me.GetExactDist(enemy) > 8 || rooted) return false;
            var nova = _knownSpells.FirstOrDefault(s => SpellName(s) == "frost nova");
            if (nova == null || BreaksMagePolymorph(nova, me)) return false; // Nova damage would break the held sheep.
            me.InterruptSpell(CurrentSpellTypes.AutoRepeat); me.AttackStop();
            if (enemy.HealthBelowPct(10)) return true;
            if (!Cast(nova, me)) return false; // Native cooldown, power and control checks.
            CancelTravel(); _mageRetreatTarget = enemy.GetGUID(); _mageRetreatPoint = null;
            _mageRetreatUntil = Now + 6000; _mageNovaConfirmUntil = Now + 1000; return true;
        }
    }

}
