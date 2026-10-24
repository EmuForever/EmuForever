using Framework.Constants;
using System;
using System.Collections.Generic;

namespace Game.AI
{
    // Shared combat facts and spell selection. Class rotations live under Routines/Classes.
    // Every action uses learned spells through the core cast gate; unavailable abilities fall through.
    [Flags]
    public enum SICombatCondition : ulong
    {
        None = 0, Combat = 1, Peace = 2, Pull = 4, Melee = 8, Distant = 16,
        LowHealth = 32, CriticalHealth = 64, LowMana = 128, Healthy = 256,
        Combo = 512, Finish = 1024, Execute = 2048, Stealthed = 4096,
        Behind = 8192, CastingEnemy = 16384, MovingEnemy = 32768,
        RootedEnemy = 65536, Multiple = 131072, PetAbsent = 262144,
        PetDead = 524288, PetHurt = 1048576, Fear = 2097152, Rooted = 4194304,
        HighRage = 8388608, SelfDebuff = 16777216, EnemyBuff = 33554432,
        Cat = 67108864, Bear = 134217728, Caster = 268435456,
        Energy = 536870912
    }

    public enum SICombatActionTarget { Enemy, Self, Pet }
    public enum SICombatActionStage { Rest, PreCombatBuff, PullBuff, Pull, Reaction, Heal, CombatBuff, Combat }

    public sealed record SICombatRule(string Name, int Priority, SICombatActionTarget Target,
        SICombatCondition Conditions, string[] Alternatives, int RetryMilliseconds = 1000,
        bool MissingAura = false, string RequiredAura = null, int DispelType = 0, SICombatActionStage Stage = SICombatActionStage.Combat);

    public readonly record struct SICombatFacts(bool Combat, float Health, float Mana,
        long Rage, long Energy, long Combo, float EnemyHealth, float Distance, float MeleeRange,
        bool Behind, bool Stealthed, bool CastingEnemy, bool MovingEnemy, bool RootedEnemy,
        int Attackers, bool HasPet, bool PetDead, float PetHealth, bool Feared, bool Rooted,
        bool SelfDebuff, bool EnemyBuff, byte Form)
    {
        public bool Matches(SICombatCondition required)
        {
            bool Has(SICombatCondition flag) => (required & flag) != 0;
            if (Has(SICombatCondition.Combat) && !Combat || Has(SICombatCondition.Peace) && Combat) return false;
            if (Has(SICombatCondition.Pull) && Combat) return false;
            if (Has(SICombatCondition.Melee) && Distance > MeleeRange || Has(SICombatCondition.Distant) && Distance <= MeleeRange) return false;
            if (Has(SICombatCondition.LowHealth) && Health >= .55f || Has(SICombatCondition.CriticalHealth) && Health >= .3f) return false;
            if (Has(SICombatCondition.LowMana) && Mana >= .3f || Has(SICombatCondition.Healthy) && Health < .7f) return false;
            if (Has(SICombatCondition.Combo) && Combo < 4 || Has(SICombatCondition.Finish) && (Combo < 1 || EnemyHealth > .2f)) return false;
            if (Has(SICombatCondition.Execute) && EnemyHealth > .2f) return false;
            if (Has(SICombatCondition.Stealthed) && !Stealthed || Has(SICombatCondition.Behind) && !Behind) return false;
            if (Has(SICombatCondition.CastingEnemy) && !CastingEnemy || Has(SICombatCondition.MovingEnemy) && !MovingEnemy) return false;
            if (Has(SICombatCondition.RootedEnemy) && !RootedEnemy || Has(SICombatCondition.Multiple) && Attackers < 3) return false;
            if (Has(SICombatCondition.PetAbsent) && HasPet || Has(SICombatCondition.PetDead) && !PetDead) return false;
            if (Has(SICombatCondition.PetHurt) && (!HasPet || PetDead || PetHealth >= .6f)) return false;
            if (Has(SICombatCondition.Fear) && !Feared || Has(SICombatCondition.Rooted) && !Rooted) return false;
            if (Has(SICombatCondition.HighRage) && Rage < 500 || Has(SICombatCondition.Energy) && Energy < 45) return false;
            if (Has(SICombatCondition.SelfDebuff) && !SelfDebuff || Has(SICombatCondition.EnemyBuff) && !EnemyBuff) return false;
            if (Has(SICombatCondition.Cat) && Form != 1 || Has(SICombatCondition.Bear) && Form is not (5 or 8)) return false;
            if (Has(SICombatCondition.Caster) && Form is 1 or 5 or 8) return false;
            return true;
        }
    }

    public readonly record struct SIClassStrategy(Class Class, bool Melee, SpellSchoolMask PreferredSchool, float CombatRange)
    {
        public static SIClassStrategy For(Class cls, byte form = 0) => cls switch
        {
            Class.Warrior => new(cls, true, SpellSchoolMask.Normal, 3),
            Class.Paladin => new(cls, true, SpellSchoolMask.Holy, 3),
            Class.Hunter => new(cls, false, SpellSchoolMask.Normal, 20),
            Class.Rogue => new(cls, true, SpellSchoolMask.Normal, 3),
            Class.Priest => new(cls, false, SpellSchoolMask.Shadow, 20),
            Class.DeathKnight => new(cls, true, SpellSchoolMask.Frost, 3),
            Class.Shaman => new(cls, false, SpellSchoolMask.Nature, 20),
            Class.Mage => new(cls, false, SpellSchoolMask.Frost, 25),
            Class.Warlock => new(cls, false, SpellSchoolMask.Shadow, 25),
            Class.Druid when form is 1 or 5 or 8 => new(cls, true, SpellSchoolMask.Normal, 3),
            Class.Druid => new(cls, false, SpellSchoolMask.Nature, 20),
            _ => new(cls, true, SpellSchoolMask.Normal, 3)
        };
    }

