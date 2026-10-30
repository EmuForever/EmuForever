using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public abstract class SICombatRoutine
    {
        public abstract Class Class { get; }
        public abstract IReadOnlyList<SICombatRule> Rules { get; }
        // Rules are static per class: the per-stage order and the managed buff names are computed once.
        // Concurrent first use from two map threads computes the same values, so no lock is needed.
        SICombatRule[][] _stages;
        HashSet<string> _managedBuffs;
        public IReadOnlyList<SICombatRule> ForStage(SICombatActionStage stage)
        {
            _stages ??= Enum.GetValues<SICombatActionStage>().Select(s => Rules
                .Where(rule => rule.Stage == s || s == SICombatActionStage.CombatBuff && rule.Stage == SICombatActionStage.PreCombatBuff)
                .OrderByDescending(rule => rule.Priority).ToArray()).ToArray();
            return _stages[(int)stage];
        }
        // Spell names a MissingAura rule maintains; generic buff upkeep leaves them to the routine.
        public IReadOnlySet<string> ManagedBuffs => _managedBuffs ??=
            Rules.Where(rule => rule.MissingAura).SelectMany(rule => rule.Alternatives).ToHashSet(StringComparer.Ordinal);

        protected static SICombatRule Enemy(string name, int priority, SICombatCondition when, params string[] alternatives) =>
            new(name, priority, SICombatActionTarget.Enemy, when, alternatives, Stage: StageFor(when, false));
        protected static SICombatRule Self(string name, int priority, SICombatCondition when, params string[] alternatives) =>
            new(name, priority, SICombatActionTarget.Self, when, alternatives, 1000, Stage: StageFor(when, true));
        protected static SICombatRule Buff(string name, int priority, params string[] alternatives) =>
            new(name, priority, SICombatActionTarget.Self, SICombatCondition.None, alternatives, 1000, true, Stage: SICombatActionStage.PreCombatBuff);
        protected static SICombatRule Dot(string name, int priority, params string[] alternatives) =>
            new(name, priority, SICombatActionTarget.Enemy, SICombatCondition.None, alternatives, 1000, true);

        static SICombatActionStage StageFor(SICombatCondition conditions, bool self)
        {
            if ((conditions & (SICombatCondition.Fear | SICombatCondition.Rooted | SICombatCondition.CastingEnemy |
                SICombatCondition.SelfDebuff | SICombatCondition.EnemyBuff)) != 0) return SICombatActionStage.Reaction;
            if ((conditions & (SICombatCondition.LowHealth | SICombatCondition.CriticalHealth | SICombatCondition.PetHurt | SICombatCondition.PetCriticalHealth)) != 0)
                return SICombatActionStage.Heal;
            if ((conditions & (SICombatCondition.PetAbsent | SICombatCondition.PetDead)) != 0) return SICombatActionStage.PreCombatBuff;
            if ((conditions & SICombatCondition.Pull) != 0) return self ? SICombatActionStage.PullBuff : SICombatActionStage.Pull;
            return self ? SICombatActionStage.CombatBuff : SICombatActionStage.Combat;
        }
    }

    public static class SIRoutineManager
    {
        static readonly SICombatRoutine[] Routines = { new WarriorRoutine(), new PaladinRoutine(), new HunterRoutine(),
            new RogueRoutine(), new PriestRoutine(), new DeathKnightRoutine(), new ShamanRoutine(), new MageRoutine(),
            new WarlockRoutine(), new DruidRoutine() };
        static readonly SICombatRoutine Default = new MeleeRoutine();
        public static SICombatRoutine For(Class cls) => Routines.FirstOrDefault(routine => routine.Class == cls) ?? Default;
        sealed class MeleeRoutine : SICombatRoutine
        {
            public override Class Class => Class.None;
            public override IReadOnlyList<SICombatRule> Rules => Array.Empty<SICombatRule>();
        }
    }
}
