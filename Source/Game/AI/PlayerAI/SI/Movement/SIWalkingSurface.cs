using System;
using System.Numerics;

namespace Game.AI.SIMovement;

public static class SIWalkingSurface
{
    // Ordinary player walking: a small stair step is allowed, a near-vertical
    // lift is not. Longer inclines must stay below the client's sliding slope.
    public const float MaximumStep = .6f;
    public const float MaximumSlope = 1.15f; // Slightly below 50 degrees.
    // The actor's collision floor and the navmesh detail floor can differ at a
    // ramp/model seam. This is a start projection, not a walking edge. Omit the
    // synthetic vertical segment and validate the first actual walking segment.
    public static bool InitialFloorCorrection(Vector3 actor, Vector3 mesh) =>
        float.IsFinite(actor.Z) && float.IsFinite(mesh.Z) &&
        Vector2.Distance(new(actor.X, actor.Y), new(mesh.X, mesh.Y)) <= .1f && Math.Abs(actor.Z - mesh.Z) <= 1.5f;
    public static bool SafeStep(Vector3 from, Vector3 to, float slope = MaximumSlope)
    {
        var delta = to - from;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z)) return false;
        float flat = new Vector2(delta.X, delta.Y).Length();
        float rise = Math.Abs(delta.Z);
        // The mesh floor and actor floor can differ by a small stair-height
        // correction even at identical XY (e.g. model/terrain seams).
        return rise <= Math.Max(MaximumStep, flat * slope);
    }

    // Detail vertices/height interpolation have centimetre-scale seam noise.
    // This allowance is only for sub-yard floor samples; full route edges still
    // obey SafeStep, preventing small errors from accumulating into a climb.
    public static bool SafeDetailStep(Vector3 from,Vector3 to)=>DetailExcess(from,to)<=0;
    // Horizontal length over which detail-floor slope is judged, and the navmesh's own steep limit (55 degrees),
    // which no single floor sample may exceed.
    public const float DetailWindow = 2;
    public const float MeshSteepSlope = 1.43f;
    // How far a floor sample rises or drops beyond the detail limit (positive: too steep).
    public static float DetailExcess(Vector3 from,Vector3 to,float slope=MaximumSlope)=>
        !float.IsFinite(from.Z)||!float.IsFinite(to.Z)?float.PositiveInfinity:
        Math.Abs(to.Z-from.Z)-Math.Max(MaximumStep,Vector2.Distance(new(from.X,from.Y),new(to.X,to.Y))*slope)-.02f;

    // The walker's own floor (where the client or a previous spline left it) can sit up to this far off the mesh
    // the route is planned on: model seams, steep detail, eroded edges. Leaving that spot may use this much
    // extra height once; the rest of the route may not. Without it a bot standing there could never move.
    public const float StartCorrection = 1.5f;
    public const float StartCorrectionLength = 3;
    public static bool SafeFirstStep(Vector3 actor, Vector3 next, float slope = MaximumSlope)
    {
        if (SafeStep(actor, next, slope)) return true;
        var corrected = actor;
        corrected.Z += Math.Clamp(next.Z - actor.Z, -StartCorrection, StartCorrection);
        return SafeStep(corrected, next, slope);
    }
}
