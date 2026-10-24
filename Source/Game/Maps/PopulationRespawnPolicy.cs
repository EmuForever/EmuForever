using System;
namespace Game.Maps;

public static class PopulationRespawnPolicy
{
    // Never lengthen an existing timer or let density cause an instant respawn.
    // This is a local population policy, not a claim to reproduce retail internals.
    public static double Scale(double delay,int players,double rate,double minimum)
    {
        if(delay<=0||players<=1||!double.IsFinite(delay)||!double.IsFinite(rate)||rate<=0)return delay;
        return Math.Min(delay,Math.Max(minimum,Math.Ceiling(delay*Math.Min(1,rate/players))));
    }
}
