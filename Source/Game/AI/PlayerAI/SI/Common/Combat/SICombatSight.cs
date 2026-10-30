using Framework.Constants;
using System;
using System.Numerics;

namespace Game.AI;

public static class SICombatSight
{
    // Vmaps contain buildings/models, not the ADT terrain surface. Detect ray
    // crossings of the heightfield as well. Holes break the surface; a ray wholly
    // below it (a cave/underground room) is left to the native model collision.
    public static bool TerrainClear(Vector3 from, Vector3 to, Func<float,float,float> height)
    {
        if (!float.IsFinite(from.X+from.Y+from.Z+to.X+to.Y+to.Z)) return false;
        float length=Vector2.Distance(new(from.X,from.Y),new(to.X,to.Y));
        int steps=Math.Clamp((int)MathF.Ceiling(length/.5f),1,1024);
        int previous=0;
        for(int i=0;i<=steps;++i)
        {
            var point=Vector3.Lerp(from,to,(float)i/steps);
            float ground=height(point.X,point.Y);
            if(!float.IsFinite(ground)||ground<=MapConst.InvalidHeight) { previous=0;continue; }
            float difference=point.Z-ground;
            int side=difference>.1f?1:difference<-.1f?-1:0;
            if(side!=0 && previous!=0 && side!=previous) return false;
            if(side!=0)previous=side;
        }
        return true;
    }
}
