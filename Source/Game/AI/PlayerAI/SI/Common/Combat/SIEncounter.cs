using Game.Entities;
using System;

namespace Game.AI
{
    public enum SIEncounterStage { None, Approach, Pull, Combat, Loot, Complete, Failed }

    public readonly record struct SIEncounterView(bool Exists, bool Alive, bool Attackable, bool Engaged,
        bool InRange, bool InSight, bool Lootable, float Health, long Damage);

    public interface ISIEncounterActions
    {
        SIEncounterView Observe(ObjectGuid target, bool melee);
        SIBehaviorResult Approach(ObjectGuid target, bool melee);
        void StopTravel();
        SIBehaviorResult Attack(ObjectGuid target, bool melee);
        SIBehaviorResult Loot(ObjectGuid target);
    }

    // One persistent Kill POI. Approaching a mob does not finish a quest objective. The same target
    // survives AI updates until it dies and is looted, becomes invalid, or times out.
    // The 45-second approach budget follows ActionMoveToTarget in the reference Bots/Grind tree.
    public sealed class SIEncounter
    {
        public ObjectGuid Target { get; private set; }
        public SIEncounterStage Stage { get; private set; }
        public string Failure { get; private set; }
        public bool ForceMelee { get; private set; }
        long _approachAt, _lastEffectAt, _pullAt;
        float _health;
        long _damage;
        bool _observed;
        bool _pullStarted;
        long? _pausedAt;

        public bool Active => !Target.IsEmpty() && Stage is not (SIEncounterStage.Complete or SIEncounterStage.Failed);
        public void Start(ObjectGuid target, long now)
        {
            Target = target; Stage = SIEncounterStage.Approach; Failure = null; ForceMelee = false;
            _approachAt = _lastEffectAt = _pullAt = now; _observed = _pullStarted = false;
            _pausedAt = null;
        }
        public void Clear() { Target = ObjectGuid.Empty; Stage = SIEncounterStage.None; Failure = null; _pausedAt = null; }
        public void Pause(long now) => _pausedAt ??= now;
        void Resume(long now)
        {
            if (_pausedAt is not long paused) return;
            long elapsed = Math.Max(0, now - paused);
            _approachAt += elapsed; _lastEffectAt += elapsed; _pullAt += elapsed; _pausedAt = null;
        }
        SIBehaviorResult Fail(string reason) { Failure = reason; Stage = SIEncounterStage.Failed; return SIBehaviorResult.Failure; }

        public SIBehaviorResult Tick(ISIEncounterActions actions, long now)
        {
            if (!Active) return Stage == SIEncounterStage.Complete ? SIBehaviorResult.Success : SIBehaviorResult.Failure;
            Resume(now);
            SIEncounterView view = actions.Observe(Target, ForceMelee);
            if (!view.Exists) return Fail("Target disappeared or left the map/phase.");
            if (!view.Alive)
            {
                if (Stage != SIEncounterStage.Loot) actions.StopTravel();
                if (!view.Lootable) { Stage = SIEncounterStage.Complete; return SIBehaviorResult.Success; }
                Stage = SIEncounterStage.Loot;
                SIBehaviorResult loot = actions.Loot(Target);
                if (loot == SIBehaviorResult.Success) Stage = SIEncounterStage.Complete;
                else if (loot == SIBehaviorResult.Failure) return Fail("Could not reach or loot the corpse.");
                return loot;
            }
            if (!view.Attackable) return Fail("Target is evading, unavailable, or tagged by someone else.");
            if (!_observed || view.Health < _health || view.Damage > _damage)
            { _lastEffectAt = now; _observed = true; }
            _health = view.Health; _damage = view.Damage;

            if (!view.InRange || !view.InSight)
            {
                if (Stage != SIEncounterStage.Approach) _approachAt = now;
                Stage = SIEncounterStage.Approach;
                if (now - _approachAt >= 45000) return Fail("Approach made no contact within 45 seconds.");
                if (actions.Approach(Target, ForceMelee) == SIBehaviorResult.Failure) return Fail("No safe route to target.");
                return SIBehaviorResult.Running;
            }

            // Clear the travel spline before pulling/fighting, even when navigation reports arrival.
            actions.StopTravel();
            if (!_pullStarted) { _pullAt = _lastEffectAt = now; _pullStarted = true; }
            Stage = view.Engaged ? SIEncounterStage.Combat : SIEncounterStage.Pull;
            SIBehaviorResult attack = actions.Attack(Target, ForceMelee);
            if (attack == SIBehaviorResult.Failure) return Fail("Core rejected the attack.");
            if (!ForceMelee && now - _lastEffectAt >= 6000 && now - _pullAt >= 6000)
            { ForceMelee = true; _approachAt = now; }
            if (now - _lastEffectAt >= 30000) return Fail("No damage or target health progress for 30 seconds.");
            return SIBehaviorResult.Running;
        }
    }
}
