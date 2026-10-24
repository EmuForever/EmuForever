using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // Autonomous questing from the world database, no profiles (CopilotBuddy/Honorbuddy QuestBot shape):
    // pick up level-appropriate quests nearby, work objectives (kill, loot drops, use/loot objects, talk,
    // explore, buy), turn in, train and sell. Unrelated grinding requires an explicit operator order.
    public sealed partial class SIPlayer
    {
        SITask _task;
        long _nextReplan;
        readonly Dictionary<string, long> _taskBlacklist = new();
        readonly Dictionary<int, int> _questFailures = new();
        readonly HashSet<int> _abandonedQuests = new();
        readonly Dictionary<long, long> _missingSpawns = new();
        readonly Dictionary<ObjectGuid, long> _usedObjects = new();
        readonly Dictionary<(int Quest, int Objective, ObjectGuid Target), long> _objectiveRetryAt = new();
        int _activeQuest, _activeObjective;
        long _nextNearbyQuestScan;
        long _nextQuestLevelCheck;
        Position _questPickupAnchor;
        bool _workingQuestBatch;
        string _questSearchSummary = "No eligible nearby quests; waiting to rescan";
        float QuestPickupRadius => _settings.QuestPickupRadius;

        static bool CanCollectQuestBatch(int pendingQuests, bool workingBatch) => pendingQuests == 0 || !workingBatch;
        static bool WithinQuestPickupArea(Position anchor, Position giver, float radius = 60) => anchor != null && anchor.GetExactDist(giver) <= radius;

        bool CollectingQuestBatch()
        {
            int pending = QuestLog().Count(q => me.GetQuestStatus(q.Quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete);
            if (pending == 0 && _workingQuestBatch) { _workingQuestBatch = false; _questPickupAnchor = null; }
            // A saved log is not proof that this area's quest givers were scanned.
            return CanCollectQuestBatch(pending, _workingQuestBatch);
        }

        public string Plan => _task?.Label ?? "none";

        static SIQuestKnowledge Knowledge => SIQuestKnowledge.Instance;

        static Vector3 V(Position p) => new(p.GetPositionX(), p.GetPositionY(), p.GetPositionZ());
        static Position P(Vector3 v) => new(v.X, v.Y, v.Z);
        float Distance(Vector3 point) => Vector2.Distance(new Vector2(me.GetPositionX(), me.GetPositionY()), new Vector2(point.X, point.Y));

        bool Blacklisted(string key) => _taskBlacklist.TryGetValue(key, out long until) && until > Now;
        void Blacklist(SITask task, long milliseconds, string reason)
        {
            _taskBlacklist[task.Key] = Now + milliseconds;
            Report("task_skipped", reason);
            if (task.Quest == null || task.Kind != SITaskKind.Objective) return;
            int failures = _questFailures.GetValueOrDefault(task.Quest.Id) + 1;
            _questFailures[task.Quest.Id] = failures;
            if (failures >= 2) AbandonQuest(task.Quest, reason);
        }

        void AbandonQuest(Quest quest, string reason)
        {
            ushort slot = me.FindQuestSlot(quest.Id);
            if (slot >= SharedConst.MaxQuestLogSize) return;
            me.GetSession().AbandonQuestForSI(slot);
            if (me.FindQuestSlot(quest.Id) < SharedConst.MaxQuestLogSize) return;
            _abandonedQuests.Add(quest.Id);
            _questFailures.Remove(quest.Id);
            if (_task?.Quest?.Id == quest.Id) { SetTask(null); _nextReplan = 0; }
            if (_suspendedTask?.Quest?.Id == quest.Id) _suspendedTask = null;
            if (_trainingReturnTask?.Quest?.Id == quest.Id) _trainingReturnTask = null;
            Report("quest_abandoned", $"{quest.LogTitle}: {reason}");
        }

        int QuestLevel(Quest quest) => quest.Level <= 0 && quest.MaxScalingLevel <= 0 ? me.GetLevel() : me.GetQuestLevel(quest);
        // Earned turn-ins remain valid after levelling. Pickup-level filtering
        // must not discard completed work already present in the quest log.
        bool QuestLevelEligible(Quest quest) => quest != null &&
            (me.GetQuestStatus(quest.Id)==QuestStatus.Complete||SIQuestPolicy.Eligible(me.GetLevel(), QuestLevel(quest), quest.MinLevel));

        void ReviewQuestLevels()
        {
            if (Now < _nextQuestLevelCheck) return;
            _nextQuestLevelCheck = Now + 5000;
            foreach (var (_, quest) in QuestLog().ToArray())
                if (me.GetQuestStatus(quest.Id)==QuestStatus.Incomplete&&SIQuestPolicy.TooLow(me.GetLevel(), QuestLevel(quest)))
                    AbandonQuest(quest, $"Quest level {QuestLevel(quest)} is at least five below player level {me.GetLevel()}");
            // Keep higher-level quests in the log for later, but never pursue them now.
            if (_task?.Quest is Quest current && !QuestLevelEligible(current)) { SetTask(null); _nextReplan = 0; }
            if (_suspendedTask?.Quest is Quest suspended && !QuestLevelEligible(suspended)) _suspendedTask = null;
            if (_trainingReturnTask?.Quest is Quest trainingReturn && !QuestLevelEligible(trainingReturn)) _trainingReturnTask = null;
        }

        string QuestWaitStatus()
        {
            int ready=0,pending=0;
            foreach(var (_,quest) in QuestLog())
            {
                var status=me.GetQuestStatus(quest.Id);
                if(status==QuestStatus.Complete)++ready;
                else if(status==QuestStatus.Incomplete)++pending;
            }
            // A failed NPC approach can defer a turn-in even when there are no
            // new pickups nearby. Report the accepted work, not an unrelated
            // pickup scan as the reason for standing still.
            if(ready+pending>0)
                return $"Questing: {ready} ready for turn-in, {pending} unfinished; retrying deferred accepted work and checking the next eligible giver";
            return "Questing: "+_questSearchSummary+"; checking the nearest eligible world-database quest giver";
        }

        // Friendly NPC templates only: vendors, trainers and quest givers of the other faction are skipped.
        bool FriendlyTemplate(int entry)
        {
            CreatureTemplate template = Global.ObjectMgr.GetCreatureTemplate(entry);
            if (template == null) return false;
            FactionTemplateRecord faction = CliDB.FactionTemplateStorage.LookupByKey(template.Faction);
            FactionTemplateRecord mine = me.GetFactionTemplateEntry();
            return faction != null && mine != null && !mine.IsHostileTo(faction) && !faction.IsHostileTo(mine);
        }

        bool SpawnAvailable(SISpawn spawn) => !_missingSpawns.TryGetValue(spawn.SpawnId * 2 + (spawn.GameObject ? 1 : 0), out long until) || until <= Now;
        void MarkMissing(SISpawn spawn, long milliseconds) => _missingSpawns[spawn.SpawnId * 2 + (spawn.GameObject ? 1 : 0)] = Now + milliseconds;

        SISpawn? NearestSpawn(IEnumerable<int> creatures, IEnumerable<int> objects, bool friendly)
        {
            SISpawn? best = null;
            float bestDistance = float.MaxValue;
            foreach (SISpawn spawn in Knowledge.Spawns(creatures, objects, me.GetMapId()))
            {
                if (!SpawnAvailable(spawn) || IsLearnedRouteBlocked(P(spawn.Position))) continue;
                if (friendly && !spawn.GameObject && !FriendlyTemplate(spawn.Entry)) continue;
                float distance = Distance(spawn.Position);
                if (distance < bestDistance) { bestDistance = distance; best = spawn; }
            }
            return best;
        }

        int QuestLogCount()
        {
            int count = 0;
            for (ushort slot = 0; slot < SharedConst.MaxQuestLogSize; ++slot) if (me.GetQuestSlotQuestId(slot) != 0) ++count;
            return count;
        }

        bool WantsQuest(Quest quest)
        {
            return QuestRejection(quest) == null;
        }

        string QuestRejection(Quest quest)
        {
            if (quest == null) return "Missing quest template";
            if (_abandonedQuests.Contains(quest.Id)) return "Previously abandoned after repeated failures";
            if (me.GetQuestStatus(quest.Id) != QuestStatus.None) return "Already accepted or rewarded";
            // The core hides disabled quests from givers; planning a trip for one loops at the NPC.
            if (!Game.Quest.IsTakingQuestEnabled(quest.Id)) return "Quest is disabled on this server";
            if (SIQuestPolicy.TooLow(me.GetLevel(), QuestLevel(quest))) return "Quest is five or more levels too low";
            if (SIQuestPolicy.TooHigh(me.GetLevel(), QuestLevel(quest), quest.MinLevel)) return "Quest level is too high";
            SIQuestPlan plan = Knowledge.Plan(quest);
            if (!plan.Supported) return plan.Reason;
            if (!me.CanTakeQuest(quest, false)) return "Core rejected quest prerequisites";
            if (!me.CanAddQuest(quest, false) && !CanMakeSourceItemSpace(quest)) return "Quest log or inventory has no room";
            if (!me.SatisfyQuestExpansion(quest, false)) return "Account expansion is too low";
            return null;
        }

        // Visible quest givers take precedence over travelling to a database spawn across the zone.
        bool LocalQuestObjective(Quest quest)
        {
            foreach(var objective in Knowledge.Plan(quest).Objectives.Where(o=>o.Supported))
            {
                var task=ObjectiveTask(quest,objective);
                if(task!=null&&TaskDistance(task)<=_settings.TargetSearchRadius)return true;
            }
            return false;
        }
        bool MayPickUpLocalQuest(Quest quest)=>WantsQuest(quest)&&
            (!_workingQuestBatch||LocalQuestObjective(quest));
        // Quest relations are fixed after startup; each lookup copies the relation list.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<(bool GameObject,int Entry),bool> QuestStarters=new();
        static bool OffersQuests(WorldObject giver)=>QuestStarters.GetOrAdd((giver.IsGameObject(),giver.GetEntry()),key=>
            (key.GameObject?Global.ObjectMgr.GetGOQuestRelations(key.Entry):Global.ObjectMgr.GetCreatureQuestRelations(key.Entry)).Count!=0);
        // Once a bot reaches an objective's work area it finishes that objective before picking up quests,
        // switching to another quest, training, learning flight paths or non-urgent upkeep. The task's own
        // no-progress and idle limits still release it if it cannot be done.
        const float EngageRadius=120;
        bool CommittedToObjective()
        {
            if(_task?.Kind!=SITaskKind.Objective||TaskDone(_task)||Blacklisted(_task.Key))return false;
            if(!_task.Engaged&&TaskDistance(_task)<=EngageRadius)_task.Engaged=true;
            return _task.Engaged;
        }
        bool NearbyQuestInteraction()
        {
            if(me.IsInCombat()||_encounter.Active||Now<_nextNearbyQuestScan)return false;
            if(CommittedToObjective())return false;
            if(_task!=null&&!TaskDone(_task)&&(_task.Kind is SITaskKind.PickUp or SITaskKind.TurnIn or SITaskKind.Train or SITaskKind.Vendor))return false;
            _nextNearbyQuestScan=Now+3000; // a 550-yard scan; givers do not appear faster than this matters
            bool collecting=CollectingQuestBatch();
            _questPickupAnchor??=new Position(me.GetPosition());
            // Most nearby objects offer no quests at all; reject them by entry before the
            // visibility, faction and route checks, which dominated this scan's cost.
            var givers=NearbyCreatures(QuestPickupRadius).Cast<WorldObject>().Concat(NearbyObjects(QuestPickupRadius))
                .Where(g=>OffersQuests(g)&&me.GetExactDist(g)<=QuestPickupRadius&&me.CanSeeOrDetect(g,distanceCheck:false)&&!me.IsHostileTo(g)&&
                    SpawnAvailable(SpawnOf(g))&&!IsLearnedRouteBlocked(g.GetPosition())&&
                    (g is not Creature c||c.IsAlive())&&(g is not GameObject o||o.IsSpawned())).OrderBy(me.GetDistance);
            foreach(var giver in givers)
            {
                var offers=giver.IsCreature()?Global.ObjectMgr.GetCreatureQuestRelations(giver.GetEntry()):Global.ObjectMgr.GetGOQuestRelations(giver.GetEntry());
                int id=offers.FirstOrDefault(id=>offers.HasQuest(id)&&MayRetryQuestInteraction(id,giver.GetGUID())&&
                    MayPickUpLocalQuest(Global.ObjectMgr.GetQuestTemplate(id))&&
                    (!collecting||WithinQuestPickupArea(_questPickupAnchor,giver.GetPosition(),QuestPickupRadius)));
                if(id==0)continue;
                var quest=Global.ObjectMgr.GetQuestTemplate(id);
                SetTask(new SITask{Kind=SITaskKind.PickUp,Quest=quest,Target=giver.GetGUID(),Destination=SpawnOf(giver),
                    Creatures=giver is Creature?[giver.GetEntry()]:[],Objects=giver is GameObject?[giver.GetEntry()]:[],Label="Pick up "+quest.LogTitle});
                return true;
            }
            _workingQuestBatch=true;
            _questSearchSummary=$"No eligible local quest pickups within {QuestPickupRadius:0} yards";return false;
        }

        SISpawn SpawnOf(WorldObject target) => new(me.GetMapId(), V(target.GetPosition()),
            target is Creature creature ? creature.GetSpawnId() : target.ToGameObject().GetSpawnId(), target.GetEntry(), target is GameObject);

        bool QuestGiverHasWork(WorldObject giver, bool turnIn)
        {
            var quests = giver.IsCreature()
                ? (turnIn ? Global.ObjectMgr.GetCreatureQuestInvolvedRelations(giver.GetEntry()) : Global.ObjectMgr.GetCreatureQuestRelations(giver.GetEntry()))
                : (turnIn ? Global.ObjectMgr.GetGOQuestInvolvedRelations(giver.GetEntry()) : Global.ObjectMgr.GetGOQuestRelations(giver.GetEntry()));
            return quests.Any(id => quests.HasQuest(id) && MayRetryQuestInteraction(id, giver.GetGUID()) && (turnIn
                ? me.GetQuestStatus(id) == QuestStatus.Complete && QuestLevelEligible(Global.ObjectMgr.GetQuestTemplate(id))
                : WantsQuest(Global.ObjectMgr.GetQuestTemplate(id))));
        }

        IEnumerable<(ushort Slot, Quest Quest)> QuestLog()
        {
            for (ushort slot = 0; slot < SharedConst.MaxQuestLogSize; ++slot)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(me.GetQuestSlotQuestId(slot));
                if (quest != null) yield return (slot, quest);
            }
        }

        // --- Planning --------------------------------------------------------------------------------

        bool TaskDone(SITask task)
        {
            switch (task.Kind)
            {
                case SITaskKind.PickUp: return me.GetQuestStatus(task.Quest.Id) != QuestStatus.None || !WantsQuest(task.Quest);
                case SITaskKind.TurnIn: return !QuestLevelEligible(task.Quest) || me.GetQuestStatus(task.Quest.Id) != QuestStatus.Complete;
                case SITaskKind.Objective:
                    ushort slot = me.FindQuestSlot(task.Quest.Id);
                    return !QuestLevelEligible(task.Quest) || slot >= SharedConst.MaxQuestLogSize || me.GetQuestStatus(task.Quest.Id) != QuestStatus.Incomplete ||
                        me.IsQuestObjectiveComplete(slot, task.Quest, task.Objective.Objective);
                case SITaskKind.Train: return task.InteractionCompleted ||
                    (_trainingOffers != null && task.Destination.HasValue && !HasSomethingToLearn(task.Destination.Value.Entry));
                case SITaskKind.Vendor: return task.InteractionCompleted || task.VendorPurpose switch
                {
                    SIVendorPurpose.Sell => !HasJunk(),
                    SIVendorPurpose.Repair => !NeedsRepair(),
                    SIVendorPurpose.Ammunition => !NeedsAmmunition(),
                    _ => !NeedsRestock()
                };
                default: return false;
            }
        }

        int TaskProgress(SITask task)
        {
            if (task.Kind != SITaskKind.Objective) return 0;
            ushort slot = me.FindQuestSlot(task.Quest.Id);
            return slot < SharedConst.MaxQuestLogSize ? me.GetQuestSlotObjectiveData(slot, task.Objective.Objective) : 0;
        }

        List<SITask> Candidates()
        {
            var tasks = new List<SITask>();
            int map = me.GetMapId();
            int level = me.GetLevel();

            // Turn-ins.
            foreach (var (slot, quest) in QuestLog())
            {
                if (!QuestLevelEligible(quest) || me.GetQuestStatus(quest.Id) != QuestStatus.Complete) continue;
                SISpawn? ender = NearestSpawn(Knowledge.CreatureEnders(quest.Id), Knowledge.ObjectEnders(quest.Id), true);
                if (ender == null) continue;
                tasks.Add(new SITask { Kind = SITaskKind.TurnIn, Quest = quest, Destination = ender,
                    Creatures = Knowledge.CreatureEnders(quest.Id).ToArray(), Objects = Knowledge.ObjectEnders(quest.Id).ToArray(),
                    Score = Distance(ender.Value.Position) * .6f, Label = $"Turn in \"{quest.LogTitle}\"" });
            }

            // Objectives of quests in the log.
            foreach (var (slot, quest) in QuestLog())
            {
                if (!QuestLevelEligible(quest) || me.GetQuestStatus(quest.Id) != QuestStatus.Incomplete) continue;
                SIQuestPlan plan = Knowledge.Plan(quest);
                foreach (SIObjectivePlan objective in plan.Objectives)
                {
                    if (!objective.Supported || me.IsQuestObjectiveComplete(slot, quest, objective.Objective)) continue;
                    if (objective.Sources.All(source => source is SIObjectiveSource.Provided or SIObjectiveSource.Passive)) continue;
                    var task = ObjectiveTask(quest, objective);
                    if (task != null && !Blacklisted(task.Key)) tasks.Add(task);
                }
            }

            // Pickup belongs to the fixed nearby batch scan; never commute across the zone for more quests.

            // Upkeep is owned by TickVendorBehavior: bag pressure must not add a
            // cross-zone vendor destination to an otherwise local quest plan.
            tasks.RemoveAll(task => Blacklisted(task.Key));
            return tasks;
        }

        SITask ObjectiveTask(Quest quest, SIObjectivePlan objective, WorldObject nearbyTarget = null)
        {
            var creatures = new List<int>();
            var objects = new List<int>();
            foreach (SIObjectiveSource source in objective.Sources)
                switch (source)
                {
                    case SIObjectiveSource.Kill:
                    case SIObjectiveSource.CreatureDrop:
                    case SIObjectiveSource.Talk:
                        // Preserve the objective's source identity. Pull readiness is evaluated on
                        // each live creature, not by erasing all sources then roaming its POI.
                        creatures.AddRange(objective.Creatures.Where(entry => Global.ObjectMgr.GetCreatureTemplate(entry) != null));
                        break;
                    case SIObjectiveSource.UseObject:
                    case SIObjectiveSource.ObjectLoot:
                        objects.AddRange(objective.Objects);
                        break;
                    case SIObjectiveSource.Vendor:
                        if (nearbyTarget != null) break;
                        SISpawn? vendor = NearestSpawn(objective.Vendors, Array.Empty<int>(), true);
                        if (vendor != null)
                            return new SITask { Kind = SITaskKind.Objective, Quest = quest, Objective = objective, Destination = vendor,
                                Creatures = objective.Vendors, Score = Distance(vendor.Value.Position) * .8f,
                                Label = $"Buy {ItemName(objective.Objective.ObjectID)} for \"{quest.LogTitle}\"" };
                        break;
                }
            var task = new SITask { Kind = SITaskKind.Objective, Quest = quest, Objective = objective,
                Creatures = creatures.Distinct().ToArray(), Objects = objects.Distinct().ToArray() };
            bool needsEnemy = objective.Sources.Any(source => source is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop) &&
                !objective.Sources.Contains(SIObjectiveSource.UseItem);
            var points = Knowledge.Spawns(task.Creatures, task.Objects, me.GetMapId()).Where(SpawnAvailable)
                .Where(spawn => !needsEnemy || spawn.GameObject || SpawnCanProvideEnemy(spawn))
                .Where(spawn=>spawn.GameObject||!objective.Sources.Any(source=>source is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop)||SpawnCanProvideEnemy(spawn))
                .Where(spawn=>!spawn.GameObject||me.GetMap().GetGameObjectBySpawnId(spawn.SpawnId)?.IsSpawned()!=false)
                .Select(spawn=>KnownObjectivePosition(spawn)).ToList();
            // Spawn tables guide travel; visible creatures also supply work (including event/manual spawns).
            points.AddRange(NearbyCreatures(_settings.TargetSearchRadius)
                .Where(creature => task.Creatures.Contains(creature.GetEntry()) && creature.IsAlive() && me.CanSeeOrDetect(creature, distanceCheck: true))
                .Select(creature => V(creature.GetPosition())));
            points.AddRange(NearbyObjects(_settings.TargetSearchRadius)
                .Where(obj => task.Objects.Contains(obj.GetEntry()) && obj.IsSpawned() && me.CanSeeOrDetect(obj, distanceCheck: true))
                .Select(obj => V(obj.GetPosition())));
            if (objective.Sources.Contains(SIObjectiveSource.Explore))
                points.AddRange(objective.Triggers.Where(trigger => trigger.ContinentID == me.GetMapId()).Select(trigger => trigger.Pos));
            // Quest POI regions narrow very common creatures to the intended area.
            if (objective.Poi.Length != 0 && (points.Count == 0 || points.Count > 40))
            {
                var poi = objective.Poi.ToList();
                var inside = points.Where(point => poi.Any(p => Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(point.X, point.Y)) < 120)).ToList();
                points = inside.Count != 0 ? inside : points.Count == 0 &&
                    objective.Sources.Any(source => source is SIObjectiveSource.Explore or SIObjectiveSource.UseItem) ? poi : points;
            }
            if (points.Count == 0) return null;
            var hotspots = points.Where(point => SearchAreaAvailable(task.Key, point) && !IsLearnedRouteBlocked(P(point))).Distinct().OrderBy(Distance).Take(32).ToList();
            if (hotspots.Count == 0) return null;
            // One area at a time: the hotspots near the closest one.
            float nearest = hotspots.Min(Distance);
            task.Hotspots = hotspots.Where(point => Distance(point) <= nearest + 350).OrderBy(Distance).Take(16).ToList();
            task.Score = task.Hotspots.Min(Distance);
            string what = objective.Objective.Type switch
            {
                QuestObjectiveType.Monster => "Kill " + CreatureName(objective.Objective.ObjectID),
                QuestObjectiveType.Item => "Collect " + ItemName(objective.Objective.ObjectID),
                QuestObjectiveType.GameObject => "Use " + ObjectName(objective.Objective.ObjectID),
                QuestObjectiveType.TalkTo => "Speak with " + CreatureName(objective.Objective.ObjectID),
                _ => "Explore"
            };
            task.Label = $"{what} for \"{quest.LogTitle}\"";
            return task;
        }

        static string CreatureName(int entry) => Global.ObjectMgr.GetCreatureTemplate(entry)?.Name ?? $"creature {entry}";
        Vector3 KnownObjectivePosition(SISpawn spawn)
        {
            WorldObject live=spawn.GameObject?me.GetMap().GetGameObjectBySpawnId(spawn.SpawnId):me.GetMap().GetCreatureBySpawnId(spawn.SpawnId);
            return live!=null&&live.IsInWorld&&me.CanSeeOrDetect(live,distanceCheck:false)?V(live.GetPosition()):spawn.Position;
        }
        static string ObjectName(int entry) => Global.ObjectMgr.GetGameObjectTemplate(entry)?.name ?? $"object {entry}";
        static string ItemName(int entry) => Global.ObjectMgr.GetItemTemplate(entry)?.GetName() ?? $"item {entry}";

        void Replan()
        {
            _nextReplan=Now;
            foreach(var key in _taskBlacklist.Where(p=>p.Value<=Now).Select(p=>p.Key).ToArray())_taskBlacklist.Remove(key);
            foreach(var key in _missingSpawns.Where(p=>p.Value<=Now).Select(p=>p.Key).ToArray())_missingSpawns.Remove(key);
            if(_task!=null&&!TaskDone(_task)&&!Blacklisted(_task.Key)&&(_task.Kind is SITaskKind.PickUp or SITaskKind.Train or SITaskKind.Vendor))return;
            if(CommittedToObjective())
            {
                // Keep the work area current (live spawns move, searched areas expire) without changing the task.
                var fresh=Candidates().FirstOrDefault(task=>task.Key==_task.Key);
                if(fresh!=null)Refresh(_task,fresh);
                return;
            }
            var primary=QuestLog().Where(q=>me.GetQuestStatus(q.Quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete)
                .OrderBy(q=>QuestAcceptanceOrder(q.Quest)).Select(q=>q.Quest).FirstOrDefault();
            var best=SIQuestOrderPolicy.Choose(Candidates(),task=>task.Quest==null?long.MaxValue:QuestAcceptanceOrder(task.Quest),TaskDistance,
                primary==null?long.MinValue:QuestAcceptanceOrder(primary),_settings.TargetSearchRadius);
            // Keep usable accepted work first, even when an older quest is gated.
            // Only an empty accepted-work plan may search the database's next giver.
            if(best==null&&(_task==null||TaskDone(_task)))best=NextAvailableQuest();
            if(best==null){if(_task!=null&&!TaskDone(_task)&&!Blacklisted(_task.Key))return;SetTask(null);return;}
            if(best.Kind!=SITaskKind.PickUp)_workingQuestBatch=true;
            if(_task!=null&&!TaskDone(_task)&&_task.Key==best.Key){Refresh(_task,best);return;}
            SetTask(best);
        }

        long _nextFrontierQuestSearch;
        SITask NextAvailableQuest()
        {
            if(QuestLogCount()>=SharedConst.MaxQuestLogSize||Now<_nextFrontierQuestSearch)return null;
            _nextFrontierQuestSearch=Now+30000+(long)(me.GetGUID().GetCounter()%5000);
            SITask best=null;float nearest=float.MaxValue;
            // Knowledge is the shared immutable index of world DB spawns/relations.
            // Reusing it avoids one SQL round trip per bot per decision tick.
            foreach(int id in Knowledge.QuestsStartedOnMap(me.GetMapId()))
            {
                var quest=Global.ObjectMgr.GetQuestTemplate(id);
                if(quest==null||!QuestLevelEligible(quest)||me.GetQuestStatus(id)!=QuestStatus.None)continue;
                var spawn=NearestSpawn(Knowledge.CreatureStarters(id),Knowledge.ObjectStarters(id),true);
                if(spawn==null||Distance(spawn.Value.Position)>=nearest||!WantsQuest(quest))continue;
                var candidate=new SITask{Kind=SITaskKind.PickUp,Quest=quest,Destination=spawn,
                    Creatures=Knowledge.CreatureStarters(id),Objects=Knowledge.ObjectStarters(id),Label="Travel to next available quest: "+quest.LogTitle};
                if(Blacklisted(candidate.Key))continue;
                best=candidate;nearest=Distance(spawn.Value.Position);
            }
            if(best!=null){_workingQuestBatch=false;_questPickupAnchor=null;}
            return best;
        }

        static SITask ChooseQuestTask(IEnumerable<SITask> candidates, Func<SITask, bool> local = null) =>
            SIQuestBatchPolicy.Choose(candidates, task => task.Score, local ?? (_ => false), false);

        static void Refresh(SITask current, SITask fresh)
        {
            current.Destination = fresh.Destination;
            if (fresh.Hotspots.Count != 0 && current.Hotspots.Count != fresh.Hotspots.Count) { current.Hotspots = fresh.Hotspots; current.Hotspot = -1; }
            current.Score = fresh.Score;
        }

        void SetTask(SITask task)
        {
            // An engaged objective should only give way when it is done, released, or for urgent upkeep.
            if (task != null && !ReferenceEquals(task, _task) && _task?.Engaged == true && !TaskDone(_task) && !Blacklisted(_task.Key))
                Report("engaged_task_replaced", $"{_task.Label} replaced by {task.Label}");
            if (!ReferenceEquals(_task, task)) _task?.Suspend(Now);
            CancelTravel();
            ReleaseQuestTarget();
            _task = task;
            _activeQuest = task?.Quest?.Id ?? 0;
            _activeObjective = task?.Objective?.Objective.Id ?? 0;
            if (task == null) return;
            task.Resume(Now);
            if (!task.Started)
            { task.Started = true; task.StartedAt = task.LastProgressAt = Now; task.ProgressMark = TaskProgress(task); }
            Report("task_started", task.Label);
        }

    }
}
