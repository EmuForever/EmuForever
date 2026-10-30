using Framework.Constants;
using Game.Entities;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    ObjectGuid _localApproachNpc;
    Vector3 _localApproachEnd;
    // The last few yards to an NPC on the same floor are walked on the collision floor directly, without a
    // navmesh request: the mesh is eroded around counters and props that the NPC stands behind.
    bool TryLocalNpcApproach(WorldObject npc,Vector3 endpoint,string reason)
    {
        if(_localApproachNpc==npc.GetGUID()&&Vector3.Distance(endpoint,_localApproachEnd)<.2f&&_movementWalk&&_hasMovementDestination&&
            Vector3.Distance(_movementDestination.Point,_localApproachEnd)<.2f&&Moving)return true;
        var map=me.GetMap();var phase=me.GetPhaseShift();
        var points=SIInteractionApproach.LocalPath(Here,endpoint,p=>FloorNear(p,1,2),
            (a,b)=>CorridorClear(a,b,(from,to)=>map.IsInLineOfSight(phase,from.X,from.Y,from.Z,to.X,to.Y,to.Z,LineOfSightChecks.All,ModelIgnoreFlags.Nothing)));
        if(points==null)return false;
        ClearRoute();_movementDestination=new(me.GetMapId(),endpoint);_hasMovementDestination=true;
        _route=points;_routeEnd=points[^1];_waypoint=1;_movementWalk=true;_movementArrival=.05f;_movementWithin3D=false;_movementFailed=false;
        _movementContext=MovementContext();_journeyReason=reason;_journeyArrival=.05f;_routeWaypoint=null;
        _localApproachNpc=npc.GetGUID();_localApproachEnd=endpoint;
        Dismount();
        LaunchMovementRoute();Status=reason;
        return Moving&&_route.Length>1&&!_movementFailed;
    }
}
