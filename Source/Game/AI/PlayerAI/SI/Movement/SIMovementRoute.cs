using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
namespace Game.AI.SIMovement;

public static class SIMovementRoute
{
    // Resume a plan computed while we were already walking. Keep all remaining
    // corners, starting at the closest segment rather than returning to its origin.
    public static Vector3[] ContinueFrom(Vector3 here,Vector3[] route)
    {
        int next=-1;float best=3*3;
        for(int i=1;i<route.Length;++i)
        {
            var delta=route[i]-route[i-1];float length=delta.LengthSquared();
            float t=length>0?Math.Clamp(Vector3.Dot(here-route[i-1],delta)/length,0,1):0;
            float distance=Vector3.DistanceSquared(here,route[i-1]+delta*t);
            if(distance<=best){best=distance;next=i;}
        }
        return next<0?null:new[]{here}.Concat(route.Skip(next)).ToArray();
    }
    public static bool FollowNeedsMovement(float distance,bool approaching,float maximum=8,float settle=3) =>
        distance>(approaching?settle:maximum);

    // Stop at the first entry into the usable destination range, keeping every
    // corner before it. Do this before slope checks: ground beyond arrival is
    // irrelevant, and may be the wall/ledge the target is standing on.
    public static Vector3[] ApproachDestination(IReadOnlyList<Vector3> points,SIPathPosition goal,out bool arrived)
    {
        arrived=false;
        if(goal.Arrival<=0||points.Count==0)return points.ToArray();
        var result=new List<Vector3>{points[0]};
        if(goal.Contains(points[0])){arrived=true;return [points[0],points[0]];}
        for(int i=1;i<points.Count;++i)
        {
            var start=points[i-1]-goal.Point;var delta=points[i]-points[i-1];
            var radialStart=start;var radialDelta=delta;
            if(!goal.Within3D||!goal.HeightKnown){radialStart.Z=0;radialDelta.Z=0;}
            float a=radialDelta.LengthSquared(),b=Vector3.Dot(radialStart,radialDelta),c=radialStart.LengthSquared()-goal.Arrival*goal.Arrival;
            float low=0,high=1;
            bool intersects=true;
            if(a<.000001f)intersects=c<=0;
            else
            {
                float discriminant=b*b-a*c;
                if(discriminant<0)intersects=false;
                else{float root=MathF.Sqrt(discriminant);low=Math.Max(low,(-b-root)/a);high=Math.Min(high,(-b+root)/a);}
            }
            if(goal.HeightKnown&&!goal.Within3D)
            {
                float height=Math.Max(goal.Arrival,1.5f);
                if(Math.Abs(delta.Z)<.000001f)intersects&=Math.Abs(start.Z)<=height;
                else
                {
                    float first=(-height-start.Z)/delta.Z,last=(height-start.Z)/delta.Z;
                    low=Math.Max(low,Math.Min(first,last));high=Math.Min(high,Math.Max(first,last));
                }
            }
            if(intersects&&low<=high&&low<=1&&high>=0)
            {
                var end=Vector3.Lerp(points[i-1],points[i],Math.Clamp(low,0,1));
                if(goal.Contains(end)){result.Add(end);arrived=true;return result.ToArray();}
            }
            result.Add(points[i]);
        }
        return result.ToArray();
    }
    public const float TravelWaypointRadius = 20;
    public static float PrefetchDistance(float speed)=>Math.Clamp(speed*12,120,360);
    public static float HandoffDistance(float speed,double tickSeconds)=>Math.Clamp(speed*(float)Math.Max(4,tickSeconds*3),12,120);
    public static bool ReachedTravelWaypoint(Vector3 here, Vector3 waypoint) =>
        Vector3.DistanceSquared(here, waypoint) <= TravelWaypointRadius * TravelWaypointRadius;

