using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        Position _questWorkArea;
        bool _returningQuestBatch;
        long _nextNearbyObjectiveScan;
        Position _questAreaOrigin;
        int _questAreaMap = -1;
        readonly record struct NearbyQuestWork(SITask Task, WorldObject Target, bool Gathering, float Distance);

        void ResetQuestWorkArea()
        {
            _questWorkArea = null; _returningQuestBatch = false; _nextNearbyObjectiveScan = 0;
        }

        void ResetQuestingArea()
        {
            ResetQuestWorkArea(); _questAreaOrigin = null; _questAreaMap = -1;
            _questPickupAnchor=null;_workingQuestBatch=false;_nextNearbyQuestScan=0;
        }

        void EnsureQuestingArea()
        {
            if (_questAreaOrigin != null && _questAreaMap == me.GetMapId()) return;
            _questAreaMap = me.GetMapId();
            _questAreaOrigin = new Position(_questPickupAnchor ?? me.GetPosition());
        }

        bool InQuestingArea(Position point) => _questAreaOrigin == null ||
            Vector3.Distance(V(_questAreaOrigin), V(point)) <= _settings.QuestAreaRadius;

        void ReviewQuestReturnBatch()
        {
            if (_returningQuestBatch && !QuestLog().Any(pair => me.GetQuestStatus(pair.Quest.Id) == QuestStatus.Complete && QuestLevelEligible(pair.Quest)))
            {
                ResetQuestWorkArea();
                _workingQuestBatch = false; _questPickupAnchor = new Position(me.GetPosition());
            }
        }

        bool InQuestWorkArea(SITask task)
        {
            EnsureQuestingArea();
            return task.Destination is SISpawn spawn ? InQuestingArea(P(spawn.Position)) :
                task.Hotspots.Any(point => InQuestingArea(P(point)));
        }

        void RememberQuestWorkArea(SITask task, Position target = null)
        {
            if (task.Kind != SITaskKind.Objective || _returningQuestBatch) return;
            if (_questWorkArea != null && InQuestWorkArea(task)) return;
            _questWorkArea = target != null ? new Position(target) : task.Hotspots.Count != 0
                ? P(task.Hotspots.OrderBy(Distance).First()) : new Position(me.GetPosition());
        }

        // A lightweight scan of already visible targets and the accepted quest log. Do not
        // rebuild world spawn/hotspot lists on each tick, or interrupt an owned encounter/NPC action.
        void SelectNearbyQuestWork()
        {
            if (_returningQuestBatch || me.IsInCombat() || _encounter.Active ||
                _questPickupAnchor != null && !_workingQuestBatch ||
                _task != null && !TaskDone(_task) && (_task.Kind != SITaskKind.Objective || !_task.Target.IsEmpty()) ||
                Now < _nextNearbyObjectiveScan) return;
            _nextNearbyObjectiveScan = Now + 1000;
            float radius = _settings.TargetSearchRadius;
            var objects = NearbyObjects(radius).Where(obj => obj.IsSpawned() && me.GetExactDist(obj) <= radius &&
                _usedObjects.GetValueOrDefault(obj.GetGUID()) <= Now && !IsLearnedRouteBlocked(obj.GetPosition()) &&
                me.CanSeeOrDetect(obj, distanceCheck: true) && ReservationAvailable(obj.GetGUID(), true) &&
                (obj.GetGoInfo().type != GameObjectTypes.Chest || obj.ActivateToQuest(me) || obj.GetGoInfo().GetLootId() != 0)).ToArray();
            var creatures = NearbyCreatures(radius).Where(creature => CanFight(creature) && me.GetExactDist(creature) <= radius &&
                !AvoidEnemy(creature) && TargetRetryReady(creature) && (!creature.HasLootRecipient() || creature.IsTappedBy(me)) &&
                ReservationAvailable(creature.GetGUID())).ToArray();
            if (objects.Length == 0 && creatures.Length == 0) return;
            var choices = new List<NearbyQuestWork>();
            foreach (var (slot, quest) in QuestLog())
            {
                if (me.GetQuestStatus(quest.Id) != QuestStatus.Incomplete || !QuestLevelEligible(quest)) continue;
                foreach (var objective in Knowledge.Plan(quest).Objectives)
                {
                    if (!objective.Supported || me.IsQuestObjectiveComplete(slot, quest, objective.Objective)) continue;
                    var task = new SITask { Kind = SITaskKind.Objective, Quest = quest, Objective = objective };
                    if (Blacklisted(task.Key)) continue;
                    if (objective.Sources.Any(source => source is SIObjectiveSource.UseObject or SIObjectiveSource.ObjectLoot))
                        foreach (var obj in objects.Where(obj => objective.Objects.Contains(obj.GetEntry()) &&
                            ObjectiveRetryReady(quest.Id, objective.Objective.Id, obj.GetGUID())))
                            choices.Add(new(task, obj, true, me.GetExactDist(obj)));
                    if (objective.Sources.Any(source => source is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop))
                        foreach (var creature in creatures.Where(creature => objective.Creatures.Contains(creature.GetEntry()) &&
                            ObjectiveRetryReady(quest.Id, objective.Objective.Id, creature.GetGUID())))
                            choices.Add(new(task, creature, false, me.GetExactDist(creature)));
                }
            }
            foreach (var choice in choices.OrderBy(choice => QuestAcceptanceOrder(choice.Task.Quest)).ThenBy(choice => choice.Distance))
            {
                if (!choice.Gathering && !SafeToInitiate((Creature)choice.Target, out _, questTarget: true)) continue;
                SITask selected = _task?.Key == choice.Task.Key && _task.Destination == null ? _task :
                    ObjectiveTask(choice.Task.Quest, choice.Task.Objective, choice.Target);
                if (selected == null) continue;
                if (!ReferenceEquals(selected, _task)) SetTask(selected);
                // Keep this exact object until its approach/transaction finishes. Nearby scans
                // must not switch back to a mob while the character is walking to the apple.
                if (choice.Gathering) selected.Target = choice.Target.GetGUID();
                RememberQuestWorkArea(selected, choice.Target.GetPosition());
                return;
            }
        }
    }
}
