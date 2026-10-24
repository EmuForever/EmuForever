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
            FactionTemplateRecord mine = me.GetFactionTemplateEntry();
            var points = new List<Vector3>();
            var fallback = new List<Vector3>();
            foreach (SISpawn spawn in Knowledge.CreaturesOnMap(me.GetMapId()))
            {
                if (!SpawnAvailable(spawn) || !SpawnCanProvideEnemy(spawn)) continue;
                CreatureTemplate template = Global.ObjectMgr.GetCreatureTemplate(spawn.Entry);
                if (template == null || template.Classification != CreatureClassifications.Normal ||
                    template.CreatureType is CreatureType.Critter or CreatureType.NonCombatPet or CreatureType.Totem ||
                    template.Npcflag != NPCFlags1.None || _dangerousEntries.ContainsKey(spawn.Entry)) continue;
                CreatureTemplateStats difficulty = template.Stats;
                if (difficulty.MinLevel > level || difficulty.MaxLevel + 2 < level) continue;
                FactionTemplateRecord faction = CliDB.FactionTemplateStorage.LookupByKey(template.Faction);
                if (!GrindFactionAllowed(mine, faction)) continue;
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
                .Where(c => c.IsAlive() && CanFight(c) && !c.IsCivilian() && c.GetCreatureTemplate().Classification == CreatureClassifications.Normal &&
                    c.GetLevel() <= me.GetLevel() && c.GetLevel() + 2 >= me.GetLevel() && c.GetOwnerGUID().IsEmpty() &&
                    !c.HasNpcFlag(NPCFlags1.QuestGiver | NPCFlags1.Gossip | NPCFlags1.Vendor) && !AvoidEnemy(c) && TargetRetryReady(c) &&
                    (!c.HasLootRecipient() || c.IsTappedBy(me)) && ReservationAvailable(c.GetGUID()) && SafeToInitiate(c))
                .OrderBy(c=>Math.Abs(c.GetLevel()-me.GetLevel()))
                .ThenBy(c => TargetPreference.TargetCost((ulong)c.GetGUID().GetCounter(), 0, me.GetDistance(c)))
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
