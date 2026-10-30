using Framework.Constants;
using Game.AI.SIMovement;
using Game.Entities;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
namespace Game.AI;
public sealed partial class SIPlayer
{
    long _nextTravelAggroCheck;
    bool GrindingTravel => !OwnedServiceTrip&&(_goal=="grind"||_goal=="quest"&&_task?.Kind==SITaskKind.Grind);
    // A long commute ends before the final work neighborhood. Grinding never
    // opts into this policy, even while travelling to a distant grind area.
    bool AvoidAggroOnLongJourney => !GrindingTravel&&_longJourney&&_hasMovementDestination&&!_encounter.Active&&
        Vector3.Distance(Here,_movementDestination.Point)>Math.Max(120,_settings.TargetSearchRadius);

    bool TryBeginTravelClearanceFight()
    {
        if(!WorkingGoal||AvoidAggroOnLongJourney||
            PartyFollowing && !PartyErrand ||
            _encounter.Active||_siDuelActive||!me.IsAlive()||me.IsInCombat()||MovementSuspended||
            _mageRetreatUntil>Now||Now<_nextTravelAggroCheck)return false;
        _nextTravelAggroCheck=Now+250;
        var origin=Here;
        // Use the native aggro distance plus a small reaction margin and a short
        // spline horizon. Clear blockers, not every mob in the entire area.
        float horizon=Math.Clamp(me.GetSpeed(UnitMoveType.Run)*.75f+2,4,12);
        Creature selected=null;float closest=float.MaxValue;
        foreach(var creature in NearbyCreatures(90))
        {
            // Distance first: almost every creature in the snapshot is far from the next few yards of route,
            // and the faction, reservation and sight checks below are comparatively expensive.
            var position=V(creature.GetPosition());float distance=Vector3.DistanceSquared(origin,position);
            if(SITravelCombatPolicy.Trivial(me.GetLevel(),creature.GetLevel(),true))continue;
            // No aggro radius exceeds 45 yards; skip the level-based radius computation for anything further.
            if(distance>=closest||distance>(48+horizon)*(48+horizon))continue;
            float radius=AggroRadius(creature)+3;
            if(distance>(radius+horizon)*(radius+horizon))continue;
            if(!creature.HasReactState(ReactStates.Aggressive)||!creature.IsHostileTo(me)||
                !CanFight(creature)||!TargetRetryReady(creature)||
                creature.HasLootRecipient()&&!creature.IsTappedBy(me)||!ReservationAvailable(creature.GetGUID())||
                !me.IsWithinLOSInMap(creature))continue;
            var threat=new SITravelThreat(position,radius);
            if(distance<=radius*radius||AnyRouteSegmentAhead(horizon,false,(a,b)=>threat.Blocks(a,b,origin)))
            {selected=creature;closest=distance;}
        }
        if(selected==null||!ReserveQuestTarget(selected.GetGUID()))return false;
        // BeginEncounter stops the spline and retains/suspends the quest task.
        // Native combat handles the kill/loot, then resumes that same task;
        // native kill credit also advances other accepted quests for this mob.
        BeginEncounter(selected);_clearingTravelApproach=true;
        SetNavigation("Clearing local approach",false);
        Status="Clearing local approach: "+selected.GetName();
        return true;
    }
    ImmutableArray<SITravelThreat> CaptureMovementThreats()
    {
        var threats=ImmutableArray.CreateBuilder<SITravelThreat>(64);
        // Creature circles belong to long commutes and deliberate escape only.
        // Local work clears its path through combat. Props remain solid in all
        // modes, and must not be mislabeled as creature aggro in diagnostics.
        bool avoidCreatures=!GrindingTravel&&(AvoidAggroOnLongJourney||_goal=="flee"||_mageRetreatUntil>Now);
        if(avoidCreatures&&!me.HasPlayerFlag(PlayerFlags.Ghost))
            threats.AddRange(NearbyCreatures(150).Where(c=>c.IsAlive()&&!SITravelCombatPolicy.Trivial(me.GetLevel(),c.GetLevel(),true)&&c.IsHostileTo(me)&&c.HasReactState(ReactStates.Aggressive)&&!c.IsInEvadeMode()&&
                c.GetGUID()!=_encounter.Target&&!IntendedQuestTravelTarget(c)&&me.CanSeeOrDetect(c,distanceCheck:true))
                .OrderBy(me.GetExactDist).Take(32).Select(c=>new SITravelThreat(V(c.GetPosition()),Math.Clamp(AggroRadius(c)+3,3,80))));
        return threats.ToImmutable();
    }
    bool IntendedQuestTravelTarget(Game.Entities.Creature creature)=>_task?.Kind==SITaskKind.Objective&&_task.Objective!=null&&
        _task.Objective.Sources.Any(s=>s is SIObjectiveSource.Kill or SIObjectiveSource.CreatureDrop)&&
        _task.Creatures.Contains(creature.GetEntry())&&Vector3.Distance(V(creature.GetPosition()),_movementDestination.Point)<=8&&
        (!creature.HasLootRecipient()||creature.IsTappedBy(me))&&TargetRetryReady(creature)&&(me.GetExactDist(creature)>90||SafeToInitiate(creature,out _,questTarget:true));

}
