using System;
using System.Numerics;
namespace Game.AI.SIMovement;

public static class SIPathFollower
{
    public static Vector3 Advance(Vector3 position, Vector3[] route,
        ref int waypoint, float speed, int elapsed)
    {
        float budget = Math.Max(0, speed) * Math.Clamp(elapsed, 0, 500) / 1000f;
        while (waypoint < route.Length && budget > 0)
        {
            Vector3 next = route[waypoint];
            float distance = (float)Math.Sqrt(Vector3.DistanceSquared(position, next));
            if (distance <= budget) { position = next; budget -= distance; waypoint++; }
            else
            {
                float t = budget / distance;
                position = new(position.X + (next.X - position.X) * t,
                    position.Y + (next.Y - position.Y) * t, position.Z + (next.Z - position.Z) * t);
                budget = 0;
            }
        }
        return position;
    }
}
