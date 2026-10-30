using Framework.Constants;
using Framework.GameMath;
using System;
using System.Numerics;

namespace Game.Collision;

// Geometry is immutable after loading and retained by this managed reference.
// No entity, owner, phase or mutable transform is read by a navigation worker.
public readonly record struct GameObjectCollisionSnapshot(WorldModel Geometry,Vector3 Position,
    Matrix4x4 InverseRotation,float InverseScale,AxisAlignedBox Bounds,float Radius=.5f,float Height=1.8f)
{
    public bool Blocks(Vector3 from,Vector3 to)
    {
        var delta=to-from;float length=delta.Length();
        if(length<.001f)return false;
        var direction=delta/length;var side=new Vector3(-direction.Y,direction.X,0);
        if(side.LengthSquared()>.001f)side=Vector3.Normalize(side)*Radius;
        for(int level=0;level<3;++level)
        {
            float height=level==0?.35f:level==1?Height*.55f:Height;
            for(int n=-1;n<=1;++n)
            {
                var start=from+side*n+new Vector3(0,0,height);
                var ray=new Ray(start,direction);
                if(ray.intersectionTime(Bounds)>length)continue;
                var local=new Ray(InverseRotation.Multiply(start-Position)*InverseScale,InverseRotation.Multiply(direction));
                float distance=length*InverseScale;
                if(Geometry.IntersectRay(local,ref distance,true,ModelIgnoreFlags.Nothing))return true;
            }
        }
        return false;
    }
}
