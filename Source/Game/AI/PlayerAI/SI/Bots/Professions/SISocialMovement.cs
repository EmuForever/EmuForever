using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI;

public static class SISocialMovement
{
    public const float HubRadius = 45;
    public static bool ServiceAllowed(int currentCity, int selectedCity, int serviceZone) =>
        selectedCity != 0 && currentCity == selectedCity && serviceZone == selectedCity;
    public static bool RouteAllowed(int selectedCity, bool travellingToCity, IEnumerable<int> zones) =>
        travellingToCity || selectedCity != 0 && zones.All(zone => zone == selectedCity);
    public static bool NearHub(Vector3 actor, Vector3 bank, Vector3? auction)
    {
        bool Near(Vector3 hub) => Vector2.Distance(new(actor.X, actor.Y), new(hub.X, hub.Y)) <= HubRadius && Math.Abs(actor.Z - hub.Z) <= 20;
        return Near(bank) || auction.HasValue && Near(auction.Value);
    }
    public static Vector3 Destination(Vector3 bank, Vector3? auction, float hubSample, float angleSample, float radiusSample)
    {
        var center = auction.HasValue && hubSample >= .5f ? auction.Value : bank;
        float angle = Math.Clamp(angleSample, 0, 1) * MathF.PI * 2;
        float radius = 5 + Math.Clamp(radiusSample, 0, 1) * 20;
        return center + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
    }
}
