using System;
using System.Numerics;
namespace Game.AI.SIMovement;
public readonly record struct SITravelThreat(Vector3 Position,float Radius,bool CreatureAggro=true)
{
    public bool Blocks(Vector3 from,Vector3 to,Vector3 origin)
    {
        if(Math.Abs(from.Z-Position.Z)>6&&Math.Abs(to.Z-Position.Z)>6)return false;
        var a=new Vector2(from.X-Position.X,from.Y-Position.Y);var b=new Vector2(to.X-Position.X,to.Y-Position.Y);
        var start=new Vector2(origin.X-Position.X,origin.Y-Position.Y);var edge=b-a;
        // Permit escaping an existing aggro circle, never deeper entry/re-entry.
        if(start.LengthSquared()<Radius*Radius&&Vector2.Dot(a,edge)>=0&&b.LengthSquared()>=a.LengthSquared())return false;
        float t=edge.LengthSquared()<.0001f?0:Math.Clamp(-Vector2.Dot(a,edge)/edge.LengthSquared(),0,1);
        return (a+edge*t).LengthSquared()<Radius*Radius;
    }
}
