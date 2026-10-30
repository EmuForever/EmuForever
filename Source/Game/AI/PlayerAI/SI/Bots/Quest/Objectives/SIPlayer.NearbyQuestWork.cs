using Framework.Constants;
using Game.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    // Works every accepted quest in the area at once. The selected task leads the bot to a database spawn area;
    // once there, any visible creature or object that advances an unfinished objective of any accepted quest is
    // fair game, nearest first. When nothing nearby is left, the normal planner picks the next database area.
    public sealed partial class SIPlayer
    {
        long _nextNearbyObjectiveScan;
        // Yards of preference for the current task's own targets, so two quests' mobs do not swap ownership every scan.
        const float CurrentTaskPreference = 20;
        readonly record struct NearbyQuestWork(SITask Task, WorldObject Target, bool Gathering, float Cost);

        // A lightweight scan of the visible snapshot and the quest log. It never interrupts an owned encounter,
        // NPC visit or object approach, and stays out of the way while the bot is still collecting pickups.
        void SelectNearbyQuestWork()
        {
            if (me.IsInCombat() || _encounter.Active || _questPickupAnchor != null && !_workingQuestBatch ||
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
            string current = _task?.Kind == SITaskKind.Objective ? _task.Key : null;
            var choices = new List<NearbyQuestWork>();
            foreach (var (slot, quest) in QuestLog())
            {
                if (CollectsCloth(quest) || me.GetQuestStatus(quest.Id) != QuestStatus.Incomplete || !QuestLevelEligible(quest)) continue;
                foreach (var objective in Knowledge.Plan(quest).Objectives)
                {
                    if (!objective.Supported || me.IsQuestObjectiveComplete(slot, quest, objective.Objective)) continue;
                    var task = new SITask { Kind = SITaskKind.Objective, Quest = quest, Objective = objective };
                    if (Blacklisted(task.Key) || !QuestDestinationAllowed(task)) continue;
                    float bias = task.Key == current ? -CurrentTaskPreference : 0;
                    if (objective.Sources.Any(source => source is SIObjectiveSource.UseObject or SIObjectiveSource.ObjectLoot))
                        foreach (var obj in objects.Where(obj => objective.Objects.Contains(obj.GetEntry()) &&
                            ObjectiveRetryReady(quest.Id, objective.Objective.Id, obj.GetGUID())))
                            choices.Add(new(task, obj, true, me.GetExactDist(obj) + bias));
                    if (objective.Sources.Any(source => source is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop))
                        foreach (var creature in creatures.Where(creature => objective.Creatures.Contains(creature.GetEntry()) &&
                            ObjectiveRetryReady(quest.Id, objective.Objective.Id, creature.GetGUID())))
                            choices.Add(new(task, creature, false, me.GetExactDist(creature) + bias));
                }
            }
            foreach (var choice in choices.OrderBy(choice => choice.Cost).ThenBy(choice => QuestAcceptanceOrder(choice.Task.Quest)))
            {
                if (!choice.Gathering && !SafeToInitiate((Creature)choice.Target, out _, questTarget: true)) continue;
                SITask selected = choice.Task.Key == current && _task.Destination == null ? _task :
                    ObjectiveTask(choice.Task.Quest, choice.Task.Objective, choice.Target);
                if (selected == null) continue;
                if (!ReferenceEquals(selected, _task))
                {
                    // Switching between accepted quests in one area is the plan, not an engaged task being lost.
                    if (_task != null) _task.Engaged = false;
                    SetTask(selected);
                }
                // Keep this exact object until its approach/transaction finishes. Nearby scans
                // must not switch back to a mob while the character is walking to the object.
                if (choice.Gathering) selected.Target = choice.Target.GetGUID();
                return;
            }
        }
    }
}
