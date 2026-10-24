using Framework.Constants;
using Game.AI.SIMovement;
using Game.Entities;
using Game.Maps;
using Game.Movement;
using System;
using System.Numerics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AI;

// ForeverWorld SI movement port. Native splines replace PublishPosition because
// this core owns relocation, grid membership, movement packets and liquid state.
public sealed partial class SIPlayer
{
    Vector3[] _route=[];
    int _waypoint,_routeMap,_routeSequence;
    Task<SIGroundPath> _planning,_aheadMovement;
    CancellationTokenSource _aheadCancel;
    CancellationTokenSource _pathCancel;
    SIPathPosition _movementDestination;
    // End of the last complete route. A destination moved by slack onto walkable mesh
    // is reached there, not at its original point.
    Vector3? _routeEnd;
    // Set when a complete route ended on the closest walkable point to a destination that is itself
    // off the mesh (an NPC behind a counter, an object on a rock): travel there is finished.
    SIPathPosition? _reachedGoal;Vector3 _reachedPoint;
    bool _hasMovementDestination,_movementFailed,_movementWalk,_casualJump;
    float _movementArrival=4;
    long _movementContext,_nextPlan,_lastMovementTick=-1,_nextCasualJump,_nextCasualPause,_casualPauseUntil;
    long _nextPrefetchPlan;
    double _movementTickSeconds;
    readonly SIProgressWatchdog _travelProgress=new();
    SIHumanMovementStyle _humanMovement;
    SIHumanMovementStyle HumanMovement=>_humanMovement??=new(unchecked((ulong)me.GetGUID().GetCounter()));
    public string NavigationMode {get;private set;}="Idle";
    public string NavigationStatistics=>SIMovementRuntime.Statistics;
    Vector3 Here
    {
        get
        {
            // Native relocation is throttled. Spline position is newer than the
            // grid position during an early handoff; using the latter can rewind.
            if(Moving&&me.GetTransport()==null)
            {var p=me.MoveSpline.ComputePosition();return new(p.X,p.Y,p.Z);}
            return new(me.GetPositionX(),me.GetPositionY(),me.GetPositionZ());
        }
    }
    bool Moving=>!me.MoveSpline.Finalized();
    bool NavigationPending=>_planning!=null;
    long MovementContext()
    {
        var phase=me.GetPhaseShift();int hash=HashCode.Combine(me.GetMapId(),me.GetInstanceId(),phase.Flags,phase.PersonalGuid);
        foreach(var pair in phase.Phases)hash^=HashCode.Combine(pair.Key,pair.Value.Flags);
        foreach(var pair in phase.VisibleMapIds)hash^=HashCode.Combine(pair.Key);
        return hash;
    }
    void SetNavigation(string mode,bool blocked)
    {NavigationMode=mode;((siPlayer)me).NavigationMode=mode;((siPlayer)me).NavigationBlocked=blocked;}
    void ClearRoute()
    {
        CancelAheadMovement();
        _pathCancel?.Cancel();_pathCancel?.Dispose();_pathCancel=null;_planning=null;
        // Recovery attempts belong to the local position, not this particular
        // route. Resetting them here let task/endpoint changes restart pacing.
        _route=[];_waypoint=0;_hasMovementDestination=false;_casualJump=false;_recoveringMovement=false;_travelProgress.Reset();
        _routeEnd=null;
        if(!me.IsInFlight()&&me.GetTransport()==null&&(Moving||me.IsMoving())){me.GetMotionMaster().Clear();me.StopMoving();}
    }
    void CancelTravel(){EndJourney();ClearRoute();SetNavigation("Idle",false);}
    bool SwimmingSurface(Vector3 point)
    {
        me.GetMap().GetLiquidStatus(me.GetPhaseShift(),point.X,point.Y,point.Z,out LiquidData liquid);
        return liquid!=null&&(liquid.type_flags&(LiquidHeaderTypeFlags.Water|LiquidHeaderTypeFlags.Ocean))!=0&&
            (liquid.type_flags&(LiquidHeaderTypeFlags.Magma|LiquidHeaderTypeFlags.Slime|LiquidHeaderTypeFlags.DarkWater))==0&&
            point.Z<=liquid.level+.5f&&liquid.depth_level<liquid.level-.5f;
    }
    bool MovementSuspended=>!me.IsInWorld||me.IsBeingTeleported()||me.IsInFlight()||me.GetTransport()!=null||(_goal=="pause"&&!_siDuelActive)||me.IsCharmed()||
        (!me.IsAlive()&&!me.HasPlayerFlag(PlayerFlags.Ghost))||me.HasUnitState(UnitState.Root|UnitState.LostControl)||
        me.IsNonMeleeSpellCast(false,skipAutorepeat:true)||me.IsAlive()&&(_resting||_recovering||HasUsefulRecoveryAura());
    bool CasualTravelAllowed=>me.IsAlive()&&!me.IsInCombat()&&!_encounter.Active&&_goal!="flee"&&!SwimmingSurface(Here)&&!_resting&&!_recovering;
    bool MovementSight(Vector3 a,Vector3 b)=>me.GetMap().IsInLineOfSight(me.GetPhaseShift(),a.X,a.Y,a.Z+1,b.X,b.Y,b.Z+1,LineOfSightChecks.All,ModelIgnoreFlags.Nothing);
    void MovementFailure(string reason)
    {
        if(TryMovementRecovery(reason))return;
        CancelAheadMovement();
        _pathCancel?.Cancel();_pathCancel?.Dispose();_pathCancel=null;_planning=null;_route=[];
        if(Moving){me.GetMotionMaster().Clear();me.StopMoving();}
        _movementFailed=true;_nextPlan=Now+3000;SetNavigation(reason,true);
    }
    bool AtDestination(SIPathPosition goal,float arrival)=>goal.MapId==me.GetMapId()&&
        (Vector2.Distance(new(Here.X,Here.Y),new(goal.Point.X,goal.Point.Y))<=arrival&&(!goal.HeightKnown||Math.Abs(Here.Z-goal.Point.Z)<=Math.Max(arrival,1.5f))||
        goal.Slack>0&&_routeEnd is Vector3 end&&Vector3.Distance(Here,end)<=1&&
        Vector2.Distance(new(Here.X,Here.Y),new(goal.Point.X,goal.Point.Y))<=goal.Slack+arrival);
    void MaintainNavigation()
    {
        // Travel can also be called by the slower brain; integrate at most once.
        if(!_hasMovementDestination||_movementFailed||Now==_lastMovementTick)return;
        _lastMovementTick=Now;
        if(_movementContext!=MovementContext()){MovementFailure("Map or phase changed during movement");return;}
        if(MovementSuspended)
        {
            _travelProgress.Observe(me.GetMapId(),Here,_movementDestination.Point,Now,_movementArrival,true);
            if(Moving&&!me.IsInFlight()&&me.GetTransport()==null){me.StopMoving();_route=[];CancelAheadMovement();}
            return;
        }
        // Local work (including all grinding) clears imminent hostiles instead
        // of treating their aggro circles as impassable navigation geometry.
        // Selection alone runs here; spell/combat work stays on the brain tick.
        if(TryBeginTravelClearanceFight())return;
        if(_casualJump){if(Moving)return;_casualJump=false;}
        if(_recoveringMovement)
        {
            if(Moving&&Now<_movementRecoveryUntil)return;
            if(Moving)me.StopMoving();
            _recoveringMovement=false;_nextPlan=Now;
            _travelProgress.Observe(me.GetMapId(),Here,_movementDestination.Point,Now,_movementArrival,true);
        }
        var target=_movementDestination.Point;
        if(!_movementDestination.HeightKnown)target.Z=Here.Z;
        var progress=_travelProgress.Observe(me.GetMapId(),Here,target,Now,_movementArrival);
        if(progress is SITravelProgress.Invalid or SITravelProgress.Stalled){MovementFailure("Movement made no measurable progress");return;}
        if(AtDestination(_movementDestination,_movementArrival)){ClearRoute();SetNavigation("Arrived",false);return;}
        if(_planning?.IsCompleted==true)
        {
            SIGroundPath path=null;
            try{path=_planning.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            _planning=null;_pathCancel?.Dispose();_pathCancel=null;
            if(path?.CapacityLimited==true)
            {
                // Backpressure is temporary capacity pressure, not an unreachable
                // NPC/corpse. Keep task ownership and retry with per-player jitter.
                _nextPlan=Now+100+(long)(me.GetGUID().GetCounter()%400);
                SetNavigation("Waiting for navigation capacity",false);return;
            }
            // Already as close as the mesh allows to a destination on a ledge or prop: that is arrival.
            if(path?.Error==UnreachableHere&&Vector2.Distance(new(Here.X,Here.Y),new(target.X,target.Y))<=Math.Max(_movementArrival+8,12)&&
                (!_movementDestination.HeightKnown||Math.Abs(Here.Z-target.Z)<=12)){ReachGoal();return;}
            if(path?.Success!=true){MovementFailure(path?.Error??"Movement request cancelled");return;}
            if(_routeMap!=me.GetMapId()){MovementFailure("Map changed during planning");return;}
            // Pushed or knocked back while the plan was computed: plan again from here.
            if(Vector3.Distance(Here,path.Points[0])>3){_nextPlan=Now;return;}
            if(path.Complete&&RouteLength(path.Points.AsSpan())<1){ReachGoal();return;}
            _routeEnd=path.Complete?path.Points[^1]:null;
            _route=path.Points.ToArray();_waypoint=1;
            LaunchMovementRoute();
            if(_movementFailed||_recoveringMovement)return;
        }
        if(_route.Length>1)
        {
            if(!Moving)
            {
                _route=[];_waypoint=0;
                if(_aheadMovement?.IsCompletedSuccessfully==true&&_aheadMovement.Result.Success&&Vector3.Distance(Here,_aheadMovement.Result.Points[0])<=3)
                {
                    _routeEnd=_aheadMovement.Result.Complete?_aheadMovement.Result.Points[^1]:null;
                    _route=_aheadMovement.Result.Points.ToArray();_waypoint=1;CancelAheadMovement();LaunchMovementRoute();return;
                }
                CancelAheadMovement();
                // A complete route has been walked to its end: that is the closest the mesh gets.
                if(_routeEnd is Vector3 end&&Vector3.Distance(Here,end)<=1.5f){ReachGoal();return;}
            }
            else
            {
                // Native spline is the authoritative follower; use the ported cursor
                // for the collision horizon without re-clicking old waypoints.
                _waypoint=Math.Clamp(me.MoveSpline.CurrentPathIdx()+1,1,_route.Length-1);
                if(ExtendMovementRoute())return;
                TryHumanJump();return;
            }
        }
        if(_route.Length<2&&_planning==null&&Now>=_nextPlan)
        {
            if(CasualTravelAllowed)
            {
                if(Now<_casualPauseUntil)return;
                if(Now>=_nextCasualPause)
                {
                    _nextCasualPause=Now+18000+(long)(HumanMovement.Sample((ulong)_routeSequence+901)*24000);
                    _casualPauseUntil=Now+HumanMovement.PauseMilliseconds(_routeSequence);
                    if(Now<_casualPauseUntil)return;
                }
            }
            // The worker chooses legs itself: direct on the mesh nearby, over the travel graph far away.
            _pathCancel=new();_routeMap=me.GetMapId();
            _planning=SIMovementRuntime.Get(Global.WorldMgr.GetDataPath()).RequestPath(new(_routeMap,Here),
                _movementDestination with{MapId=_routeMap},_pathCancel.Token,
                CasualTravelAllowed?HumanMovement.Seed:0,++_routeSequence,CaptureMovementThreats());
            SetNavigation("Planning movement",false);
        }
    }
    const string UnreachableHere="Destination is not reachable from here";
    static float RouteLength(ReadOnlySpan<Vector3> points){float sum=0;for(int i=1;i<points.Length;++i)sum+=Vector3.Distance(points[i-1],points[i]);return sum;}
    void ReachGoal()
    {
        var goal=_movementDestination;
        ClearRoute();
        _reachedGoal=goal;_reachedPoint=Here;
        SetNavigation("Arrived at the closest reachable point",false);
    }
    void CancelAheadMovement()
    {
        _aheadCancel?.Cancel();_aheadCancel?.Dispose();_aheadCancel=null;_aheadMovement=null;
    }
    bool ExtendMovementRoute()
    {
        float remaining=Vector3.Distance(Here,_route[_waypoint]);
        for(int i=_waypoint+1;i<_route.Length;++i)remaining+=Vector3.Distance(_route[i-1],_route[i]);
        var end=_route[^1];var target=_movementDestination.Point;
        if(!_movementDestination.HeightKnown)target.Z=end.Z;
        float distance=Vector3.Distance(end,target);
        if(distance<=Math.Max(1,_movementArrival))return false;
        if(_aheadMovement?.IsCompletedSuccessfully==true&&_aheadMovement.Result.CapacityLimited)
        {CancelAheadMovement();_nextPrefetchPlan=Now+150+(long)(me.GetGUID().GetCounter()%250);}
        if(_routeEnd!=null)return false; // This leg already ends at the destination.
        if(remaining<40&&_aheadMovement==null&&Now>=_nextPrefetchPlan)
        {
            _aheadCancel=new();
            _aheadMovement=SIMovementRuntime.Get(Global.WorldMgr.GetDataPath()).RequestPath(new(_routeMap,end),
                _movementDestination with{MapId=_routeMap},_aheadCancel.Token,
                CasualTravelAllowed?HumanMovement.Seed:0,++_routeSequence,CaptureMovementThreats());
        }
        float horizon=Math.Clamp(me.GetSpeed(UnitMoveType.Run)*(float)Math.Max(.15,_movementTickSeconds),1.5f,8);
        if(remaining>horizon||_aheadMovement?.IsCompletedSuccessfully!=true||!_aheadMovement.Result.Success)return false;
        var next=_aheadMovement.Result.Points;
        if(Vector3.Distance(_route[^1],next[0])>1||next.Length<2||RouteLength(next.AsSpan())<.5f){CancelAheadMovement();_nextPrefetchPlan=Now+1000;return false;}
        _routeEnd=_aheadMovement.Result.Complete?next[^1]:null;
        // Keep every unconsumed corner through the junction. Starting a new native
        // spline at Here prevents rewinding to the prefetched segment's origin.
        _route=SIMovementRoute.Join(Here,_route,_waypoint,next.ToArray());
        _waypoint=1;CancelAheadMovement();LaunchMovementRoute();return true;
    }
    void LaunchMovementRoute()
    {
        if(_goal=="flee"||_mageRetreatUntil>Now)
        {
            var hazards=RecoveryHazards();
            for(int i=1;i<_route.Length;++i)
                if(!SICorpseRecovery.AvoidsNewThreats(P(_route[i-1]),P(_route[i]),hazards))
                {MovementFailure("Escape path enters a new threat");return;}
        }
        bool swim=false;
        for(int i=1;i<_route.Length;++i)
            if(SwimmingSurface(_route[i]))
            {
                me.GetMap().GetLiquidStatus(me.GetPhaseShift(),_route[i].X,_route[i].Y,_route[i].Z,out LiquidData liquid);
                _route[i].Z=Math.Clamp(_route[i].Z,liquid.depth_level+.25f,Math.Max(liquid.depth_level+.25f,liquid.level-.5f));swim=true;
            }
        _route=SIMovementRoute.Prepare(Here,_route);_waypoint=1;
        if(_route.Length<2)
        {
            if(AtDestination(_movementDestination,_movementArrival)){ClearRoute();SetNavigation("Arrived",false);}
            else if(_routeEnd!=null)ReachGoal();
            else{_route=[];_nextPlan=Now+250;}
            return;
        }
        me.SetStandState(UnitStandStateType.Stand);me.GetMotionMaster().Clear();
        var init=new MoveSplineInit(me);init.MovebyPath(_route);init.SetUncompressed();init.SetSmoothGroundPath(false);init.SetWalk(_movementWalk);
        if(swim){if(me.IsMounted())me.RemoveAurasByType(AuraType.Mounted);init.SetVelocity(me.GetSpeed(UnitMoveType.Swim));}
        if(init.Launch()<=0){MovementFailure("Movement spline rejected");return;}
        SetNavigation("Moving",false);
    }
    int _jumpSequence;
    void TryHumanJump()
    {
        if(_nextCasualJump==0)_nextCasualJump=Now+HumanMovement.JumpInterval(0);
        if(!CasualTravelAllowed||Now<_nextCasualJump||_waypoint>=_route.Length||Vector3.Distance(Here,_route[_waypoint])<8||Math.Abs(Here.Z-_route[_waypoint].Z)>.8f)return;
        var landing=Vector3.Lerp(Here,_route[_waypoint],Math.Min(1,me.GetSpeed(UnitMoveType.Run)*SIHumanMovementStyle.JumpDuration/Vector3.Distance(Here,_route[_waypoint])));
        if(!MovementSight(Here,landing)||!MovementSight(Here,Here+new Vector3(0,0,3.6f)))return;
        var previous=Here;
        for(int sample=1;sample<=8;++sample)
        {
            float time=SIHumanMovementStyle.JumpDuration*sample/8;
            var point=Vector3.Lerp(Here,landing,sample/8f)+new Vector3(0,0,SIHumanMovementStyle.JumpHeight(time));
            if(!MovementSight(previous,point))return;
            previous=point;
        }
        _nextCasualJump=Now+HumanMovement.JumpInterval(++_jumpSequence);
        me.GetMotionMaster().MoveJump(P(landing),me.GetSpeed(UnitMoveType.Run),(Speed)SIHumanMovementStyle.JumpVelocity);
        _route=[];CancelAheadMovement();_casualJump=true;
    }
    SIBehaviorResult Travel(Position destination,string reason,float arrival=4,bool walk=false,float destinationTolerance=6,int? mapId=null,bool heightKnown=true,bool rememberFailure=true,float slack=0)
    {
        if(destination==null)return SIBehaviorResult.Failure;
        var goal=new SIPathPosition(mapId??(destination is WorldLocation location?location.GetMapId():me.GetMapId()),V(destination),heightKnown,slack);
        if(!goal.Valid||goal.MapId!=me.GetMapId()){SetNavigation("Destination is on another map",true);return SIBehaviorResult.Failure;}
        if(AtDestination(goal,arrival)){CancelTravel();SetNavigation("Arrived",false);return SIBehaviorResult.Success;}
        if(_reachedGoal is SIPathPosition reached&&reached.MapId==goal.MapId&&Vector3.Distance(reached.Point,goal.Point)<=Math.Max(1,destinationTolerance*.5f))
        {
            if(Vector3.Distance(Here,_reachedPoint)<=2){SetNavigation("Arrived at the closest reachable point",false);return SIBehaviorResult.Success;}
            _reachedGoal=null;
        }
        if(rememberFailure&&IsLearnedRouteBlocked(destination))return SIBehaviorResult.Failure;
        float changed=heightKnown?Vector3.Distance(goal.Point,_movementDestination.Point):Vector2.Distance(new(goal.Point.X,goal.Point.Y),new(_movementDestination.Point.X,_movementDestination.Point.Y));
        if(!_hasMovementDestination||changed>destinationTolerance||goal.HeightKnown!=_movementDestination.HeightKnown)
        {
            ClearRoute();_movementDestination=goal;_hasMovementDestination=true;_movementFailed=false;_movementContext=MovementContext();_nextPlan=0;
            _journeyReason=reason;_journeyArrival=arrival;
            _longJourney=me.IsAlive()&&!_encounter.Active&&(_goal is "quest" or "grind")&&Vector3.Distance(Here,goal.Point)>1500;
        }
        _journeyRememberFailure=rememberFailure;
        _movementArrival=arrival;_movementWalk=walk;
        if(_movementFailed)
        {
            // NPC approaches have their own task retry. A bad stand-off point
            // must not blacklist the NPC's whole neighborhood for five minutes.
            if(rememberFailure)RememberBlockedRoute(destination,NavigationMode);
            Report("navigation_failed",NavigationMode+"; "+reason);return SIBehaviorResult.Failure;
        }
        if(MountForTravel())return SIBehaviorResult.Running;
        MaintainNavigation();Status=reason;
        return _movementFailed?SIBehaviorResult.Failure:SIBehaviorResult.Running;
    }
    SIBehaviorResult MoveWithin(WorldObject target,float range,string reason,bool npcInteraction=false)
    {
        // Native GO range uses the display's rotated interaction box, not its
        // center. Large solid props are usable from outside their collision model.
        if(target is GameObject obj&&me.GetGameObjectIfCanInteractWith(obj.GetGUID())!=null)
        {
            CancelTravel();me.SetSelection(obj.GetGUID());
            if(!me.HasUnitState(UnitState.CannotTurn))me.SetFacingToObject(obj);
            return SIBehaviorResult.Success;
        }
        float distance=me.GetExactDist(target);float ready=npcInteraction?Math.Min(range,2):range;
        bool sight=me.IsWithinLOSInMap(target);
        bool native=npcInteraction&&target is Creature&&me.GetNPCIfCanInteractWith(target.GetGUID(),NPCFlags1.None,NPCFlags2.None)!=null;
        if(native||SIInteractionApproach.Ready(distance,ready,sight,native))
        {
            CancelTravel();
            if(!me.HasUnitState(UnitState.CannotTurn))me.SetFacingToObject(target);
            if(npcInteraction)me.SetSelection(target.GetGUID());return SIBehaviorResult.Success;
        }
        // An offset at the NPC's height can be outside a tower/platform. Follow
        // its real floor position first; stand off only when close on that floor.
        var endpoint=npcInteraction?SIInteractionApproach.Endpoint(Here,V(target.GetPosition()),distance,sight):V(target.GetPosition());
        var result=Travel(P(endpoint),reason,npcInteraction?.25f:sight?Math.Max(1.5f,range-1.5f):.5f,npcInteraction&&distance<=4.5f,npcInteraction?.25f:6,
            heightKnown:true,rememberFailure:!npcInteraction);
        return result==SIBehaviorResult.Failure?result:SIBehaviorResult.Running;
    }
}
