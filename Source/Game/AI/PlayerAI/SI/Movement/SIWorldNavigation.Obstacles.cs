using Game.Collision;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Game.AI.SIMovement;

public sealed partial class SIWorldNavigation
{
    public SIGroundPath Find(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,
        ImmutableArray<SITravelThreat> threats=default,SIRouteHints hints=default)
    {
        var route=FindGround(from,to,cancelled,threats,hints);
        if(!route.Success&&SIRouteRetry.Retryable(route.Error))
            route=AlternateGround(from,to,cancelled,threats,hints)??route;
        if(route.Success && !route.Partial && hints.VariationSeed != 0)
            route=VaryRoute(from,route,cancelled,hints.VariationSeed);
        if(!route.Success||hints.Obstacles.IsDefaultOrEmpty)return route;
        var points=route.Points.ToArray();
        for(int attempt=0;attempt<4;++attempt)
        {
            if(cancelled())throw new OperationCanceledException();
            if(!FirstCollision(points,hints.Obstacles,out int edge,out var obstacle))return route with{Points=points.ToImmutableArray()};
            var detour=ObjectDetour(from.MapId,points,edge,obstacle,hints.Obstacles,cancelled,route.Complete?to:default);
            if(detour==null)
                return AlternateGround(from,to,cancelled,threats,hints,points[Math.Max(0,edge-1)],
                    Math.Max(obstacle.Bounds.Hi.X-obstacle.Bounds.Lo.X,obstacle.Bounds.Hi.Y-obstacle.Bounds.Lo.Y)*.5f+4)
                    ??SIGroundPath.Failed("No walkable route around a solid spawned gameobject");
            points=detour;
        }
        if(!FirstCollision(points,hints.Obstacles,out int blocked,out _))return route with{Points=points.ToImmutableArray()};
        // Crowded areas can be continued in another leg, but never publish an
        // unchecked remainder merely because the local detour budget was used.
        return blocked>1?route with{Points=points.Take(blocked).ToImmutableArray(),Complete=false}:
            SIGroundPath.Failed("Spawned gameobjects block the walking corridor");
    }

    // A failed corridor is not a failed destination. Force a different approach
    // through side waypoints, rather than requesting the same funnel again.
    internal SIGroundPath AlternateGround(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,
        ImmutableArray<SITravelThreat> threats,SIRouteHints hints,Vector3? obstruction=null,float clearance=8)
    {
        if(!from.Valid||!to.Valid||from.MapId!=to.MapId)return null;
        var direction=to.Point-from.Point;direction.Z=0;float distance=direction.Length();
        if(distance<3)return null;
        var side=new Vector3(-direction.Y,direction.X,0)/distance;
        var center=obstruction??Vector3.Lerp(from.Point,to.Point,Math.Min(.5f,35/distance));
        float width=Math.Clamp(clearance,8,35);
        float sign=(hints.VariationSeed&1)==0?1:-1;
        foreach(float offset in new[]{width*sign,-width*sign,(width+12)*sign,-(width+12)*sign})
        {
            if(cancelled())throw new OperationCanceledException();
            _planner.Filter.Use(threats,from.Point);
            var first=_planner.Plan(from,new(from.MapId,center+side*offset),cancelled,_max);
            if(!first.Success||!first.Complete||first.Partial)continue;
            var second=FindGround(new(from.MapId,first.Points[^1]),to,cancelled,threats,hints with{Via=null});
            if(!second.Success||second.Partial)continue;
            var joined=SIMovementRoute.ApproachDestination(first.Points.Concat(second.Points.Skip(1)).ToArray(),to,out bool arrived);
            float length=0;for(int i=1;i<joined.Length;++i)length+=Vector3.Distance(joined[i-1],joined[i]);
            if(length>distance*2+40)continue;
            var prefix=SIMovementRoute.Prefix(joined,SINavMeshPlanner.LegLength);
            if(!_planner.Walkable(from,prefix)||!hints.Obstacles.IsDefaultOrEmpty&&FirstCollision(prefix,hints.Obstacles,out _,out _))continue;
            _planner.Filter.Use(threats,from.Point);
            return new(prefix.ToImmutableArray()){Complete=(arrived||second.Complete)&&length<=SINavMeshPlanner.LegLength+.01f,Waypoint=second.Waypoint};
        }
        _planner.Filter.Use(threats,from.Point);
        return null;
    }

    SIGroundPath VaryRoute(SIPathPosition from,SIGroundPath route,Func<bool> cancelled,ulong seed)
    {
        var points=route.Points;float length=0;
        for(int i=1;i<points.Length;++i)length+=Vector3.Distance(points[i-1],points[i]);
        if(length<35)return route; // Keep close pursuit and interactions precise.
        float along=Math.Min(55,length*.45f);int edge=1;
        while(edge<points.Length-1 && Vector3.Distance(points[edge-1],points[edge])<along)
        {along-=Vector3.Distance(points[edge-1],points[edge]);++edge;}
        var delta=points[edge]-points[edge-1];float span=delta.Length();
        if(span<.01f)return route;
        var middle=Vector3.Lerp(points[edge-1],points[edge],along/span);
        var side=new Vector3(-delta.Y,delta.X,0);
        if(side.LengthSquared()<.01f)return route;
        side=Vector3.Normalize(side);
        ulong hash=unchecked((seed^(seed>>30))*0xBF58476D1CE4E5B9UL);
        float offset=3+(hash%14000)/1000f;
        if((hash&0x10000)!=0)offset=-offset;
        for(int attempt=0;attempt<2;++attempt)
        {
            var anchor=middle+side*(attempt==0?offset:-offset);
            var first=_planner.Plan(from,new(from.MapId,anchor,true,2),cancelled,_max,1000);
            if(!first.Success||!first.Complete||Vector3.Distance(first.Points[^1],middle)<1)continue;
            var second=_planner.Plan(new(from.MapId,first.Points[^1]),new(from.MapId,points[^1]),cancelled,_max,1000);
            if(!second.Success||!second.Complete)continue;
            var joined=first.Points.Concat(second.Points.Skip(1)).ToArray();float detour=0;
            for(int i=1;i<joined.Length;++i)detour+=Vector3.Distance(joined[i-1],joined[i]);
            if(detour>length*1.35f+8)continue;
            // Both legs pass the ordinary terrain/slope checks. The caller still
            // checks the resulting path against every live collision obstacle.
            return route with{Points=SIMovementRoute.Prefix(joined,SINavMeshPlanner.LegLength).ToImmutableArray(),
                Complete=route.Complete&&detour<=SINavMeshPlanner.LegLength};
        }
        return route; // A narrow passage may have only one safe corridor.
    }

