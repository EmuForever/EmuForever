using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        SITask GrindTask()
        {
            int level = me.GetLevel();
            var points = new List<Vector3>();
            var fallback = new List<Vector3>();
            foreach (SISpawn spawn in Knowledge.CreaturesOnMap(me.GetMapId()))
            {
                if (_goal=="quest"&&me.GetZoneId()!=0&&QuestPointZone(spawn.Position)!=me.GetZoneId() || !HuntableSpawn(spawn, out var template)) continue;
                CreatureTemplateStats difficulty = template.Stats;
                if (difficulty.MinLevel > level || difficulty.MaxLevel + 2 < level) continue;
                if(difficulty.MaxLevel>=level)points.Add(spawn.Position);
                else fallback.Add(spawn.Position);
            }
            if(points.Count==0)points=fallback;
            if (points.Count == 0) return null;
            string key = new SITask { Kind = SITaskKind.Grind }.Key;
            // Bound clustering to the nearest available band, after checking the whole
            // map for suitable spawns. An exhausted local radius must not strand a bot.
            points.RemoveAll(point => !SearchAreaAvailable(key, point) || IsLearnedRouteBlocked(P(point)));
            if (points.Count == 0) return null;
            float nearest = points.Min(Distance);
            var hotspots = SIQuestKnowledge.Cluster(points.Where(point => Distance(point) <= nearest + _settings.GrindRadius), 40)
                .Where(point => SearchAreaAvailable(key, point) && !IsLearnedRouteBlocked(P(point))).OrderBy(Distance).Take(12).ToList();
            if (hotspots.Count == 0) return null;
            return new SITask { Kind = SITaskKind.Grind, Hotspots = hotspots, Score = hotspots.Min(Distance),
                Label = "Grind level-appropriate creatures" };
        }

        static bool GrindFactionAllowed(FactionTemplateRecord mine, FactionTemplateRecord faction) =>
            faction != null && mine != null && !faction.IsFriendlyTo(mine) && !mine.IsFriendlyTo(faction);

        // A database spawn worth hunting: an ordinary, non-service, non-friendly creature that is not
        // learned to be lethal and may be alive and untapped right now.
        bool HuntableSpawn(SISpawn spawn, out CreatureTemplate template)
        {
            template = null;
            if (!SpawnAvailable(spawn) || _dangerousEntries.ContainsKey(spawn.Entry) || !SpawnCanProvideEnemy(spawn)) return false;
            template = Global.ObjectMgr.GetCreatureTemplate(spawn.Entry);
            return template != null && template.Classification == CreatureClassifications.Normal && template.Npcflag == NPCFlags1.None &&
                template.CreatureType is not (CreatureType.Critter or CreatureType.NonCombatPet or CreatureType.Totem) &&
                GrindFactionAllowed(me.GetFactionTemplateEntry(), CliDB.FactionTemplateStorage.LookupByKey(template.Faction));
        }

        // A live creature a bot may choose to fight: ordinary, unowned, not a service NPC, untapped, unreserved and safe to pull.
        bool OptionalPull(Creature c) => c.IsAlive() && CanFight(c) && !c.IsCivilian() && c.GetCreatureTemplate().Classification == CreatureClassifications.Normal &&
            c.GetOwnerGUID().IsEmpty() && !c.HasNpcFlag(NPCFlags1.QuestGiver | NPCFlags1.Gossip | NPCFlags1.Vendor) && !AvoidEnemy(c) && TargetRetryReady(c) &&
            (!c.HasLootRecipient() || c.IsTappedBy(me)) && ReservationAvailable(c.GetGUID()) && SafeToInitiate(c);

        bool SpawnCanProvideEnemy(SISpawn spawn)
        {
            if (me.GetMap().GetCreatureRespawnTime(spawn.SpawnId) > LoopTime.ServerTime) return false;
            Creature live = me.GetMap().GetCreatureBySpawnId(spawn.SpawnId);
            // Unloaded grids remain candidates; known dead, tapped or claimed spawns do not.
            return live == null || live.IsAlive() && (!live.HasLootRecipient() || live.IsTappedBy(me)) && ReservationAvailable(live.GetGUID());
        }

        void Grind(SITask task)
        {
            Creature enemy = NearbyCreatures(_settings.TargetSearchRadius)
                .Where(c => c.GetLevel() <= me.GetLevel() && c.GetLevel() + 2 >= me.GetLevel() && OptionalPull(c))
                .OrderBy(c=>Math.Abs(c.GetLevel()-me.GetLevel()))
                .ThenBy(c => HumanMovement.TargetCost((ulong)c.GetGUID().GetCounter(), 0, me.GetDistance(c)))
                .FirstOrDefault(c => ReserveQuestTarget(c.GetGUID()));
            if (enemy != null) { Status = task.Label; BeginEncounter(enemy); TickEncounter(); return; }
            Sweep(task);
        }

        SIBehaviorResult TickGrindBehavior()
        {
            if (!SIQuestKnowledge.IsBuilt) { Status = "Loading world knowledge"; return SIBehaviorResult.Running; }
            if (_task?.Kind != SITaskKind.Grind)
            {
                if (Now < _nextReplan) return SIBehaviorResult.Running;
                _nextReplan = Now + Random.Shared.Next(1000, 5001);
                SITask grind = GrindTask();
                if (grind == null) { Status = "No reachable level-appropriate spawn area; checking again shortly"; return SIBehaviorResult.Running; }
                SetTask(grind);
            }
            Grind(_task);
            return SIBehaviorResult.Running;
        }
    }
}