    public static class SIClassSpellPriorities
    {
        // Tactical priority by learned spell name (all ranks); damage scoring keeps rank order inside a tier.
        public static float ScoreNamed(SIClassStrategy strategy, string name, float damageScore,
            long comboPoints, float targetHealthFraction, float manaFraction = 1, float healthFraction = 1)
        {
            if (strategy.Class == Class.Rogue && name is "eviscerate" or "envenom" or "rupture" && comboPoints < 3)
                return float.NegativeInfinity;
            if (strategy.Class == Class.Warrior && name == "execute" && targetHealthFraction > .2f)
                return float.NegativeInfinity;
            int priority = strategy.Class switch
            {
                Class.Warrior => name switch { "execute" => 80, "mortal strike" or "bloodthirst" => 60, "rend" => 45, "slam" => 30, "heroic strike" => 10, _ => 0 },
                Class.Paladin => name switch { "judgement" or "judgment" or "judgement of righteousness" => 70, "crusader strike" => 60, "holy strike" => 50, _ => 0 },
                Class.Hunter => name switch { "serpent sting" => 80, "aimed shot" => 70, "arcane shot" => 60, "steady shot" => 40, "auto shot" or "shoot" => 10, _ => 0 },
                Class.Rogue => name switch { "eviscerate" or "envenom" => 80, "rupture" => 75, "sinister strike" => 60, "hemorrhage" => 55, _ => 0 },
                Class.Priest => name switch { "shadow word: pain" => 80, "mind blast" => 70, "mind flay" => 60, "smite" => 40, _ => 0 },
                Class.DeathKnight => name switch { "icy touch" or "plague strike" => 70, "death strike" => 60, "blood strike" => 40, _ => 0 },
                Class.Shaman => name switch { "flame shock" => 80, "stormstrike" => 70, "earth shock" => 60, "lightning bolt" => 40, _ => 0 },
                Class.Mage => name switch { "frostbolt" => 60, "ice lance" or "fire blast" => 50, "fireball" => 40, "arcane missiles" => 30, "shoot" => manaFraction < .2f ? 90 : 10, _ => 0 },
                Class.Warlock => name switch { "corruption" => 80, "immolate" => 70, "drain life" => healthFraction < .5f ? 85 : 30,
                    "drain soul" => targetHealthFraction < .2f ? 80 : 20, "shadow bolt" => 50, "shoot" => manaFraction < .2f ? 90 : 10, _ => 0 },
                Class.Druid => name switch { "moonfire" or "rake" => 80, "mangle" => 70, "claw" => 60, "wrath" => 50, _ => 0 },
                _ => 0
            };
            return priority * 1000 + damageScore;
        }
    }

    public readonly record struct SIPullAssessment(bool Allowed, string Reason);

    // Port of SIPullSafety: local safety before an optional pull. Never stops defense against an existing
    // attacker and never changes the server's combat rules.
    public static class SIPullSafety
    {
        public const float MinimumHealth = .9f;
        public const float MinimumMana = .6f;
        public static bool RecoveryReady(float healthPercent, float manaPercent, bool usesMana) =>
            healthPercent / 100 >= MinimumHealth && (!usesMana || manaPercent / 100 >= MinimumMana);
        public static SIPullAssessment Assess(int playerLevel, int creatureLevel,
            CreatureClassifications classification, float health, float mana, bool usesMana,
            int additionalThreats, bool learnedDanger, bool recovering, bool questTarget = false)
        {
            if (recovering) return new(false, "Recovery has priority over another pull.");
            if (learnedDanger) return new(false, "This enemy was learned to be lethal.");
            if (!float.IsFinite(health) || !float.IsFinite(mana) || playerLevel < 1 || creatureLevel < 1)
                return new(false, "Combat readiness is unknown.");
            if (health < MinimumHealth) return new(false, "Recover health before an optional fight.");
            if (usesMana && mana < MinimumMana) return new(false, "Recover mana before an optional fight.");
            int allowance = questTarget && classification == CreatureClassifications.Normal ? 1 : 0;
            if (creatureLevel > playerLevel + allowance) return new(false, "Target exceeds the solo level budget.");
            // A rare normal creature is not an elite.
            if (classification is CreatureClassifications.Elite or CreatureClassifications.RareElite or CreatureClassifications.Obsolete
                && creatureLevel > playerLevel - 5)
                return new(false, "Elite or boss requires a stronger character or a group.");
            if (additionalThreats > 0)
                return new(false, "Pull would engage an unsafe group of hostiles.");
            return new(true, "A level-appropriate solo pull is available.");
        }
    }

}
