using Framework.Constants;
using Game.Collision;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    static readonly float[] BodyHeights=[.35f,1,1.8f];

    // Knee, waist and head rays along the left, centre and right of a one-yard-wide walker.
    static bool CorridorClear(Vector3 from,Vector3 to,Func<Vector3,Vector3,bool> clear)
    {
        var delta=to-from;var side=new Vector3(-delta.Y,delta.X,0);
        if(side.LengthSquared()>.001f)side=Vector3.Normalize(side)*.5f;
        foreach(float height in BodyHeights)
            for(int n=-1;n<=1;++n)
            {
                var offset=side*n+new Vector3(0,0,height);
                if(!clear(from+offset,to+offset))return false;
            }
        return true;
    }

    // Whether any stretch of the published route within the next `distance` yards satisfies `test`. A launching
    // route starts at its first corner; a running spline at its next unconsumed corner.
    bool AnyRouteSegmentAhead(float distance,bool launching,Func<Vector3,Vector3,bool> test)
    {
        var previous=Here;float remaining=distance;
        int first=launching?1:Moving?Math.Clamp(me.MoveSpline.CurrentPathIdx()+1,1,Math.Max(1,_route.Length-1)):_waypoint;
        for(int i=first;i<_route.Length&&remaining>0;++i)
        {
            var next=_route[i];float length=Vector3.Distance(previous,next);
            if(length>remaining)next=Vector3.Lerp(previous,next,remaining/length);
            if(test(previous,next))return true;
            remaining-=length;previous=next;
        }
        return false;
    }

    ImmutableArray<GameObjectCollisionSnapshot> CaptureMovementObstacles()
    {
        var result=ImmutableArray.CreateBuilder<GameObjectCollisionSnapshot>();
        foreach(var obj in NearbyObjects(200))
            if(obj.m_model?.Snapshot(me.GetPhaseShift()) is GameObjectCollisionSnapshot snapshot)result.Add(snapshot);
        return result.ToImmutable();
    }

    long _nextDynamicCollisionCheck;
    bool StopForDynamicObstacle(bool launching=false)
    {
        if(_route.Length<2||!launching&&(!Moving||Now<_nextDynamicCollisionCheck))return false;
        _nextDynamicCollisionCheck=Now+100;
        var map=me.GetMap();var phase=me.GetPhaseShift();
        bool blocked=AnyRouteSegmentAhead(Math.Max(6,me.GetSpeed(UnitMoveType.Run)*.75f+2),launching,(a,b)=>
            !CorridorClear(a,b,(from,to)=>!map.GetObjectHitPos(phase,from.X,from.Y,from.Z,to.X,to.Y,to.Z,out _,out _,out _,0)));
        if(!blocked)return false;
        ReplanRoute("Routing around a spawned gameobject");
        return true;
    }
}
