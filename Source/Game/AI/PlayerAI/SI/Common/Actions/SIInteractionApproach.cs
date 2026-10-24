using System.Numerics;
using System;

namespace Game.AI
{
    public static class SIInteractionApproach
    {
        // A native-approved NPC interaction must not wait forever for an extra
        // AI sight ray after arrival. Loot and combat retain their sight checks.
        public static bool Ready(float distance, float range, bool sight, bool nativeNpcAllowed) =>
            distance <= range && (sight || nativeNpcAllowed);
        public static bool TimedOut(long elapsed, bool ready) => !ready && elapsed >= 45000;
        public static Vector3 Endpoint(Vector3 actor, Vector3 target, float distance, bool sight) =>
            sight && distance <= 4.5f && Math.Abs(actor.Z-target.Z) <= 1.5f
                ? StandOff(actor,target,1.5f) : target;
        public static Vector3 StandOff(Vector3 actor, Vector3 target, float distance)
        {
            Vector3 offset = actor - target; offset.Z = 0;
            if (offset.LengthSquared() < .0001f) offset = Vector3.UnitX;
            return target + Vector3.Normalize(offset) * distance;
        }
    }
}
