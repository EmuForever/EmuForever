using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Loots;
using Game.Maps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Game.AI
{
    public readonly record struct SISpawn(int Map, Vector3 Position, long SpawnId, int Entry, bool GameObject);
    public readonly record struct SIFlightMasterStop(SISpawn Spawn,int HordeNode,int AllianceNode);

    public enum SIObjectiveSource { Kill, CreatureDrop, ObjectLoot, UseObject, Talk, Explore, Vendor, Provided, Passive, UseItem }

    // How one quest objective can be progressed, derived from the world database. An objective with no
    // source is unsupported and makes its quest a poor pick.
    public sealed record SIObjectivePlan(QuestObjective Objective, SIObjectiveSource[] Sources, int[] Creatures,
        int[] Objects, int[] Vendors, AreaTriggerRecord[] Triggers, Vector3[] Poi)
    {
        public bool Supported => Sources.Length != 0;
    }

    public sealed record SIQuestPlan(Quest Quest, SIObjectivePlan[] Objectives, bool Supported, string Reason);

    // Database-backed replacement for questing profiles (HB QuestOrder/DropDatabase/NpcQueries): who starts
    // and ends each quest, where everything spawns, which creatures/objects/vendors provide quest items,
    // kill-credit proxies, explore triggers, quest POI regions and class trainers. Built once, read-only.
    public sealed class SIQuestKnowledge
    {
        static readonly Lazy<SIQuestKnowledge> Lazy = new(() => new SIQuestKnowledge(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static SIQuestKnowledge Instance => Lazy.Value;
        public static bool IsBuilt => Lazy.IsValueCreated;

        static readonly SISpawn[] NoSpawns = Array.Empty<SISpawn>();
        static readonly int[] NoEntries = Array.Empty<int>();

        readonly Dictionary<int, SISpawn[]> _creatureSpawns = new();
        readonly Dictionary<int, SISpawn[]> _objectSpawns = new();
        readonly Dictionary<int, int[]> _creatureStarters = new();
        readonly Dictionary<int, int[]> _objectStarters = new();
        readonly Dictionary<int, int[]> _questsByMap = new();
        readonly Dictionary<int, (int Entry, float Chance)[]> _itemDroppers = new();
        readonly Dictionary<int, int[]> _itemObjects = new();
        readonly Dictionary<int, int[]> _itemVendors = new();
        readonly Dictionary<int, int[]> _killCreditProxies = new();
        readonly Dictionary<int, AreaTriggerRecord[]> _questTriggers = new();
        readonly Dictionary<Class, int[]> _classTrainers = new();
        readonly Dictionary<int, SISpawn[]> _creaturesByMap = new();
        readonly int[] _vendors, _repairers;
        readonly SIFlightMasterStop[] _flightMasters;
        readonly System.Collections.Concurrent.ConcurrentDictionary<int, SIQuestPlan> _plans = new();
        readonly object _planLock = new();

        public int QuestCount { get; }
        public TimeSpan BuildTime { get; }

        SIQuestKnowledge()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var creatures = new Dictionary<int, List<SISpawn>>();
            var byMap = new Dictionary<int, List<SISpawn>>();
            foreach (CreatureData data in Global.ObjectMgr.GetAllCreatureData().Values)
            {
                if (!Natural(data)) continue;
                var spawn = new SISpawn(data.MapId, Vector(data.SpawnPoint), data.SpawnId, data.Id, false);
                Add(creatures, data.Id, spawn);
                Add(byMap, data.MapId, spawn);
            }
            foreach (var pair in creatures) _creatureSpawns[pair.Key] = pair.Value.ToArray();
            foreach (var pair in byMap) _creaturesByMap[pair.Key] = pair.Value.ToArray();

            var objects = new Dictionary<int, List<SISpawn>>();
            foreach (GameObjectData data in Global.ObjectMgr.GetAllGameObjectData().Values)
                if (Natural(data))
                    Add(objects, data.Id, new SISpawn(data.MapId, Vector(data.SpawnPoint), data.SpawnId, data.Id, true));
            foreach (var pair in objects) _objectSpawns[pair.Key] = pair.Value.ToArray();

            var starters = new Dictionary<int, List<int>>();
            foreach (var pair in Global.ObjectMgr.GetCreatureQuestRelationMapHACK()) Add(starters, pair.Value, pair.Key);
            foreach (var pair in starters) _creatureStarters[pair.Key] = pair.Value.Distinct().ToArray();
            starters.Clear();
            foreach (var pair in Global.ObjectMgr.GetGOQuestRelationMapHACK()) Add(starters, pair.Value, pair.Key);
            foreach (var pair in starters) _objectStarters[pair.Key] = pair.Value.Distinct().ToArray();

            var questMaps = new Dictionary<int, HashSet<int>>();
            foreach (var (quest, entries) in _creatureStarters)
                foreach (int entry in entries)
                    foreach (SISpawn spawn in CreatureSpawns(entry)) AddSet(questMaps, spawn.Map, quest);
            foreach (var (quest, entries) in _objectStarters)
                foreach (int entry in entries)
                    foreach (SISpawn spawn in ObjectSpawns(entry)) AddSet(questMaps, spawn.Map, quest);
            foreach (var pair in questMaps) _questsByMap[pair.Key] = pair.Value.ToArray();

            // Item sources: creature loot, gameobject loot and quest item lists, vendors.
            var droppers = new Dictionary<int, Dictionary<int, float>>();
            void Drop(int item, int creature, float chance)
            {
                if (!droppers.TryGetValue(item, out var map)) droppers[item] = map = new Dictionary<int, float>();
                map[creature] = Math.Max(map.GetValueOrDefault(creature), Math.Abs(chance));
            }
            var proxies = new Dictionary<int, List<int>>();
            var trainers = new Dictionary<Class, List<int>>();
            var vendors = new List<int>();
            var repairers = new List<int>();
            var flightMasters = new List<SIFlightMasterStop>();
            var vendorItems = new Dictionary<int, List<int>>();
            foreach (CreatureTemplate template in Global.ObjectMgr.GetCreatureTemplates().Values)
            {
                int entry = template.Entry;
                int lootId = template.Stats.LootID;
                if (lootId != 0)
                    LootStorage.Creature.ForEachDrop(lootId, (item, chance, quest) => Drop(item, entry, chance));
                IReadOnlyList<int> questItems = Global.ObjectMgr.GetCreatureQuestItemList(entry, Difficulty.None);
                if (questItems != null) foreach (int item in questItems) Drop(item, entry, 0);
                foreach (int credit in template.KillCredit)
                    if (credit != 0) Add(proxies, credit, entry);
                if (!_creatureSpawns.ContainsKey(entry)) continue;
                if(template.Npcflag.HasFlag(NPCFlags1.FlightMaster))
                    foreach(var spawn in _creatureSpawns[entry])
                        flightMasters.Add(new(spawn,
                            Global.ObjectMgr.GetNearestTaxiNode(spawn.Position,spawn.Map,Team.Horde),
                            Global.ObjectMgr.GetNearestTaxiNode(spawn.Position,spawn.Map,Team.Alliance)));
                if (template.Npcflag.HasFlag(NPCFlags1.Vendor))
                {
                    vendors.Add(entry);
                    VendorItemData items = Global.ObjectMgr.GetNpcVendorItemList(entry);
                    for (int slot = 0; items != null && slot < items.GetItemCount(); ++slot)
                    {
                        VendorItem item = items.GetItem(slot);
                        if (item != null && item.Type == ItemVendorType.Item && item.ExtendedCost == 0) Add(vendorItems, item.item, entry);
                    }
                }
                if (template.Npcflag.HasFlag(NPCFlags1.Repair)) repairers.Add(entry);
                if (template.Npcflag.HasFlag(NPCFlags1.Trainer) && template.TrainerClass != Class.None)
                {
                    if (SITrainerCatalog.ForCreature(entry).Any())
                        Add(trainers, template.TrainerClass, entry);
                }
            }
            foreach (var pair in droppers) _itemDroppers[pair.Key] = pair.Value.Select(p => (p.Key, p.Value)).ToArray();
            foreach (var pair in proxies) _killCreditProxies[pair.Key] = pair.Value.Distinct().ToArray();
            foreach (var pair in trainers) _classTrainers[pair.Key] = pair.Value.ToArray();
            foreach (var pair in vendorItems) _itemVendors[pair.Key] = pair.Value.Distinct().ToArray();
            _vendors = vendors.ToArray();
            _repairers = repairers.ToArray();
            _flightMasters = flightMasters.ToArray();

            var objectItems = new Dictionary<int, List<int>>();
            foreach (int entry in _objectSpawns.Keys)
            {
                GameObjectTemplate template = Global.ObjectMgr.GetGameObjectTemplate(entry);
                if (template == null) continue;
                int lootId = template.GetLootId();
                if (lootId != 0)
                    LootStorage.Gameobject.ForEachDrop(lootId, (item, chance, quest) => Add(objectItems, item, entry));
                foreach (int item in Global.ObjectMgr.GetGameObjectQuestItemList(entry) ?? (IReadOnlyList<int>)NoEntries)
                    Add(objectItems, item, entry);
            }
            foreach (var pair in objectItems) _itemObjects[pair.Key] = pair.Value.Distinct().ToArray();

            var triggers = new Dictionary<int, List<AreaTriggerRecord>>();
            foreach (AreaTriggerRecord trigger in CliDB.AreaTriggerStorage.Values)
                foreach (int quest in Global.ObjectMgr.GetQuestsForAreaTrigger(trigger.Id) ?? (IReadOnlyList<int>)NoEntries)
                    Add(triggers, quest, trigger);
            foreach (var pair in triggers) _questTriggers[pair.Key] = pair.Value.ToArray();

            QuestCount = Global.ObjectMgr.GetQuestTemplates().Count;
            BuildTime = watch.Elapsed;
        }

        static bool Natural(SpawnData data) =>
            data.MapId >= 0 && (data.spawnGroupData == null || !data.spawnGroupData.flags.HasAnyFlag(SpawnGroupFlags.ManualSpawn | SpawnGroupFlags.EscortQuestNpc));

        static Vector3 Vector(Position position) => new(position.GetPositionX(), position.GetPositionY(), position.GetPositionZ());

        static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<TValue>();
            list.Add(value);
        }

        static void AddSet(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>();
            set.Add(value);
        }

        public SISpawn[] CreatureSpawns(int entry) => _creatureSpawns.GetValueOrDefault(entry, NoSpawns);
        public SISpawn[] ObjectSpawns(int entry) => _objectSpawns.GetValueOrDefault(entry, NoSpawns);
        public SISpawn[] CreaturesOnMap(int map) => _creaturesByMap.GetValueOrDefault(map, NoSpawns);
        public int[] QuestsStartedOnMap(int map) => _questsByMap.GetValueOrDefault(map, NoEntries);
        public int[] CreatureStarters(int quest) => _creatureStarters.GetValueOrDefault(quest, NoEntries);
        public int[] ObjectStarters(int quest) => _objectStarters.GetValueOrDefault(quest, NoEntries);
        public IReadOnlyList<int> CreatureEnders(int quest) => Global.ObjectMgr.GetCreatureQuestInvolvedRelationReverseBounds(quest);
        public IReadOnlyList<int> ObjectEnders(int quest) => Global.ObjectMgr.GetGOQuestInvolvedRelationReverseBounds(quest);
        public int[] ClassTrainers(Class cls) => _classTrainers.GetValueOrDefault(cls, NoEntries);
        public int[] Vendors => _vendors;
        public int[] Repairers => _repairers;
        public IReadOnlyList<SIFlightMasterStop> FlightMasters => _flightMasters;
        public int[] VendorsSelling(int item) => _itemVendors.GetValueOrDefault(item, NoEntries);

        public IEnumerable<SISpawn> Spawns(IEnumerable<int> creatures, IEnumerable<int> objects, int map) =>
            creatures.SelectMany(CreatureSpawns).Concat(objects.SelectMany(ObjectSpawns)).Where(spawn => spawn.Map == map);

        // Objective-to-source analysis, cached per quest.
        public SIQuestPlan Plan(Quest quest)
        {
            // Cached plans are read by both map threads on every quest decision: lock only to build one.
            if (_plans.TryGetValue(quest.Id, out SIQuestPlan cached)) return cached;
            lock (_planLock)
            {
                if (_plans.TryGetValue(quest.Id, out cached)) return cached;
                SIQuestPlan plan = BuildPlan(quest);
                _plans[quest.Id] = plan;
                return plan;
            }
        }

        SIQuestPlan BuildPlan(Quest quest)
        {
            string reason = Unsuitable(quest);
            QuestPOIData poi = Global.ObjectMgr.GetQuestPOIData(quest.Id);
            var objectives = new List<SIObjectivePlan>();
            foreach (QuestObjective objective in quest.Objectives)
            {
                var sources = new List<SIObjectiveSource>();
                int[] creatures = NoEntries, objects = NoEntries, vendors = NoEntries;
                AreaTriggerRecord[] triggers = Array.Empty<AreaTriggerRecord>();
                switch (objective.Type)
                {
                    case QuestObjectiveType.Monster:
                        creatures = new[] { objective.ObjectID }.Concat(_killCreditProxies.GetValueOrDefault(objective.ObjectID, NoEntries))
                            .Where(entry => Global.ObjectMgr.GetCreatureTemplate(entry) != null).Distinct().ToArray();
                        if (creatures.Length != 0) sources.Add(SIObjectiveSource.Kill);
                        break;
                    case QuestObjectiveType.TalkTo:
                        creatures = Global.ObjectMgr.GetCreatureTemplate(objective.ObjectID) != null ? new[] { objective.ObjectID } : NoEntries;
                        if (creatures.Length != 0) sources.Add(SIObjectiveSource.Talk);
                        break;
                    case QuestObjectiveType.GameObject:
                        objects = _objectSpawns.ContainsKey(objective.ObjectID) ? new[] { objective.ObjectID } : NoEntries;
                        if (objects.Length != 0) sources.Add(SIObjectiveSource.UseObject);
                        break;
                    case QuestObjectiveType.Item:
                        creatures = Droppers(objective.ObjectID);
                        objects = _itemObjects.GetValueOrDefault(objective.ObjectID, NoEntries);
                        vendors = VendorsSelling(objective.ObjectID);
                        if (creatures.Length != 0) sources.Add(SIObjectiveSource.CreatureDrop);
                        if (objects.Length != 0) sources.Add(SIObjectiveSource.ObjectLoot);
                        if (vendors.Length != 0) sources.Add(SIObjectiveSource.Vendor);
                        // Provided by the quest giver on accept.
                        if (sources.Count == 0 && quest.SourceItemId == objective.ObjectID) sources.Add(SIObjectiveSource.Provided);
                        break;
                    case QuestObjectiveType.AreaTrigger:
                    case QuestObjectiveType.AreaTriggerEnter:
                        triggers = _questTriggers.GetValueOrDefault(quest.Id, Array.Empty<AreaTriggerRecord>())
                            .Where(trigger => objective.ObjectID <= 0 || trigger.Id == objective.ObjectID).ToArray();
                        if (triggers.Length == 0 && objective.ObjectID > 0 && CliDB.AreaTriggerStorage.LookupByKey(objective.ObjectID) is AreaTriggerRecord direct)
                            triggers = new[] { direct };
                        if (triggers.Length != 0) sources.Add(SIObjectiveSource.Explore);
                        break;
                    case QuestObjectiveType.Money:
                    case QuestObjectiveType.MinReputation:
                    case QuestObjectiveType.MaxReputation:
                    case QuestObjectiveType.HaveCurrency:
                        // Passive requirements: never block on their own.
                        sources.Add(SIObjectiveSource.Passive);
                        break;
                }
                // Some supplied tools create an objective item or trigger scripted
                // credit in the quest area, without a loot-table source.
                if (sources.Count == 0 && quest.SourceItemId != 0 &&
                    Global.ObjectMgr.GetItemTemplate(quest.SourceItemId)?.Effects.Any(effect => effect.TriggerType == ItemSpelltriggerType.OnUse) == true)
                    sources.Add(SIObjectiveSource.UseItem);
                Vector3[] points = poi?.Blobs.Where(blob => blob.QuestObjectiveID == objective.Id ||
                        blob.QuestObjectiveID == 0 && blob.ObjectiveIndex == objective.StorageIndex)
                    .SelectMany(blob => Hotspots(blob)).ToArray() ?? Array.Empty<Vector3>();
                objectives.Add(new SIObjectivePlan(objective, sources.ToArray(), creatures, objects, vendors, triggers, points));
            }
            if (reason == null && objectives.Any(objective => !objective.Supported && !objective.Objective.Flags.HasAnyFlag(QuestObjectiveFlags.Optional)))
                reason = "An objective has no known source in the world database.";
            bool hasEnder = CreatureEnders(quest.Id).Count != 0 || ObjectEnders(quest.Id).Count != 0;
            if (reason == null && !hasEnder) reason = "No quest ender in the world database.";
            return new SIQuestPlan(quest, objectives.ToArray(), reason == null, reason);
        }

        // Spawned creatures that drop an item; world-drop sources (well under the best chance) are left out.
        int[] Droppers(int item)
        {
            var sources = _itemDroppers.GetValueOrDefault(item, Array.Empty<(int Entry, float Chance)>())
                .Where(source => _creatureSpawns.ContainsKey(source.Entry)).ToArray();
            if (sources.Length == 0) return NoEntries;
            float best = sources.Max(source => source.Chance);
            float floor = best <= 0 ? 0 : Math.Min(1, best * .2f);
            return sources.Where(source => source.Chance >= floor || source.Chance == 0).Select(source => source.Entry).ToArray();
        }

        // Quests a solo character should not pick up by itself.
        static string Unsuitable(Quest quest)
        {
            if (quest.IsUnavailable() || quest.IsWorldQuest() || quest.IsDailyOrWeekly() || quest.IsMonthly() || quest.IsSeasonal() || quest.IsRepeatable())
                return "Unavailable, repeatable or periodic quest.";
            if (quest.HasAnyFlag(QuestFlags.Pvp) || quest.LimitTime != TimeSpan.Zero)
                return "PvP or timed quest.";
            if (quest.SuggestedPlayers > 1)
                return "Group quest.";
            if ((QuestInfos)quest.QuestInfoID is QuestInfos.Group or QuestInfos.Raid or QuestInfos.Dungeon or QuestInfos.Pvp or
                QuestInfos.Escort or QuestInfos.Heroic or QuestInfos.Raid10 or QuestInfos.Raid25 or QuestInfos.Legendary or QuestInfos.Scenario)
                return "Group, dungeon, raid or escort quest.";
            return null;
        }

        // Quest POI region: its centre plus points pulled 40% toward the centre (inside the region).
        static IEnumerable<Vector3> Hotspots(QuestPOIBlobData blob)
        {
            if (blob.Points == null || blob.Points.Count == 0) yield break;
            var centre = new Vector3((float)blob.Points.Average(p => p.X), (float)blob.Points.Average(p => p.Y), (float)blob.Points.Average(p => p.Z));
            yield return centre;
            if (blob.Points.Count < 3) yield break;
            foreach (QuestPOIBlobPoint point in blob.Points)
                yield return Vector3.Lerp(new Vector3(point.X, point.Y, point.Z), centre, .4f);
        }

        // Spawn points grouped into cells, largest groups first: the "hotspots" of a grind or objective area.
        public static List<Vector3> Cluster(IEnumerable<Vector3> points, float cell = 30)
        {
            return points.GroupBy(point => ((int)MathF.Floor(point.X / cell), (int)MathF.Floor(point.Y / cell)))
                .OrderByDescending(group => group.Count())
                // Use an actual spawn near the centre. Averaging XYZ can invent a destination in
                // midair, inside a hill or between two floors with no creature to fight there.
                .Select(group =>
                {
                    Vector3 centre = new(group.Average(p => p.X), group.Average(p => p.Y), group.Average(p => p.Z));
                    return group.OrderBy(point => Vector3.DistanceSquared(point, centre)).First();
                })
                .ToList();
        }
    }
}
