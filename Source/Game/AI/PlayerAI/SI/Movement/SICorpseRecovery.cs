using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Game.AI
{
    public readonly record struct SIRecoveryHazard(Position Position, float AggroRadius);

    public static class SICorpseRecovery
    {
        public const float ReclaimRadius = 35;
        public static bool ShouldRetryCorpseRun(long stationaryMilliseconds, int failedRoutes, bool corpseOnMap) =>
            !corpseOnMap || stationaryMilliseconds >= 90000 || failedRoutes >= 3;
        const float Margin = 6;

        public static Position ResolveCandidate(Position candidate, Position standing, Func<Position, Position> projectFloor) =>
            candidate.GetExactDist(standing) < .1f ? new Position(standing) : projectFloor(candidate);

        static float DistanceSquared(Position a, Position b)
        {
            float dx = a.GetPositionX() - b.GetPositionX(), dy = a.GetPositionY() - b.GetPositionY(), dz = a.GetPositionZ() - b.GetPositionZ();
            return dx * dx + dy * dy + dz * dz;
        }

        // A straight leg must not enter a new hostile's aggro radius. Escaping an existing threat necessarily
        // starts inside its radius, so those are ignored.
        public static bool AvoidsNewThreats(Position start, Position end, IReadOnlyList<SIRecoveryHazard> hazards)
        {
            float dx = end.GetPositionX() - start.GetPositionX(), dy = end.GetPositionY() - start.GetPositionY();
            float length = dx * dx + dy * dy;
            foreach (SIRecoveryHazard hazard in hazards)
            {
                float radius = hazard.AggroRadius + Margin;
                if (DistanceSquared(start, hazard.Position) <= radius * radius) continue;
                float t = length <= .001f ? 0 : Math.Clamp(((hazard.Position.GetPositionX() - start.GetPositionX()) * dx +
                    (hazard.Position.GetPositionY() - start.GetPositionY()) * dy) / length, 0, 1);
                var closest = new Position(start.GetPositionX() + t * dx, start.GetPositionY() + t * dy,
                    start.GetPositionZ() + (end.GetPositionZ() - start.GetPositionZ()) * t);
                if (DistanceSquared(closest, hazard.Position) <= radius * radius) return false;
            }
            return true;
        }

        public static bool IsSafe(Position point, IReadOnlyList<SIRecoveryHazard> hazards) =>
            hazards.All(hazard => DistanceSquared(point, hazard.Position) > MathF.Pow(hazard.AggroRadius + Margin, 2));

        // Reclaim points within reclaim range, outside every live hostile's aggro radius plus a margin,
        // nearest to the ghost first. Ground projection and path checks happen in the controller.
        public static IEnumerable<Position> Candidates(Position corpse, Position ghost, IReadOnlyList<SIRecoveryHazard> hazards)
        {
            var candidates = new List<Position> { new(ghost), new(corpse) };
            foreach (float radius in new float[] { 30, 22, 12 })
                for (int angle = 0; angle < 16; angle++)
                {
                    float radians = angle * MathF.Tau / 16;
                    candidates.Add(new Position(corpse.GetPositionX() + MathF.Cos(radians) * radius,
                        corpse.GetPositionY() + MathF.Sin(radians) * radius, corpse.GetPositionZ()));
                }
            return candidates.Where(point => DistanceSquared(point, corpse) <= ReclaimRadius * ReclaimRadius && IsSafe(point, hazards))
                .OrderBy(point => DistanceSquared(point, ghost));
        }

        // The native reclaim handler checks corpse ownership, distance and its reclaim delay.
        // A nearby graveyard must not force a pointless detour just to accumulate walked distance.
        public static bool CanFinishCorpseRun(long elapsedMilliseconds) => elapsedMilliseconds >= 30000;
    }

}
