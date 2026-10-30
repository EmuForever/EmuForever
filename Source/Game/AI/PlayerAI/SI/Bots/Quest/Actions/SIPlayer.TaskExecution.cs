using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // Executing the selected task: quest objectives, NPC visits (quest givers, trainers, merchants) and rewards.
    public sealed partial class SIPlayer
    {
        void Quest()
        {
            if (_task?.Kind == SITaskKind.Grind)
            {
                if (!QuestNeedsLevelGrinding()) { SetTask(null); _nextReplan = 0; }
                else if (Now < _nextReplan) { Grind(_task); return; }
            }
            if (!SIQuestKnowledge.IsBuilt) Status = "Loading quest knowledge";
            // An empty result must obey its retry deadline too; otherwise every idle bot
            // rebuilds candidates on all four decision ticks each second.
            if (Now >= _nextReplan || _task != null && TaskDone(_task)) Replan();
            if (_task == null)
            {
                if (!QuestNeedsLevelGrinding()) { Status = QuestWaitStatus(); return; }
                SITask grind = GrindTask();
                if (grind == null) { Status = "Questing: " + _questSearchSummary; return; }
                SetTask(grind);
            }

            int progress = TaskProgress(_task);
            if (progress > _task.ProgressMark) { _task.ProgressMark = progress; _task.LastProgressAt = Now; NoteGameplayProgress(); }
            // Commuting toward a distant destination or work area counts as progress; sweeping inside it does not.
            float remaining = TaskDistance(_task);
            if (remaining > 60 && remaining < _task.ClosestApproach - 20) { _task.ClosestApproach = remaining; _task.LastProgressAt = Now; }
            long budget = _task.Kind is SITaskKind.Objective or SITaskKind.Grind ? 8 * 60000 : 4 * 60000;
            if (_task.Kind != SITaskKind.Grind && Now - _task.LastProgressAt > budget)
            {
                Blacklist(_task, 20 * 60000, "No progress for too long.");
                SetTask(null); _nextReplan = 0; return;
            }
            switch (_task.Kind)
            {
                case SITaskKind.PickUp:
                case SITaskKind.TurnIn:
                case SITaskKind.Train:
                case SITaskKind.Vendor:
                    VisitNpc(_task); break;
                case SITaskKind.Objective:
                    if (_task.Objective.Sources.Contains(SIObjectiveSource.Vendor) && _task.Destination != null && _task.Hotspots.Count == 0) VisitNpc(_task);
                    else WorkObjective(_task);
                    break;
                case SITaskKind.Grind:
                    Grind(_task); break;
            }
        }

        float TaskDistance(SITask task)
        {
            if (task.Destination is SISpawn spawn) return Distance(spawn.Position);
            return task.Hotspots.Count == 0 ? 0 : task.Hotspots.Min(Distance);
        }

        // Travel to a known spawn, find the live NPC/object there and interact.
        SIBehaviorResult VisitNpc(SITask task)
        {
            if (task.Destination is not SISpawn spawn) return FailInteractionTask(task, "NPC destination is missing.");
            WorldObject target = task.Target.IsEmpty() ? null : Global.ObjAccessor.GetWorldObject(me, task.Target);
            bool pinnedService=SIServiceSelection.OwnsTrip(task.Kind);
            // Service travel owns one selected spawn until it succeeds or fails.
            // Quest relations may resolve another suitable live giver nearby.
            if(target!=null&&(!target.IsInWorld||target is Creature dead&&!dead.IsAlive()||target is GameObject gone&&!gone.IsSpawned()))
            {target=null;task.Target=ObjectGuid.Empty;task.ApproachStartedAt=null;}
            if(target==null)
                target=FindLive(task.Creatures,task.Objects,_settings.TargetSearchRadius,live=>
                    pinnedService?(live is Creature chosen&&(spawn.SpawnId!=0?chosen.GetSpawnId()==spawn.SpawnId:
                        Vector3.Distance(V(live.GetPosition()),spawn.Position)<=3)):
                    task.Kind==SITaskKind.PickUp?OffersQuest(live,task.Quest.Id):
                    task.Kind==SITaskKind.TurnIn?QuestGiverHasWork(live,true):true);
            if (target == null)
            {
                // Once the spawn has been checked, retain the patrol cursor rather
                // than returning to the empty home coordinate on every decision.
                if (task.PatrolSearch != null)
                    return SearchQuestGiverPatrol(task, spawn) ?? FailInteractionTask(task, "Quest giver patrol is no longer on this map.");
                if (Distance(spawn.Position) > 8 && task.Target.IsEmpty())
                {
                    SIBehaviorResult move = Travel(P(spawn.Position), task.Label,rememberFailure:false);
                    if (move != SIBehaviorResult.Failure) return SIBehaviorResult.Running;
                    // A travelling giver may have left an inaccessible home point;
                    // its real patrol is still worth searching before deferring it.
                    return SearchQuestGiverPatrol(task, spawn) ?? FailInteractionTask(task, "No path to NPC spawn.");
                }
                SIBehaviorResult? patrol = SearchQuestGiverPatrol(task, spawn);
                if (patrol.HasValue) return patrol.Value;
                // Cross-check the shared object index against the core's own grid search before writing
                // the spawn off; a spawn the core cannot see either is treated as gone for much longer.
                var gridFound=task.Creatures.SelectMany(entry=>me.GetCreatureListWithEntryInGrid(entry,_settings.TargetSearchRadius)).Where(c=>c.IsAlive()).Cast<WorldObject>()
                    .Concat(task.Objects.SelectMany(entry=>me.GetGameObjectListWithEntryInGrid(entry,_settings.TargetSearchRadius)).Where(o=>o.IsSpawned())).ToList();
                string detail=gridFound.FirstOrDefault() is WorldObject seen?
                    $"; first at {me.GetDistance(seen):F0} yd, in snapshot {_visibleCreatures.Contains(seen as Creature)||_visibleObjects.Contains(seen as GameObject)}, "+
                    $"sees bot {seen.InSamePhase(me)}, bot sees it {me.InSamePhase(seen)}, detectable {me.CanSeeOrDetect(seen,distanceCheck:false)}, hostile {me.IsHostileTo(seen)}":"";
                bool present=gridFound.Any(found=>_visibleCreatures.Contains(found as Creature)||_visibleObjects.Contains(found as GameObject));
                if(present)
                    // The NPC is right here but does not offer (or accept) this quest for this character.
                    return FailInteractionTask(task, $"NPC (entry {spawn.Entry}) is present but has no {(task.Kind==SITaskKind.TurnIn?"turn-in":task.Kind==SITaskKind.PickUp?"pickup":"service")} for this character{detail}.");
                return FailInteractionTask(task, $"NPC disappeared, died, or is unavailable in this phase (entry {spawn.Entry}, spawn {spawn.SpawnId}; grid search finds {gridFound.Count}{detail}).",
                    spawnMissing:true,missingFor:gridFound.Count==0?600000:30000);
            }
            if (task.PatrolSearch != null)
            {
                // Live discovery wins over the database hint immediately. The
                // ordinary approach/quest handler still owns range and eligibility.
                task.PatrolSearch = null; task.PatrolChecked = false;
                task.ApproachStartedAt = null; task.ClosestApproach = float.MaxValue;
            }
            task.Target = target.GetGUID();
            task.ApproachStartedAt ??= Now;
            float remaining = me.GetExactDist(target);
            if (remaining < task.ClosestApproach - 2)
            { task.ClosestApproach = remaining; task.ApproachStartedAt = Now; }
            if (target is Creature npc && !npc.IsAlive()) return FailInteractionTask(task, "NPC is dead.",spawnMissing:true);
            SIBehaviorResult approach = MoveWithin(target, 3.5f, task.Label, npcInteraction: target is Creature);
            if (SIInteractionApproach.TimedOut(Now - task.ApproachStartedAt.Value, approach == SIBehaviorResult.Success))
                return FailInteractionTask(task, "NPC approach timed out, including an unusable interaction at close range.");
            if (approach == SIBehaviorResult.Failure) return FailInteractionTask(task, "No safe interaction approach.");
            if (approach != SIBehaviorResult.Success) return SIBehaviorResult.Running;
            if(task.Kind==SITaskKind.Vendor&&_vendorRetryAt.GetValueOrDefault(target.GetGUID())>Now)
                return FailInteractionTask(task,"Merchant interaction is unavailable; deferring this vendor instead of waiting at the NPC.");
            SIBehaviorResult result = task.Interaction.Tick(() =>
            {
                bool success;
                switch (task.Kind)
                {
                    case SITaskKind.PickUp: success = AcceptQuest(target, task.Quest); break;
                    case SITaskKind.TurnIn: success = TurnInQuest(target, task.Quest); break;
                    case SITaskKind.Train: success = TrainAt(target as Creature); break;
                    case SITaskKind.ProfessionTrain: success = ProfessionTrainAt(target as Creature); break;
                    case SITaskKind.Vendor:
                        if (task.VendorPurpose == SIVendorPurpose.Auction) return AuctionAt(target as Creature, task);
                        success = VendorAt(target as Creature, task.VendorPurpose); break;
                    case SITaskKind.Objective: success = BuyObjectiveItem(target as Creature, task); break;
                    default: success = false; break;
                }
                return success ? SIBehaviorResult.Success : SIBehaviorResult.Failure;
            }, Now);
            if (result == SIBehaviorResult.Failure) return FailInteractionTask(task, "NPC action failed after bounded retries.");
            if (result == SIBehaviorResult.Success)
            {
                task.InteractionCompleted = true; task.LastProgressAt = Now; _nextReplan = 0; _interactionFailures.Remove(task.Key);
                if (task.Kind == SITaskKind.Objective && !TaskDone(task)) task.Interaction.Reset();
            }
            return result;
        }

        readonly Dictionary<string, int> _interactionFailures = new();
        SIBehaviorResult FailInteractionTask(SITask task, string reason, bool spawnMissing=false, long missingFor=30000)
        {
            // A rejected approach is not proof the NPC is absent. Hiding its
            // spawn for every quest/trainer/vendor role stranded ready turn-ins.
            // Repeated failures of the same task back off: 30 s, 2 min, 8 min, then 15 min.
            int failures = _interactionFailures.GetValueOrDefault(task.Key) + 1;
            if (_interactionFailures.Count > 4096) _interactionFailures.Clear();
            _interactionFailures[task.Key] = failures;
            long retry = Math.Min(15 * 60000L, 30000L << Math.Min(8, 2 * (failures - 1)));
            if (spawnMissing&&task.Destination is SISpawn spawn) MarkMissing(spawn, missingFor);
            _taskBlacklist[task.Key] = Now + retry;
            Report("interaction_failed", reason);
            Status = reason;
            CancelTravel(); SetTask(null); _nextReplan = 0;
            return SIBehaviorResult.Failure;
        }

        static bool OffersQuest(WorldObject giver, int quest) => QuestRelations(giver).HasQuest(quest);

        WorldObject FindLive(int[] creatures, int[] objects, float range, Func<WorldObject, bool> filter = null)
        {
            WorldObject best = null;
            float bestDistance = float.MaxValue;
            if (creatures.Length != 0)
                foreach (Creature creature in NearbyCreatures(range))
                {
                    if (!creatures.Contains(creature.GetEntry()) || !creature.IsAlive() || !me.CanSeeOrDetect(creature, distanceCheck: false) ||
                        me.IsHostileTo(creature) || filter != null && !filter(creature)) continue;
                    float distance = me.GetDistance(creature);
                    if (distance < bestDistance) { bestDistance = distance; best = creature; }
                }
            if (objects.Length != 0)
                foreach (GameObject obj in NearbyObjects(range))
                {
                    if (!objects.Contains(obj.GetEntry()) || !obj.IsSpawned() || !me.CanSeeOrDetect(obj, distanceCheck: false) || filter != null && !filter(obj)) continue;
                    float distance = me.GetDistance(obj);
                    if (distance < bestDistance) { bestDistance = distance; best = obj; }
                }
            return best;
        }

        // ForcedQuestPickUp/ForcedQuestTurnIn own one quest ID. Another quest changing at
        // the same NPC cannot complete this action or latch its interaction as successful.
        bool AcceptQuest(WorldObject giver, Quest quest)
        {
            if (me.GetExactDist(giver)>QuestPickupRadius || me.GetZoneId()!=0&&QuestPointZone(V(giver.GetPosition()))!=me.GetZoneId() || !MayPickUpLocalQuest(quest) ||
                !MayRetryQuestInteraction(quest.Id, giver.GetGUID())) return false;
            if (!me.GetSession().AcceptQuestForSI(giver, quest, out string reason))
            {
                Status = reason; FailQuestInteraction(quest.Id, giver, reason); return false;
            }
            RecordQuestAcceptance(quest);
            _nextNearbyQuestScan=0;_nextFrontierQuestSearch=0;_nextReplan=0;
            _questPickupAnchor=new Position(me.GetPosition());_workingQuestBatch=false;
            ++QuestsAccepted; NoteGameplayProgress();
            Status = "Accepted " + quest.LogTitle;
            Report("quest_accepted", quest.LogTitle);
            return true;
        }

        bool TurnInQuest(WorldObject giver, Quest quest)
        {
            if (quest == null || me.GetQuestStatus(quest.Id) != QuestStatus.Complete ||
                !MayRetryQuestInteraction(quest.Id, giver.GetGUID())) return false;
            var (type, reward) = ChooseReward(quest);
            if (!me.GetSession().RewardQuestForSI(giver, quest, type, reward, out string reason))
            {
                Status = reason; FailQuestInteraction(quest.Id, giver, reason); return false;
            }
            ++QuestsCompleted; _questFailures.Remove(quest.Id);
            NoteGameplayProgress(); EquipUpgrades();
            Status = "Turned in " + quest.LogTitle;
            Report("quest_complete", quest.LogTitle);
            return true;
        }

        // HB ActionSelectReward: the most useful item this character can use, otherwise the most valuable.
        (LootItemType Type, int Item) ChooseReward(Quest quest)
        {
            if (quest.GetRewChoiceItemsCount() == 0) return (LootItemType.Item, 0);
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < quest.RewardChoiceItemId.Length; ++i)
            {
                int item = quest.RewardChoiceItemId[i];
                ItemTemplate template = item != 0 ? Global.ObjectMgr.GetItemTemplate(item) : null;
                if (template == null) continue;
                float score = template.GetSellPrice() / 100f;
                if (me.CanUseItem(template) == InventoryResult.Ok && template.GetInventoryType() != InventoryType.NonEquip)
                    score += 1000 + ItemScore(template) - EquippedScore(template);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best < 0 ? (LootItemType.Item, 0) : (quest.RewardChoiceItemType[best], quest.RewardChoiceItemId[best]);
        }

    }
}

