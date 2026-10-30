using System.Numerics;
using System;
using System.Collections.Generic;

namespace Game.AI
{
    public static class SIInteractionApproach
    {
        public const float NpcRange = 2.25f;
        public const float WalkRange = 4.5f;
        // A native-approved NPC interaction must not wait forever for an extra
        // AI sight ray after arrival. Loot and combat retain their sight checks.
        public static bool Ready(float distance, float range, bool sight, bool nativeNpcAllowed) =>
            distance <= range+.001f && (sight || nativeNpcAllowed);
        public static bool TimedOut(long elapsed, bool ready) => !ready && elapsed >= 45000;
        // Where to stand beside an NPC on the walker's side: within NpcRange in 3D, so a height difference
        // shortens the horizontal stand-off (otherwise "arrived" on a slope was still out of reach).
        public static Vector3 Endpoint(Vector3 actor, Vector3 target)
        {
            float reach = NpcRange - .1f, height = Math.Abs(actor.Z - target.Z);
            return StandOff(actor, target, MathF.Sqrt(Math.Max(0, reach * reach - height * height)));
        }
        public static Vector3 StandOff(Vector3 actor, Vector3 target, float distance)
        {
            Vector3 offset = actor - target; offset.Z = 0;
            if (offset.LengthSquared() < .0001f) offset = Vector3.UnitX;
            return target + Vector3.Normalize(offset) * distance;
        }
        public static Vector3[] LocalPath(Vector3 actor,Vector3 endpoint,Func<Vector3,float> floor,Func<Vector3,Vector3,bool> corridor)
        {
            float distance=Vector3.Distance(actor,endpoint);
            if(!float.IsFinite(distance)||distance>WalkRange||distance<.05f)return null;
            var points=new List<Vector3>{actor};
            float startFloor=floor(actor);
            float correction=SIMovement.SIWalkingSurface.InitialFloorCorrection(actor,new(actor.X,actor.Y,startFloor))?actor.Z-startFloor:0;
            int steps=Math.Max(1,(int)Math.Ceiling(distance/.5f));
            for(int i=1;i<=steps;++i)
            {
                var point=Vector3.Lerp(actor,endpoint,(float)i/steps);float height=floor(point);
                if(!float.IsFinite(height)||Math.Abs(height-point.Z)>1.25f)return null;
                point.Z=height+correction*(1-(float)i/steps);
                if(!SIMovement.SIWalkingSurface.SafeStep(points[^1],point)||!corridor(points[^1],point))return null;
                points.Add(point);
            }
            return points.ToArray();
        }
    }
}
