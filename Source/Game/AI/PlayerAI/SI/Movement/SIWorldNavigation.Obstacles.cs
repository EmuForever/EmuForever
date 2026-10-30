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
        if(!route.Success||hints.Obstacles.IsDefaultOrEmpty)return route;
        var points=route.Points.ToArray();
        for(int attempt=0;attempt<4;++attempt)
        {
            if(cancelled())throw new OperationCanceledException();
            if(!FirstCollision(points,hints.Obstacles,out int edge,out var obstacle))return route with{Points=points.ToImmutableArray()};
            var detour=ObjectDetour(from.MapId,points,edge,obstacle,hints.Obstacles,cancelled,route.Complete?to.Slack:0);
            if(detour==null)return SIGroundPath.Failed("No walkable route around a solid spawned gameobject");
            points=detour;
        }
        if(!FirstCollision(points,hints.Obstacles,out int blocked,out _))return route with{Points=points.ToImmutableArray()};
        // Crowded areas can be continued in another leg, but never publish an
        // unchecked remainder merely because the local detour budget was used.
        return blocked>1?route with{Points=points.Take(blocked).ToImmutableArray(),Complete=false}:
            SIGroundPath.Failed("Spawned gameobjects block the walking corridor");
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
        ImmutableArray<GameObjectCollisionSnapshot> obstacles,Func<bool> cancelled,float goalSlack)
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
        if(reconnect==route.Length-1&&goalSlack>0&&goal.X>=low.X&&goal.X<=high.X&&goal.Y>=low.Y&&goal.Y<=high.Y)
        {
            // Interaction destinations can lie inside a solid chest/table. Use
            // their existing allowed stand-off instead of trying to enter it.
            Vector3[] approaches=[new(low.X,Math.Clamp(start.Y,low.Y,high.Y),goal.Z),
                new(high.X,Math.Clamp(start.Y,low.Y,high.Y),goal.Z),
                new(Math.Clamp(start.X,low.X,high.X),low.Y,goal.Z),
                new(Math.Clamp(start.X,low.X,high.X),high.Y,goal.Z)];
            foreach(var approach in approaches.OrderBy(p=>Vector3.DistanceSquared(start,p)))
            {
                if(Vector3.Distance(approach,goal)>goalSlack)continue;
                var stop=_planner.Plan(new(map,start),new(map,approach),cancelled,_max,1000);
                if(stop.Success&&stop.Complete&&Vector3.Distance(stop.Points[^1],goal)<=goalSlack&&
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
