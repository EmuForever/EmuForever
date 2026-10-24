using System;

namespace Game.AI
{
    // LevelBot: rest/precombat buffs before pull, heal/combat buffs/combat while engaged.
    // Preparation is retained per Kill POI; reaching a target never restarts it.
    public sealed class SIRoutineExecution
    {
        static readonly SICombatActionStage[] Preparation = { SICombatActionStage.Rest, SICombatActionStage.PreCombatBuff, SICombatActionStage.PullBuff };
        static readonly SICombatActionStage[] Engaged = { SICombatActionStage.Reaction, SICombatActionStage.Heal, SICombatActionStage.CombatBuff, SICombatActionStage.Combat };
        static readonly SICombatActionStage[] Pulling = { SICombatActionStage.Pull, SICombatActionStage.Combat };
        int _preparation;
        long? _stepAt;
        public bool Prepared => _preparation >= Preparation.Length;
        public void Start() { _preparation = 0; _stepAt = null; }
        public SIBehaviorResult Prepare(Func<SICombatActionStage, SIBehaviorResult> action, long now)
        {
            if (_preparation >= Preparation.Length) return SIBehaviorResult.Success;
            _stepAt ??= now;
            SICombatActionStage stage = Preparation[_preparation];
            SIBehaviorResult result = action(stage);
            if (result == SIBehaviorResult.Failure || stage != SICombatActionStage.Rest && now - _stepAt.Value >= 30000)
            { ++_preparation; _stepAt = null; }
            return _preparation >= Preparation.Length ? SIBehaviorResult.Success : SIBehaviorResult.Running;
        }
        public void EngagedCombat() { _preparation = Preparation.Length; _stepAt = null; }
        public static SIBehaviorResult Fight(bool engaged, Func<SICombatActionStage, SIBehaviorResult> action)
        {
            foreach (SICombatActionStage stage in engaged ? Engaged : Pulling)
            {
                SIBehaviorResult result = action(stage);
                if (result != SIBehaviorResult.Failure) return result;
            }
            return SIBehaviorResult.Failure;
        }
    }
}
