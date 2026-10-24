using System;
using System.Numerics;
using System.Collections.Generic;
namespace Game.AI.SIMovement;

public static class SIMovementRoute
{
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