    // Only road-graph waypoints have this tolerance. Preserve the mesh corners
    // leading into the radius; NPC approaches and the final destination stay exact.
    public static Vector3[] ApproachTravelWaypoint(IReadOnlyList<Vector3> points, Vector3 waypoint)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < points.Count; ++i)
        {
            result.Add(points[i]);
            if (i > 0 && ReachedTravelWaypoint(points[i], waypoint)) break;
        }
        return result.ToArray();
    }

    // Plan far enough to see around terrain, but publish short splines so live
    // collision/target changes can be applied without rewinding the route.
    public static Vector3[] Prefix(IReadOnlyList<Vector3> points,float length)
    {
        var result=new List<Vector3>();if(points.Count==0)return [];
        result.Add(points[0]);
        for(int i=1;i<points.Count&&length>0;++i)
        {
            float step=Vector3.Distance(points[i-1],points[i]);
            if(step>length){result.Add(Vector3.Lerp(points[i-1],points[i],length/step));break;}
            result.Add(points[i]);length-=step;
        }
        return result.ToArray();
    }

    public static Vector3[] Prepare(Vector3 here,Vector3[] route)
    {
        // Detail-height projection and joined legs can produce tiny consecutive
        // edges. Native splines reject any edge shorter than 0.1 yards.
        var points=new System.Collections.Generic.List<Vector3>{here};
        for(int i=1;i<route.Length;++i)
        {
            var point=route[i];
            if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))
                throw new ArgumentException("Non-finite movement point");
            if(Vector3.Distance(points[^1],point)>=.11f)points.Add(point);
        }
        return points.ToArray();
    }
    // A corner is behind the walker when it stands beyond it along the following segment. Paths are planned from
    // where the walker was (snapped onto the mesh) and it keeps moving meanwhile, so the first corners of a fresh
    // route can be behind it; walking to them is a step back. Skip those, while the next one is in straight reach.
    public static bool Passed(Vector3 here,Vector3 corner,Vector3 next,float reach,float rise)
    {
        var along=new Vector2(next.X-corner.X,next.Y-corner.Y);var offset=new Vector2(here.X-corner.X,here.Y-corner.Y);
        return Vector2.Dot(along,offset)>0&&offset.Length()<=reach&&Math.Abs(here.Z-corner.Z)<=rise;
    }
    public static Vector3[] SkipPassed(Vector3[] route,Func<Vector3,Vector3,bool> straight,float reach=8)
    {
        int first=1;
        while(first+1<route.Length&&Passed(route[0],route[first],route[first+1],reach,3)&&straight(route[0],route[first+1]))++first;
        if(first==1)return route;
        var result=new Vector3[route.Length-first+1];result[0]=route[0];
        Array.Copy(route,first,result,1,route.Length-first);return result;
    }
    // The native cursor identifies the next corner; never reinsert consumed points
    // or replace remaining corners with a straight chord to the next segment.
    public static Vector3[] Join(Vector3 here,Vector3[] current,int nextCorner,Vector3[] next)
    {
        if(nextCorner<1||nextCorner>=current.Length||next.Length<2||Vector3.Distance(current[^1],next[0])>1)
            throw new ArgumentException("Discontinuous movement legs");
        var joined=new Vector3[1+current.Length-nextCorner+next.Length-1];joined[0]=here;
        Array.Copy(current,nextCorner,joined,1,current.Length-nextCorner);
        Array.Copy(next,1,joined,1+current.Length-nextCorner,next.Length-1);return joined;
    }
}

// Retries belong to a place, not a task: changing targets while stuck must not
// reset the budget and leave a bot planning forever at the same coordinates.
public sealed class SIRouteRetry
{
    int _map=-1,_attempts;
    Vector3 _origin;
    public static bool Retryable(string error)=>error!=null&&
        (error.StartsWith("Route ",StringComparison.Ordinal)||error.StartsWith("No walkable route",StringComparison.Ordinal)||
        error==SIGroundPath.UnreachableError||error.StartsWith("Spawned gameobjects",StringComparison.Ordinal)||
        error.StartsWith("Movement blocked",StringComparison.Ordinal)||error.Contains("no measurable progress",StringComparison.Ordinal));
    public bool TryBegin(int map,Vector3 here,string error)
    {
        if(!Retryable(error))return false;
        if(map!=_map||Vector3.DistanceSquared(here,_origin)>144){_map=map;_origin=here;_attempts=0;}
        if(_attempts>=2)return false;
        ++_attempts;return true;
    }
}
