using System;
using System.Numerics;
using System.Collections.Generic;
namespace Game.AI.SIMovement;

public static class SIMovementRoute
{
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
