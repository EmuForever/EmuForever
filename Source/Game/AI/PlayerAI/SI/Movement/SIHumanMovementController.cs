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
    int _travelGraphRevision;
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
    // Arrival measured as straight-line 3D distance: travel that ends in an action (attack, loot, talk) arrives
    // exactly when the action's own range check passes. Other travel arrives horizontally with a height allowance.
    bool _movementWithin3D;
    long _movementContext,_nextPlan,_lastMovementTick=-1,_nextCasualJump;
    // When the current uninterrupted run began (0 while standing). Casual jumps need a settled run.
    long _runningSince;
    const long JumpSettleMilliseconds=8000;
    long _nextPrefetchPlan;
    double _movementTickSeconds;
    int _jumpSequence;
    readonly SIProgressWatchdog _travelProgress=new();
    SIHumanMovementStyle _humanMovement;
    SIHumanMovementStyle HumanMovement=>_humanMovement??=new(unchecked((ulong)me.GetGUID().GetCounter()));
    public string NavigationMode {get;private set;}="Idle";
    // Navmesh detail heights can sit up to about a yard off the collision floor that the core and
    // clients use. Route corners are moved onto that floor when it is found within this window.
    const float FloorSearchAbove=1,FloorSearchBelow=1.5f;
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
    {NavigationMode=mode;me.NavigationMode=mode;me.NavigationBlocked=blocked;}
    void CancelAheadMovement()
    {
        _aheadCancel?.Cancel();_aheadCancel?.Dispose();_aheadCancel=null;_aheadMovement=null;
    }
    void CancelPlanning()
    {
        CancelAheadMovement();
        _pathCancel?.Cancel();_pathCancel?.Dispose();_pathCancel=null;_planning=null;
    }
    // Stops the travel spline and forgets the published route. Taxi and transport movement belong to the core.
    void HaltSpline(bool clearMotion=true)
    {
        _route=[];CancelAheadMovement();_runningSince=0;
        if(me.IsInFlight()||me.GetTransport()!=null||!Moving&&!me.IsMoving())return;
        if(clearMotion)me.GetMotionMaster().Clear();
        me.StopMoving();
    }
    void ClearRoute()
    {
        CancelPlanning();
        // Recovery attempts belong to the local position, not this particular
        // route. Resetting them here let task/endpoint changes restart pacing.
        _waypoint=0;_hasMovementDestination=false;_casualJump=false;_recoveringMovement=false;_travelProgress.Reset();
        _routeEnd=null;
        HaltSpline();
    }
    void CancelTravel(){EndJourney();ClearRoute();SetNavigation("Idle",false);}
    // Keeps the destination but drops the current route, so the next tick plans again from here.
    void ReplanRoute(string mode)
    {
        ClearRoute();_hasMovementDestination=true;_routeWaypoint=null;_nextPlan=Now;
        if(mode!=null)SetNavigation(mode,false);
    }
    bool SwimmingSurface(Vector3 point)
    {
        me.GetMap().GetLiquidStatus(me.GetPhaseShift(),point.X,point.Y,point.Z,out LiquidData liquid);
        return liquid!=null&&(liquid.type_flags&(LiquidHeaderTypeFlags.Water|LiquidHeaderTypeFlags.Ocean))!=0&&
            (liquid.type_flags&(LiquidHeaderTypeFlags.Magma|LiquidHeaderTypeFlags.Slime|LiquidHeaderTypeFlags.DarkWater))==0&&
            point.Z<=liquid.level+.5f&&liquid.depth_level<liquid.level-.5f;
    }
    // Collision floor (terrain, models and spawned objects) at a point, or NaN when none is close to its height.
    float FloorNear(Vector3 point,float above=FloorSearchAbove,float below=FloorSearchBelow)
    {
        float floor=me.GetMap().GetHeight(me.GetPhaseShift(),point.X,point.Y,point.Z+above,true,above+below);
        return float.IsFinite(floor)&&floor>MapConst.InvalidHeight&&floor>=point.Z-below-.01f&&floor<=point.Z+above+.01f?floor:float.NaN;
    }
    void Dismount(){if(me.IsMounted())me.RemoveAurasByType(AuraType.Mounted);}
    void StandUp(){if(me.GetStandState()!=UnitStandStateType.Stand)me.SetStandState(UnitStandStateType.Stand);}
    bool MovementSuspended=>!me.IsInWorld||me.IsBeingTeleported()||me.IsInFlight()||me.GetTransport()!=null||(_goal=="pause"&&!_siDuelActive)||me.IsCharmed()||
        (!me.IsAlive()&&!me.HasPlayerFlag(PlayerFlags.Ghost))||me.HasUnitState(UnitState.Root|UnitState.LostControl)||
        WaitingForTravelMount()||me.IsNonMeleeSpellCast(false,skipAutorepeat:true)||me.IsAlive()&&
        (!RecoverySupplyTrip&&(_resting||_recovering)||HasUsefulRecoveryAura());
    bool CasualTravelAllowed=>me.IsAlive()&&!me.IsInCombat()&&!_encounter.Active&&_goal!="flee"&&!SwimmingSurface(Here)&&!_resting&&!_recovering;
    bool MovementSight(Vector3 a,Vector3 b)=>me.GetMap().IsInLineOfSight(me.GetPhaseShift(),a.X,a.Y,a.Z+1,b.X,b.Y,b.Z+1,LineOfSightChecks.All,ModelIgnoreFlags.Nothing);
    bool WalkableMovementStep(Vector3 a,Vector3 b,bool first=false)=>
        (first?SIWalkingSurface.SafeFirstStep(a,b):SIWalkingSurface.SafeStep(a,b))||(SwimmingSurface(a)&&SwimmingSurface(b));
    void MovementFailure(string reason)
    {
        if(TryMovementRecovery(reason))return;
        CancelPlanning();HaltSpline();
        _movementFailed=true;_nextPlan=Now+3000;SetNavigation(reason,true);
    }
    bool AtDestination(SIPathPosition goal,float arrival,bool within3D=false)=>goal.MapId==me.GetMapId()&&
        (within3D&&goal.HeightKnown?Vector3.Distance(Here,goal.Point)<=arrival:
        Vector2.Distance(new(Here.X,Here.Y),new(goal.Point.X,goal.Point.Y))<=arrival&&(!goal.HeightKnown||Math.Abs(Here.Z-goal.Point.Z)<=Math.Max(arrival,1.5f))||
        goal.Slack>0&&_routeEnd is Vector3 end&&Vector3.Distance(Here,end)<=1&&
        Vector2.Distance(new(Here.X,Here.Y),new(goal.Point.X,goal.Point.Y))<=goal.Slack+arrival);
    void MaintainNavigation()
    {
        // Travel can also be called by the slower brain; integrate at most once.
        if(!_hasMovementDestination||_movementFailed||Now==_lastMovementTick)return;
        _lastMovementTick=Now;
        if(_movementContext!=MovementContext()){MovementFailure("Map or phase changed during movement");return;}
        int graphRevision=SITravelGraphs.Revision(me.GetMapId());
        if(_travelGraphRevision!=graphRevision)
        {
            // GM bans and point moves take effect on an active trip too. Replan from here
            // without learning a false terrain failure for the destination.
            _travelGraphRevision=graphRevision;ReplanRoute(null);_nextPlan=0;
        }
        if(MovementSuspended)
        {
            _travelProgress.Observe(me.GetMapId(),Here,_movementDestination.Point,Now,_movementArrival,true);
            if(Moving)HaltSpline(clearMotion:false);
            return;
        }
        // Local work (including all grinding) clears imminent hostiles instead
        // of treating their aggro circles as impassable navigation geometry.
        // Selection alone runs here; spell/combat work stays on the brain tick.
        if(StopForDynamicObstacle())return;
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
        if(AtDestination(_movementDestination,_movementArrival,_movementWithin3D)){ArriveHere();return;}
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
            if(path?.Error==SIGroundPath.UnreachableError&&Vector2.Distance(new(Here.X,Here.Y),new(target.X,target.Y))<=Math.Max(_movementArrival+8,12)&&
                (!_movementDestination.HeightKnown||Math.Abs(Here.Z-target.Z)<=12)){ReachGoal();return;}
            if(path?.Success!=true){MovementFailure(path?.Error??"Movement request cancelled");return;}
            if(_routeMap!=me.GetMapId()){MovementFailure("Map changed during planning");return;}
            // Pushed or knocked back while the plan was computed: plan again from here.
            if(Vector3.Distance(Here,path.Points[0])>3){_nextPlan=Now;return;}
            if(path.Complete&&RouteLength(path.Points.AsSpan())<1){ReachGoal();return;}
            AdoptRoute(path);
            LaunchMovementRoute();
            if(_movementFailed||_recoveringMovement)return;
        }
        if(_route.Length>1)
        {
            if(Moving)
            {
                // Native spline is the authoritative follower; use the ported cursor
                // for the collision horizon without re-clicking old waypoints.
                _waypoint=Math.Clamp(me.MoveSpline.CurrentPathIdx()+1,1,_route.Length-1);
                if(ExtendMovementRoute())return;
                TryHumanJump();return;
            }
            _route=[];_waypoint=0;
            if(_aheadMovement?.IsCompletedSuccessfully==true&&_aheadMovement.Result.Success&&Vector3.Distance(Here,_aheadMovement.Result.Points[0])<=3)
            {
                AdoptRoute(_aheadMovement.Result);CancelAheadMovement();LaunchMovementRoute();return;
            }
            CancelAheadMovement();
            // A complete route has been walked to its end: that is the closest the mesh gets.
            if(_routeEnd is Vector3 end&&Vector3.Distance(Here,end)<=1.5f){ReachGoal();return;}
        }
        if(_route.Length<2&&_planning==null&&Now>=_nextPlan)
        {
            // The worker chooses legs itself: direct on the mesh nearby, over the travel graph far away.
            _pathCancel=new();_routeMap=me.GetMapId();
            _planning=RequestRoute(Here,_pathCancel.Token);
            SetNavigation("Planning movement",false);
        }
    }
    Task<SIGroundPath> RequestRoute(Vector3 from,CancellationToken cancel)=>
        SIMovementRuntime.Get(Global.WorldMgr.GetDataPath()).RequestPath(new(_routeMap,from),_movementDestination with{MapId=_routeMap},cancel,
            CasualTravelAllowed?HumanMovement.Seed:0,++_routeSequence,CaptureMovementThreats(),RouteHints());
    void AdoptRoute(SIGroundPath path)
    {
        _routeEnd=path.Complete?path.Points[^1]:null;_routeWaypoint=path.Waypoint;
        _route=path.Points.ToArray();_waypoint=1;
    }
    static float RouteLength(ReadOnlySpan<Vector3> points){float sum=0;for(int i=1;i<points.Length;++i)sum+=Vector3.Distance(points[i-1],points[i]);return sum;}
    // Walking distance left on the published route from the current position.
    float RemainingRoute()=>_waypoint<_route.Length?Vector3.Distance(Here,_route[_waypoint])+RouteLength(_route.AsSpan(_waypoint)):0;
    void ArriveHere(){ClearRoute();SetNavigation("Arrived",false);}
    void ReachGoal()
    {
        var goal=_movementDestination;
        ClearRoute();
        _reachedGoal=goal;_reachedPoint=Here;
        SetNavigation("Arrived at the closest reachable point",false);
    }
    bool ExtendMovementRoute()
    {
        float remaining=RemainingRoute();
        var end=_route[^1];var target=_movementDestination.Point;
        if(!_movementDestination.HeightKnown)target.Z=end.Z;
        if(Vector3.Distance(end,target)<=Math.Max(1,_movementArrival))return false;
        if(_aheadMovement?.IsCompletedSuccessfully==true&&_aheadMovement.Result.CapacityLimited)
        {CancelAheadMovement();_nextPrefetchPlan=Now+150+(long)(me.GetGUID().GetCounter()%250);}
        if(_routeEnd!=null)return false; // This leg already ends at the destination.
        float speed=me.GetSpeed(_movementWalk?UnitMoveType.Walk:me.IsInWater()?UnitMoveType.Swim:UnitMoveType.Run);
        if(remaining<=SIMovementRoute.PrefetchDistance(speed)&&_aheadMovement==null&&Now>=_nextPrefetchPlan)
        {
            _aheadCancel=new();
            _aheadMovement=RequestRoute(end,_aheadCancel.Token);
        }
        // Join well before the old spline decelerates at its endpoint. The
        // unconsumed corners remain in the new spline, so this cannot cut a turn.
        float horizon=SIMovementRoute.HandoffDistance(speed,_movementTickSeconds);
        if(remaining>horizon||_aheadMovement?.IsCompletedSuccessfully!=true||!_aheadMovement.Result.Success)return false;
        var next=_aheadMovement.Result.Points;
        if(Vector3.Distance(_route[^1],next[0])>1||next.Length<2||RouteLength(next.AsSpan())<.5f){CancelAheadMovement();_nextPrefetchPlan=Now+1000;return false;}
        var ahead=_aheadMovement.Result;
        // Keep every unconsumed corner through the junction. Starting a new native
        // spline at Here prevents rewinding to the prefetched segment's origin.
        var joined=SIMovementRoute.Join(Here,_route,_waypoint,next.ToArray());
        AdoptRoute(ahead);_route=joined;
        CancelAheadMovement();LaunchMovementRoute();return true;
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
        bool swim=SwimmingSurface(Here);
        for(int i=1;i<_route.Length;++i)
            if(SwimmingSurface(_route[i]))
            {
                me.GetMap().GetLiquidStatus(me.GetPhaseShift(),_route[i].X,_route[i].Y,_route[i].Z,out LiquidData liquid);
                _route[i].Z=Math.Clamp(_route[i].Z,liquid.depth_level+.25f,Math.Max(liquid.depth_level+.25f,liquid.level-.5f));
            }
        // SkipPassed only tests straight steps from the current position.
        _route=SIMovementRoute.SkipPassed(SIMovementRoute.Prepare(Here,_route),(a,b)=>WalkableMovementStep(a,b,first:true)&&MovementSight(a,b));_waypoint=1;
        if(!SocialRouteAllowed(_route)) { MovementFailure("Social route leaves the selected main city"); return; }
        for(int i=1;i<_route.Length;++i)
            if(!WalkableMovementStep(_route[i-1],_route[i],first:i==1))
            {MovementFailure("Route exceeds player walking slope or step height");return;}
        if(_route.Length<2)
        {
            if(AtDestination(_movementDestination,_movementArrival,_movementWithin3D))ArriveHere();
            else if(_routeEnd!=null)ReachGoal();
            else{_route=[];_nextPlan=Now+250;}
            return;
        }
        PlaceRouteOnFloor();
        StandUp();me.GetMotionMaster().Clear();
        if(StopForDynamicObstacle(launching:true))return;
        var init=new MoveSplineInit(me);init.MovebyPath(_route);init.SetUncompressed();init.SetSmoothGroundPath(false);init.SetWalk(_movementWalk);
        if(swim){Dismount();init.SetVelocity(me.GetSpeed(UnitMoveType.Swim),UnitMoveType.Swim);}
        bool resumed=!Moving;
        if(init.Launch()<=0){MovementFailure("Movement spline rejected");return;}
        if(resumed||_runningSince==0)_runningSince=Now;
        SetNavigation("Moving",false);
    }
    // Splines are sent without client ground smoothing, so observers draw exactly these heights. Corners left at
    // navmesh height made bots float or sink while walking, settle on arrival at a height the server did not
    // share, and jump back when the next spline started. Each corner moves onto the collision floor when that
    // keeps both of its walking steps within player limits and the edges long enough for a native spline.
    void PlaceRouteOnFloor()
    {
        for(int i=1;i<_route.Length;++i)
        {
            var point=_route[i];
            if(SwimmingSurface(point))continue;
            float floor=FloorNear(point);
            if(float.IsNaN(floor)||Math.Abs(floor-point.Z)<.02f)continue;
            point.Z=floor;
            if(!(i==1?SIWalkingSurface.SafeFirstStep(_route[0],point):SIWalkingSurface.SafeStep(_route[i-1],point))||Vector3.Distance(_route[i-1],point)<.11f)continue;
            if(i+1<_route.Length&&(!SIWalkingSurface.SafeStep(point,_route[i+1])||Vector3.Distance(point,_route[i+1])<.11f))continue;
            _route[i]=point;
        }
    }
    void TryHumanJump()
    {
        if(_movementWalk)return;
        if(_nextCasualJump==0)_nextCasualJump=Now+HumanMovement.JumpInterval(0);
        // A stop (a cast, eating, a pull, an NPC) holds the timer: never hop the moment the bot sets off again.
        if(!CasualTravelAllowed||Now<_nextCasualJump||_runningSince==0||Now-_runningSince<JumpSettleMilliseconds||_waypoint>=_route.Length)return;
        var corner=_route[_waypoint];float reach=Vector3.Distance(Here,corner);
        // Never hop into an arrival: the last stretch of a route belongs to the approach.
        if(reach<8||Math.Abs(Here.Z-corner.Z)>.8f||RemainingRoute()<20||_routeEnd is Vector3 end&&Vector3.Distance(Here,end)<20)return;
        var landing=Vector3.Lerp(Here,corner,Math.Min(1,me.GetSpeed(UnitMoveType.Run)*SIHumanMovementStyle.JumpDuration/reach));
        // Land on the actual floor; a straight line between corners can be above or below it.
        float floor=FloorNear(landing,.8f,.8f);
        if(float.IsNaN(floor))return;
        landing.Z=floor;
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
        _route=[];CancelAheadMovement();_casualJump=true;_runningSince=0;
    }
    SIBehaviorResult Travel(Position destination,string reason,float arrival=4,bool walk=false,float destinationTolerance=6,int? mapId=null,bool heightKnown=true,bool rememberFailure=true,float slack=0,bool within3D=false)
    {
        if(destination==null)return SIBehaviorResult.Failure;
        var goal=new SIPathPosition(mapId??(destination is WorldLocation location?location.GetMapId():me.GetMapId()),V(destination),heightKnown,slack);
        if(!goal.Valid||goal.MapId!=me.GetMapId()){SetNavigation("Destination is on another map",true);return SIBehaviorResult.Failure;}
        if(AtDestination(goal,arrival,within3D)){CancelTravel();SetNavigation("Arrived",false);return SIBehaviorResult.Success;}
        if(!_boardingFlight)
        {
            CommitJourney(goal.Point);
            if(TryJourneyFlight(goal,reason,walk,out var flight))return flight;
        }
        if(_reachedGoal is SIPathPosition reached&&reached.MapId==goal.MapId&&Vector3.Distance(reached.Point,goal.Point)<=Math.Max(1,destinationTolerance*.5f))
        {
            if(Vector3.Distance(Here,_reachedPoint)<=2){SetNavigation("Arrived at the closest reachable point",false);return SIBehaviorResult.Success;}
            _reachedGoal=null;
        }
        if(rememberFailure&&IsLearnedRouteBlocked(destination))return SIBehaviorResult.Failure;
        float changed=heightKnown?Vector3.Distance(goal.Point,_movementDestination.Point):Vector2.Distance(new(goal.Point.X,goal.Point.Y),new(_movementDestination.Point.X,_movementDestination.Point.Y));
        if(!_hasMovementDestination||changed>destinationTolerance||goal.HeightKnown!=_movementDestination.HeightKnown)
        {
            ClearRoute();_movementDestination=goal;_hasMovementDestination=true;_routeWaypoint=null;_movementFailed=false;_movementContext=MovementContext();_nextPlan=0;
            _journeyReason=reason;_journeyArrival=arrival;
            _longJourney=me.IsAlive()&&!_encounter.Active&&WorkingGoal&&Vector3.Distance(Here,goal.Point)>1500;
        }
        _journeyRememberFailure=rememberFailure;
        _movementArrival=arrival;_movementWithin3D=within3D;
        if(_movementWalk!=walk&&Moving&&!_casualJump){HaltSpline();_nextPlan=Now;}
        _movementWalk=walk;
        if(walk)Dismount();
        if(_movementFailed)
        {
            // NPC approaches have their own task retry. A bad stand-off point
            // must not blacklist the NPC's whole neighborhood for five minutes.
            if(rememberFailure)RememberBlockedRoute(destination,NavigationMode);
            Report("navigation_failed",NavigationMode+"; "+reason);return SIBehaviorResult.Failure;
        }
        if(!walk&&Vector3.Distance(Here,goal.Point)>40&&MountForTravel())return SIBehaviorResult.Running;
        MaintainNavigation();Status=reason;
        return _movementFailed?SIBehaviorResult.Failure:SIBehaviorResult.Running;
    }
    // Turning is a facing spline sent to every observer and it ends a running spline, so only turn when needed.
    void FaceTarget(WorldObject target,bool select=true)
    {
        if(select)me.SetSelection(target.GetGUID());
        if(!me.HasUnitState(UnitState.CannotTurn)&&(Moving||!me.HasInArc(.35f,target)))me.SetFacingToObject(target);
    }
    // Walks into attack range of a unit: arriving is being within range in 3D, as the attack checks it.
    // Out of sight it walks onto the target's spot, around whatever blocks the view.
    SIBehaviorResult ApproachInRange(Unit target,float range,bool sight,string reason)=>sight
        ?Travel(target.GetPosition(),reason,Math.Max(.5f,range-.25f),destinationTolerance:1,within3D:true)
        :Travel(target.GetPosition(),reason,.5f,destinationTolerance:1);
    SIBehaviorResult MoveWithin(WorldObject target,float range,string reason,bool npcInteraction=false)
    {
        // Native GO range uses the display's rotated interaction box, not its
        // center. Large solid props are usable from outside their collision model.
        if(target is GameObject obj&&me.GetGameObjectIfCanInteractWith(obj.GetGUID())!=null)
        {
            CancelTravel();FaceTarget(obj);
            return SIBehaviorResult.Success;
        }
        float distance=me.GetExactDist(target);float ready=npcInteraction?Math.Min(range,SIInteractionApproach.NpcRange):range;
        bool sight=me.IsWithinLOSInMap(target);
        bool native=npcInteraction&&target is Creature&&me.GetNPCIfCanInteractWith(target.GetGUID(),NPCFlags1.None,NPCFlags2.None)!=null;
        if(SIInteractionApproach.Ready(distance,ready,sight,native))
        {
            CancelTravel();FaceTarget(target,select:npcInteraction);
            return SIBehaviorResult.Success;
        }
        // The last yards to a visible NPC on the same floor are walked on the collision floor, to a stand-off point
        // that is within reach in 3D.
        if(npcInteraction&&native&&sight&&distance<=SIInteractionApproach.WalkRange&&Math.Abs(Here.Z-target.GetPositionZ())<=1.5f&&
            TryLocalNpcApproach(target,SIInteractionApproach.Endpoint(Here,V(target.GetPosition())),reason))
            return SIBehaviorResult.Running;
        // In sight, arrival is the same 3D range the readiness check uses. Out of sight the bot walks onto the
        // target's spot (an NPC's own position may be on a tower or platform), around whatever hides it.
        var result=sight
            ?Travel(target.GetPosition(),reason,Math.Max(.5f,ready-.1f),npcInteraction&&distance<=SIInteractionApproach.WalkRange,npcInteraction?.1f:1,
                rememberFailure:!npcInteraction,within3D:true)
            :Travel(target.GetPosition(),reason,npcInteraction?.1f:.5f,false,npcInteraction?.1f:6,rememberFailure:!npcInteraction);
        // The route cannot get closer (an NPC behind a counter or on a prop): the core's own interaction range decides.
        if(result==SIBehaviorResult.Success&&native){CancelTravel();FaceTarget(target);return SIBehaviorResult.Success;}
        return result==SIBehaviorResult.Failure?result:SIBehaviorResult.Running;
    }
}
