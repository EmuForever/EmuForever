using System;

namespace Game.AI
{
    public enum SIPreparationStage { Equipment, Recovery, Supplies, Buffs, Ready }

    // Retained preparation order, analogous to the routine's Rest/PreCombatBuff behaviors.
    // Only Running keeps the same step. Combat pauses its budget instead of restarting it.
    public sealed class SIPreparation
    {
        public SIPreparationStage Stage { get; private set; }
        public bool Complete => Stage == SIPreparationStage.Ready;
        long? _stepAt, _pausedAt;
        public void Reset() { Stage = SIPreparationStage.Equipment; _stepAt = _pausedAt = null; }
        public void Pause(long now) { _pausedAt ??= now; }
        public void Resume(long now)
        {
            if (_pausedAt is long paused && _stepAt.HasValue) _stepAt += Math.Max(0, now - paused);
            _pausedAt = null;
        }
        public SIBehaviorResult Tick(Func<SIPreparationStage, SIBehaviorResult> action, long now)
        {
            if (Complete) return SIBehaviorResult.Success;
            if (_pausedAt.HasValue) return SIBehaviorResult.Running;
            _stepAt ??= now;
            SIBehaviorResult result = action(Stage);
            // Missing reagents, unavailable supplies or an uncastable optional buff cannot stall login.
            bool expired = Stage is SIPreparationStage.Supplies or SIPreparationStage.Buffs && now - _stepAt.Value >= 30000;
            if (result != SIBehaviorResult.Running || expired) { Stage++; _stepAt = null; }
            return Complete ? SIBehaviorResult.Success : SIBehaviorResult.Running;
        }
    }
}
