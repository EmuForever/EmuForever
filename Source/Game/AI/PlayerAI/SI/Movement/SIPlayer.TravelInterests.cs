using Framework.Constants;
using Game.AI.SIMovement;
using Game.Entities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Game.AI;

// Long trips choose between routes by what they pass: the planner weighs quest turn-ins and trainers with
// lessons against the extra distance, and the journey stops when it gets near one.
public sealed partial class SIPlayer
{
    // The travel graph waypoint the current trip walks to; kept until reached or the destination changes.
    Vector3? _routeWaypoint;
    ImmutableArray<SITravelInterest> _travelInterests = [];
    long _nextTravelInterests;
    readonly HashSet<int> _professionTrainerLessons = new();
    readonly HashSet<int> _turnInEnders = new();

    SIRouteHints RouteHints()
    {
        if (_routeWaypoint is Vector3 via && SIMovementRoute.ReachedTravelWaypoint(Here, via)) _routeWaypoint = null;
        return new(_routeWaypoint, OwnedServiceTrip || CommittedToJourney() || _goal == "social" && _socialWalkDestination.HasValue ? [] : TravelInterests(),
            CaptureMovementObstacles());
    }

    ImmutableArray<SITravelInterest> TravelInterests()
    {
        if (Now < _nextTravelInterests || !SIQuestKnowledge.IsBuilt) return _travelInterests;
        _nextTravelInterests = Now + 20000;
        var interests = new List<SITravelInterest>();
        void Add(IEnumerable<int> entries, float worth)
        {
            foreach (SISpawn spawn in Knowledge.Spawns(entries, System.Array.Empty<int>(), me.GetMapId()))
                if (SpawnAvailable(spawn) && FriendlyTemplate(spawn.Entry)) interests.Add(new(spawn.Position, worth));
        }
        _turnInEnders.Clear();
        if (_goal == "quest")
            foreach (var (_, quest) in QuestLog())
                if (me.GetQuestStatus(quest.Id) == QuestStatus.Complete)
                    foreach (int ender in Knowledge.CreatureEnders(quest.Id)) _turnInEnders.Add(ender);
        Add(_turnInEnders, 400);
        if (WorkingGoal)
        {
            Add(_trainingEntries, 250);
            Add(_professionTrainerLessons, 200);
            if (NeedsRepair() && CanAffordRepair() || BagPressure())
                Add(Knowledge.Repairers.Where(entry => Global.ObjectMgr.GetCreatureTemplate(entry)?.Npcflag.HasFlag(NPCFlags1.Vendor) == true), 120);
        }
        // the closest few; far ones are no use to this trip's choice and only cost scoring time
        _travelInterests = interests.OrderBy(interest => Distance(interest.Position)).Take(32).ToImmutableArray();
        return _travelInterests;
    }

    // A stop the route was chosen for is next to the bot: end the commute so the normal upkeep takes it.
    bool NearUsefulStop()
    {
        if (_travelInterests.Length == 0) return false;
        foreach (Creature npc in NearbyCreatures(60))
        {
            if (!npc.IsAlive() || me.IsHostileTo(npc) || npc.IsHostileTo(me)) continue;
            int entry = npc.GetEntry();
            if (_turnInEnders.Contains(entry) || _trainingEntries.Contains(entry)) return true;
            if (_professionTrainerLessons.Contains(entry)) { _nextProfessionTrainingScan = 0; return true; }
        }
        return false;
    }
}