    static bool FirstCollision(IReadOnlyList<Vector3> points,ImmutableArray<GameObjectCollisionSnapshot> obstacles,
        out int edge,out GameObjectCollisionSnapshot obstacle)
    {
        for(int i=1;i<points.Count;++i)
            foreach(var candidate in obstacles)
                if(candidate.Blocks(points[i-1],points[i])){edge=i;obstacle=candidate;return true;}
        edge=0;obstacle=default;return false;
    }

    Vector3[] ObjectDetour(int map,Vector3[] route,int edge,GameObjectCollisionSnapshot obstacle,
        ImmutableArray<GameObjectCollisionSnapshot> obstacles,Func<bool> cancelled,SIPathPosition destination)
    {
        float margin=obstacle.Radius+1;
        var low=obstacle.Bounds.Lo-new Vector3(margin,margin,0);var high=obstacle.Bounds.Hi+new Vector3(margin,margin,0);
        int reconnect=edge;
        while(reconnect+1<route.Length&&route[reconnect].X>=low.X&&route[reconnect].X<=high.X&&
            route[reconnect].Y>=low.Y&&route[reconnect].Y<=high.Y)++reconnect;
        // Begin one checked corner earlier when possible, giving the detour a
        // smooth entry instead of reaching the prop and stepping back around it.
        int startIndex=Math.Max(0,edge-2);
        var start=route[startIndex];var goal=route[reconnect];
        bool AllowedStop(Vector3 point)=>destination.Contains(point)||destination.Slack>0&&Vector3.Distance(point,destination.Point)<=destination.Slack;
        if(reconnect==route.Length-1&&(destination.Arrival>0||destination.Slack>0)&&goal.X>=low.X&&goal.X<=high.X&&goal.Y>=low.Y&&goal.Y<=high.Y)
        {
            // Interaction destinations can lie inside a solid chest/table. Use
            // their existing allowed stand-off instead of trying to enter it.
            Vector3[] approaches=[new(low.X,Math.Clamp(start.Y,low.Y,high.Y),goal.Z),
                new(high.X,Math.Clamp(start.Y,low.Y,high.Y),goal.Z),
                new(Math.Clamp(start.X,low.X,high.X),low.Y,goal.Z),
                new(Math.Clamp(start.X,low.X,high.X),high.Y,goal.Z)];
            foreach(var approach in approaches.OrderBy(p=>Vector3.DistanceSquared(start,p)))
            {
                if(!AllowedStop(approach))continue;
                var stop=_planner.Plan(new(map,start),new(map,approach),cancelled,_max,1000);
                if(stop.Success&&stop.Complete&&AllowedStop(stop.Points[^1])&&
                    !FirstCollision(stop.Points,obstacles,out _,out _))
                    return route.Take(startIndex).Concat(stop.Points).ToArray();
            }
        }
        Vector3[] corners=[new(low.X,low.Y,start.Z),new(high.X,low.Y,start.Z),new(high.X,high.Y,start.Z),new(low.X,high.Y,start.Z)];
        Vector3[] best=null;float bestCost=float.PositiveInfinity;
        for(int first=0;first<4;++first)
            foreach(int direction in new[]{-1,1})
            {
                var candidate=new List<Vector3>{start};
                for(int count=0;count<3;++count)
                {
                    if(cancelled())throw new OperationCanceledException();
                    var corner=corners[(first+direction*count+8)%4];
                    var side=_planner.Plan(new(map,candidate[^1]),new(map,corner),cancelled,_max,1000);
                    if(!side.Success||!side.Complete||FirstCollision(side.Points,obstacles,out _,out _))
                    {Trace?.Invoke($"Object detour corner {corner}: {side.Error??"incomplete or colliding"}; start {candidate[^1]}");break;}
                    candidate.AddRange(side.Points.Skip(1));
                    var end=_planner.Plan(new(map,candidate[^1]),new(map,goal),cancelled,_max,1000);
                    if(!end.Success||!end.Complete||FirstCollision(end.Points,obstacles,out _,out _))
                    {Trace?.Invoke($"Object detour exit {candidate[^1]} -> {goal}: {end.Error??"incomplete or colliding"}");continue;}
                    var joined=candidate.Concat(end.Points.Skip(1)).ToArray();
                    float cost=0;
                    for(int i=1;i<joined.Length;++i)cost+=SITravelGraph.TerrainCost(joined[i-1],joined[i],Vector3.Distance(joined[i-1],joined[i]));
                    if(cost>=bestCost)continue;
                    bestCost=cost;best=joined;
                }
            }
        if(best==null)return null;
        return route.Take(startIndex).Concat(best).Concat(route.Skip(reconnect+1)).ToArray();
    }
}
