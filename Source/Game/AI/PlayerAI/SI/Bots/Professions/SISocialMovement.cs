using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI;

public static class SISocialMovement
{
    public static bool Active(string goal,bool queued,bool inBattleground) => goal=="social" || goal=="bg"&&queued&&!inBattleground;
    public static bool StrollPointAllowed(bool outdoors,float bankDistance,float auctionDistance) =>
        bankDistance>=22 && (outdoors || auctionDistance<25);
    public static bool OnPath(Vector3 point,IReadOnlyList<Vector3> path,float width)
    {
        if(path==null)return false;
        for(int i=1;i<path.Count;++i)
        {
            var delta=path[i]-path[i-1];float length=delta.LengthSquared();
            float t=length>0?Math.Clamp(Vector3.Dot(point-path[i-1],delta)/length,0,1):0;
            if(Vector3.DistanceSquared(point,path[i-1]+delta*t)<=width*width)return true;
        }
        return false;
    }
    public static bool ServiceAllowed(int currentCity, int selectedCity, int serviceZone) =>
        selectedCity != 0 && currentCity == selectedCity && serviceZone == selectedCity;
    public static bool RouteAllowed(int selectedCity, bool travellingToCity, IEnumerable<int> zones) =>
        travellingToCity || selectedCity != 0 && zones.All(zone => zone == selectedCity);
}
