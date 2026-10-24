using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        readonly SIEncounter _encounter = new();
        bool _clearingTravelApproach;
        readonly Dictionary<ObjectGuid, long> _unreachableTargets = new();
        List<Creature> _visibleCreatures = new();
        List<GameObject> _visibleObjects = new();
        ObjectGuid _combatTarget => _encounter.Target;
        long _nextWorldSnapshot;
        int _snapshotMap = -1;
        System.Numerics.Vector3 _snapshotPosition;

        void RefreshWorldSnapshot()
        {
            if (_snapshotMap == me.GetMapId() && Now < _nextWorldSnapshot &&
                System.Numerics.Vector3.DistanceSquared(_snapshotPosition, Here) < 100) return;
            _nextWorldSnapshot = Now + 1000;
            _snapshotMap = me.GetMapId(); _snapshotPosition = Here;
            // The snapshot covers the target search radius; the occasional wider lookups (quest givers,
            // vendors up to SI.QuestPickupRadius) query the shared index directly instead.
            _snapshotRange = _settings.TargetSearchRadius;
            // Fresh lists: callers may still enumerate the previous snapshot.
            _visibleCreatures = new(_visibleCreatures.Count);
            _visibleObjects = new(_visibleObjects.Count);
            SIMapObjectIndex.For(me.GetMap()).Collect(me, _snapshotRange, _visibleCreatures, _visibleObjects);
            foreach (ObjectGuid guid in _unreachableTargets.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                _unreachableTargets.Remove(guid);
        }
        float _snapshotRange;
        IEnumerable<Creature> NearbyCreatures(float range) => (range <= _snapshotRange ? _visibleCreatures : Wide(range).Creatures).Where(creature => creature.IsInWorld &&
            creature.GetMap() == me.GetMap() && me.GetExactDist2d(creature) <= range);
        IEnumerable<GameObject> NearbyObjects(float range) => (range <= _snapshotRange ? _visibleObjects : Wide(range).Objects).Where(obj => obj.IsInWorld &&
            obj.GetMap() == me.GetMap() && me.GetExactDist2d(obj) <= range);
        // A wide query is reused within the same decision tick.
        (long At, float Range, List<Creature> Creatures, List<GameObject> Objects) _wide;
        (long At, float Range, List<Creature> Creatures, List<GameObject> Objects) Wide(float range)
        {
            if (_wide.Creatures != null && _wide.At == Now && _wide.Range >= range) return _wide;
            var creatures = new List<Creature>(); var objects = new List<GameObject>();
            if (me.IsInWorld) SIMapObjectIndex.For(me.GetMap()).Collect(me, range, creatures, objects);
            return _wide = (Now, range, creatures, objects);
        }
        readonly SISkippedTargets _failedCreatures=new();
        bool TargetRetryReady(Unit target) => !_failedCreatures.Contains(target.GetGUID()) && _unreachableTargets.GetValueOrDefault(target.GetGUID()) <= Now &&
            !IsLearnedRouteBlocked(target.GetPosition());

        void BeginEncounter(Unit target)
        {
            if (_encounter.Active && _encounter.Target == target.GetGUID()) return;
            _clearingTravelApproach = false;
            if (_encounter.Active) ReleaseQuestTarget();
            _task?.Suspend(Now);
            CancelTravel();
            me.AttackStop();
            _encounter.Start(target.GetGUID(), Now);
            _routine.Start();
            me.SetSelection(target.GetGUID());
            Report("target_selected", $"Kill POI: {target.GetName()} ({target.GetEntry()})");
        }
        void EndEncounter(bool failed)
        {
            _mageRetreatUntil=0;_mageRetreatPoint=null;
            if (!_encounter.Target.IsEmpty()) me.GetSession().PetFollowForSI(_encounter.Target);
            _petCommandTarget = ObjectGuid.Empty; _nextPetAttackCommand = 0;
            if (failed && !_encounter.Target.IsEmpty())
            {
                _failedCreatures.Add(_encounter.Target);
                _unreachableTargets[_encounter.Target] = Now + SIDestinationBlocks.Duration;
                Unit target = Resolve(_encounter.Target);
                if (target != null) RememberBlockedRoute(target.GetPosition(), "Unreachable combat target; skipping for five minutes.");
            }
            _encounter.Clear();
            me.InterruptSpell(CurrentSpellTypes.AutoRepeat);
            _clearingTravelApproach = false;
            me.AttackStop();
            ReleaseQuestTarget();
        }
        SIBehaviorResult TickEncounter()
        {
            if (TickMagePolymorph(Resolve(_encounter.Target)))
            { _encounter.Pause(Now); return SIBehaviorResult.Running; }
            if(TickMageRetreat(Resolve(_encounter.Target)))
            {_encounter.Pause(Now);Status="Frost Nova: gaining casting distance";return SIBehaviorResult.Running;}
            if (!_clearingTravelApproach && _task?.Kind == SITaskKind.Objective && UseQuestItemOnTarget(_task)) return SIBehaviorResult.Running;
            if (me.IsInCombat()) _routine.EngagedCombat();
            else if (!_routine.Prepared && Resolve(_encounter.Target)?.IsAlive() == true)
            {
                _encounter.Pause(Now);
                if (_routine.Prepare(stage => TickRoutinePhase(stage, Resolve(_encounter.Target)), Now) != SIBehaviorResult.Success)
                { Status = "Preparing to pull " + Resolve(_encounter.Target)?.GetName(); return SIBehaviorResult.Running; }
            }
            SIBehaviorResult result = _encounter.Tick(new EncounterActions(this), Now);
            Status = $"{_encounter.Stage}: {Resolve(_encounter.Target)?.GetName() ?? "target"}";
            if (result != SIBehaviorResult.Running)
            {
                if (result == SIBehaviorResult.Failure) Report("combat_failed", _encounter.Failure);
                CancelTravel();
                EndEncounter(result == SIBehaviorResult.Failure);
            }
            // A failed encounter is handled here; it must not fall through to another movement owner this tick.
            return SIBehaviorResult.Running;
        }

        sealed class EncounterActions(SIPlayer owner) : ISIEncounterActions
        {
            Unit Target(ObjectGuid guid) => owner.Resolve(guid);
            public SIEncounterView Observe(ObjectGuid guid, bool melee)
            {
                Unit target = Target(guid);
                if (target == null) return default;
                bool engaged = owner.me.IsInCombatWith(target) || owner.me.GetAttackers().Contains(target);
                bool attackable = owner.CanFight(target) && (engaged || target is not Creature creature ||
                    !creature.HasLootRecipient() || creature.IsTappedBy(owner.me));
                float range = owner.CombatApproachRange(target, melee);
                return new(true, target.IsAlive(), attackable, engaged, owner.me.GetExactDist(target) <= range,
                    owner.me.IsWithinLOSInMap(target), target is Creature corpse && owner.me.IsAllowedToLoot(corpse),
                    target.GetHealthPct(), owner.DamageDelivered);
            }
            public SIBehaviorResult Approach(ObjectGuid guid, bool melee)
            {
                Unit target = Target(guid);
                return target == null ? SIBehaviorResult.Failure : owner.Travel(target.GetPosition(),
                    "Approaching " + target.GetName(), owner.me.IsWithinLOSInMap(target) ? owner.CombatApproachRange(target, melee) : .5f,destinationTolerance:1);
            }
            public void StopTravel() => owner.CancelTravel();
            public SIBehaviorResult Attack(ObjectGuid guid, bool melee)
            {
                Unit target = Target(guid);
                return target == null ? SIBehaviorResult.Failure : owner.AttackEncounterTarget(target);
            }
            public SIBehaviorResult Loot(ObjectGuid guid) => Target(guid) is Creature corpse ? owner.LootCorpse(corpse) : SIBehaviorResult.Success;
        }
    }
}

