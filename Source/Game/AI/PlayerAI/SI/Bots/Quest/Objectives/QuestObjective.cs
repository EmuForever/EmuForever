using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // Server port of the corresponding CopilotBuddy quest behavior; database/range orders replace profiles.
    public sealed partial class SIPlayer
    {
        long _nextObjectiveTargetReport;
        // Kill, loot, use objects, talk or explore: live targets first, otherwise sweep the objective's hotspots.
        void WorkObjective(SITask task)
        {
            string searchReason = null;
            SIObjectivePlan objective = task.Objective;
            int quest = task.Quest.Id, id = objective.Objective.Id;
            bool kills = objective.Sources.Any(source => source is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop);
            bool talk = objective.Sources.Contains(SIObjectiveSource.Talk);

            if (UseQuestItemOnTarget(task)) return;
            if (task.Objects.Length != 0 && !task.Target.IsEmpty() && WorkQuestObject(task)) return;
            if (task.Creatures.Length != 0 && (kills || talk))
            {
                Creature enemy = null;
                Creature approachTarget = null;
                Creature rejected = null;
                string rejectedReason = null;
                float bestDistance = float.MaxValue;
                foreach (Creature creature in NearbyCreatures(_settings.TargetSearchRadius)
                    .Where(creature => task.Creatures.Contains(creature.GetEntry()) && creature.IsAlive())
                    .OrderBy(creature => me.GetAttackers().Contains(creature) ? -1 : me.GetExactDist(creature)))
                {
                    if (!task.Creatures.Contains(creature.GetEntry()) || !creature.IsAlive() || !ObjectiveRetryReady(quest, id, creature.GetGUID()) ||
                        IsLearnedRouteBlocked(creature.GetPosition())) continue;
                    if (talk && !me.IsHostileTo(creature))
                    {
                        if (!me.CanSeeOrDetect(creature, distanceCheck: true)) continue;
                        var move = MoveWithin(creature, 3.5f, task.Label);
                        if (move == SIBehaviorResult.Failure) SkipObjective(quest, id, creature, "No route to quest NPC.", true);
                        if (move == SIBehaviorResult.Success)
                        {
                            int before = TaskProgress(task);
                            me.GetSession().TalkForSI(creature);
                            if (TaskProgress(task) <= before) SkipObjective(quest, id, creature, "Talking produced no objective credit.", false);
                            Status = "Speaking with " + creature.GetName();
                        }
                        return;
                    }
                    if (!kills) continue;
                    if(me.GetExactDist(creature)>90&&me.IsValidAttackTarget(creature)&&me.CanSeeOrDetect(creature,distanceCheck:false)&&
                        !AvoidEnemy(creature)&&TargetRetryReady(creature)&&(!creature.HasLootRecipient()||creature.IsTappedBy(me)))
                    {approachTarget??=creature;continue;}
                    if (!CanFight(creature) || AvoidEnemy(creature) || !TargetRetryReady(creature))
                    {
                        if (rejected == null || me.GetDistance(creature) < me.GetDistance(rejected))
                        {
                            rejected = creature;
                            rejectedReason = $"attackable {me.IsValidAttackTarget(creature)}, visible {me.CanSeeOrDetect(creature, distanceCheck: true)}, " +
                                $"distance {me.GetExactDist(creature):F1}, avoided {AvoidEnemy(creature)}";
                        }
                        continue;
                    }
                    // Never steal another player's tagged creature.
                    if (creature.HasLootRecipient() && !creature.IsTappedBy(me)) continue;
                    bool attacking = me.GetAttackers().Contains(creature);
                    if (!attacking && !ReservationAvailable(creature.GetGUID())) continue;
                    if (!attacking && !SafeToInitiate(creature, out string reason, questTarget: true))
                    {
                        if (rejected == null || me.GetDistance(creature) < me.GetDistance(rejected)) { rejected = creature; rejectedReason = reason; }
                        continue;
                    }
                    float distance = me.GetDistance(creature) + (attacking ? -100 : 0);
                    if (distance < bestDistance && (attacking || ReserveQuestTarget(creature.GetGUID())))
                    { bestDistance = distance; enemy = creature; break; }
                }
                if (enemy != null && (me.GetAttackers().Contains(enemy) || ReserveQuestTarget(enemy.GetGUID())))
                {
                    BeginEncounter(enemy); TickEncounter(); return;
                }
                if(approachTarget!=null)
                {
                    me.SetSelection(approachTarget.GetGUID());
                    MoveWithin(approachTarget,70,"Approaching live quest target: "+approachTarget.GetName());return;
                }
                if (rejected != null) searchReason = $"cannot pull {rejected.GetName()} ({rejectedReason})";
            }

            if (task.Objects.Length != 0 && WorkQuestObject(task)) return;

            if (objective.Sources.Contains(SIObjectiveSource.Explore))
                foreach (AreaTriggerRecord trigger in objective.Triggers.Where(t => t.ContinentID == me.GetMapId()))
                {
                    if (me.IsInAreaTriggerRadius(trigger))
                    {
                        me.GetSession().AreaTriggerForSI(trigger.Id);
                        Status = "Explored the quest area";
                        return;
                    }
                }

            if (searchReason != null)
            {
                Status = task.Label + ": " + searchReason;
                if (Now >= _nextObjectiveTargetReport) { _nextObjectiveTargetReport = Now + 15000; Report("quest_target_blocked", Status); }
            }
            Sweep(task, searchReason);
        }

        bool WorkQuestObject(SITask task)
        {
            int quest = task.Quest.Id, objective = task.Objective.Objective.Id;
            if(!task.Target.IsEmpty())
            {
                var previous=Global.ObjAccessor.GetWorldObject(me,task.Target) as GameObject;
                if(previous==null||!previous.IsSpawned()){task.Target=ObjectGuid.Empty;ReleaseQuestTarget();}
            }
            GameObject obj = NearbyObjects(_settings.TargetSearchRadius)
                .Where(obj => (task.Target.IsEmpty() || obj.GetGUID() == task.Target) && task.Objects.Contains(obj.GetEntry()) &&
                    obj.IsSpawned() && me.CanSeeOrDetect(obj, distanceCheck: true) && _usedObjects.GetValueOrDefault(obj.GetGUID()) <= Now &&
                    ObjectiveRetryReady(quest, objective, obj.GetGUID()) && !IsLearnedRouteBlocked(obj.GetPosition()) && ReservationAvailable(obj.GetGUID(), true) &&
                    (obj.GetGoInfo().type != GameObjectTypes.Chest || obj.ActivateToQuest(me) || obj.GetGoInfo().GetLootId() != 0))
                .OrderBy(me.GetExactDist).FirstOrDefault();
            if (obj == null || !ReserveQuestTarget(obj.GetGUID(), true))
            {
                task.Target = ObjectGuid.Empty; ReleaseQuestTarget(); return false;
            }
            task.Target = obj.GetGUID();
            var move = MoveWithin(obj, 3f, task.Label);
            if (move == SIBehaviorResult.Failure)
            {
                SkipObjective(quest, objective, obj, "No route to quest object.", true);
                task.Target = ObjectGuid.Empty; return true;
            }
            if (move != SIBehaviorResult.Success || me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return true;
            _usedObjects[obj.GetGUID()] = Now + 4000;
            int before = TaskProgress(task);
            me.GetSession().UseObjectForSI(obj);
            me.GetSession().LootOpenViewForSI();
            if (TaskProgress(task) > before) { task.LastProgressAt = Now; NoteGameplayProgress(); }
            else _usedObjects[obj.GetGUID()] = Now + 60000;
            task.Target = ObjectGuid.Empty; ReleaseQuestTarget();
            Status = "Using " + obj.GetName();
            return true;
        }

        // "Use the X on Y" quests: the quest's source item cast on a target creature (alive or its corpse), once per target.
        bool UseQuestItemOnTarget(SITask task)
        {
            if (task.Quest == null || task.Objective == null || task.Quest.SourceItemId == 0 || me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return false;
            Item item = null;
            me.ForEachItem(ItemSearchLocation.Inventory, candidate =>
            {
                if (candidate.GetEntry() != task.Quest.SourceItemId) return true;
                item = candidate;
                return false;
            });
            if (item == null || me.CanUseItem(item) != InventoryResult.Ok) return false;
            int quest = task.Quest.Id, objective = task.Objective.Objective.Id;
            foreach (var effect in item.GetEffects().Where(value => value.TriggerType == ItemSpelltriggerType.OnUse))
            {
                var spell = Global.SpellMgr.GetSpellInfo(effect.SpellID, me.GetMap().GetDifficultyID());
                if (spell == null || !me.GetSpellHistory().IsReady(spell, item.GetEntry())) continue;
                var mask = spell.GetExplicitTargetMask();
                bool unitTarget = spell.NeedsExplicitUnitTarget();
                bool objectTarget = mask.HasAnyFlag(SpellCastTargetFlags.GameobjectMask);
                float range = Math.Max(1, spell.GetMaxRange());
                IEnumerable<WorldObject> candidates = objectTarget
                    ? NearbyObjects(_settings.TargetSearchRadius).Where(obj => task.Objects.Contains(obj.GetEntry()) && obj.IsSpawned())
                    : NearbyCreatures(_settings.TargetSearchRadius).Where(c => task.Creatures.Contains(c.GetEntry()) &&
                        (!me.IsInCombat() || c.GetGUID() == _encounter.Target) &&
                        (c.IsAlive() || c.IsTappedBy(me) || !c.HasLootRecipient())).Cast<WorldObject>();
                WorldObject target = candidates.Where(c => me.CanSeeOrDetect(c, distanceCheck: true) &&
                    _objectiveRetryAt.GetValueOrDefault((quest, -objective, c.GetGUID())) <= Now && !IsLearnedRouteBlocked(c.GetPosition()) &&
                    (!(unitTarget || objectTarget) || spell.CheckTarget(me, c, false) == SpellCastResult.SpellCastOk))
                    .OrderBy(me.GetExactDist).FirstOrDefault();
                if (target == null)
                {
                    if (unitTarget || objectTarget || me.IsInCombat() || task.Hotspots.Count == 0 ||
                        task.Hotspots.Min(point => Vector3.Distance(V(me.GetPosition()), point)) > 8) continue;
                    target = me;
                    if (_objectiveRetryAt.GetValueOrDefault((quest, -objective, target.GetGUID())) > Now) continue;
                }
                if (target != me)
                {
                    var approach = MoveWithin(target, unitTarget || objectTarget ? Math.Max(1, range - 1) : 3,
                        "Using " + item.GetTemplate().GetName());
                    if (approach == SIBehaviorResult.Failure) SkipObjective(quest, -objective, target, "No route to quest item target.", true);
                    if (approach != SIBehaviorResult.Success) return true;
                }
                WorldObject castTarget = unitTarget || objectTarget || mask.HasAnyFlag(SpellCastTargetFlags.DestLocation) ? target : me;
                if (!me.GetSession().UseItemForSI(item, spell.Id, castTarget)) continue;
                me.AttackStop();
                // Acceptance queues a normal item cast; credit is checked by the quest
                // planner. Do not blacklist the target for two minutes before it executes.
                _objectiveRetryAt[(quest, -objective, target.GetGUID())] = Now + 4000;
                Report("quest_item_requested", $"Using {item.GetTemplate().GetName()} for {task.Quest.LogTitle} near {target.GetName()}");
                return true;
            }
            return false;
        }

        bool ObjectiveRetryReady(int quest, int objective, ObjectGuid target) =>
            _objectiveRetryAt.GetValueOrDefault((quest, objective, target)) <= Now;

        void SkipObjective(int quest, int objective, WorldObject target, string reason, bool blocked)
        {
            if (_objectiveRetryAt.Count >= 512)
                foreach (var key in _objectiveRetryAt.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                    _objectiveRetryAt.Remove(key);
            _objectiveRetryAt[(quest, objective, target.GetGUID())] = Now + (blocked ? SIDestinationBlocks.Duration : 30000);
            if (blocked) RememberBlockedRoute(target.GetPosition(), reason);
            Report("quest_task_failed", reason);
            ReleaseQuestTarget();
        }

    }
}

