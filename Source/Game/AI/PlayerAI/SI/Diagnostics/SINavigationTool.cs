using Game.AI.SIMovement;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // Offline navigation checks against the extracted mmaps; no database, configuration or network.
    public static class SINavigationTool
    {
        public static void BuildGraph(string data, int map)
        {
            var watch = Stopwatch.StartNew();
            var graph = SITravelGraphs.Ensure(data, map);
            Console.WriteLine(graph == null ? $"No travel graph for map {map}." :
                $"Travel graph map {map}: {graph.NodeCount} nodes, {graph.EdgeCount} links, {watch.Elapsed.TotalSeconds:F1} s ({SITravelGraphs.GraphFile(data, map)}).");
        }

        // Random walks between connected graph nodes 200-2500 yards apart; reports the arrival rate.
        public static void RandomWalks(string data, int map, int count)
        {
            var graph = SITravelGraphs.Ensure(data, map);
            if (graph == null) { Console.WriteLine("No travel graph."); return; }
            var component = graph.Components();
            var random = new Random(12345);
            int arrived = 0, tried = 0;
            var watch = Stopwatch.StartNew();
            while (tried < count)
            {
                int a = random.Next(graph.NodeCount), b = random.Next(graph.NodeCount);
                if (graph.Degree(a) == 0 || component[a] != component[b]) continue;
                float distance = Vector3.Distance(graph.Position(a), graph.Position(b));
                if (distance < 200 || distance > 2500) continue;
                ++tried;
                // Start and end a little off the node, like a real bot or NPC position.
                var from = graph.Position(a) + new Vector3(random.NextSingle() * 16 - 8, random.NextSingle() * 16 - 8, 0);
                var to = graph.Position(b) + new Vector3(random.NextSingle() * 16 - 8, random.NextSingle() * 16 - 8, 0);
                Console.WriteLine($"route {tried}: {from} -> {to} ({distance:F0} yd)");
                if (Walk(data, map, from, to)) ++arrived;
            }
            Console.WriteLine($"RESULT map {map}: {arrived}/{tried} arrived, {watch.Elapsed.TotalSeconds:F0} s");
        }

        // Random routes walked with a replan partway through every leg (a moving target, a pull, a refreshed task),
        // counting fresh legs that start by heading back the way the bot came: before and after the controller
        // skips the corners it is already past.
        public static void ReplanWalks(string data, int map, int count)
        {
            var graph = SITravelGraphs.Ensure(data, map);
            if (graph == null) { Console.WriteLine("No travel graph."); return; }
            var component = graph.Components();
            var navigation = new SIWorldNavigation(data);
            var random = new Random(4242);
            int routes = 0, legs = 0, rawBack = 0, skippedBack = 0, farBack = 0;
            while (routes < count)
            {
                int a = random.Next(graph.NodeCount), b = random.Next(graph.NodeCount);
                if (graph.Degree(a) == 0 || component[a] != component[b]) continue;
                float distance = Vector3.Distance(graph.Position(a), graph.Position(b));
                if (distance < 300 || distance > 2000) continue;
                ++routes;
                var position = graph.Position(a) + new Vector3(random.NextSingle() * 16 - 8, random.NextSingle() * 16 - 8, 0);
                var to = graph.Position(b);
                Vector2? heading = null;
                Vector3? via = null;
                for (int leg = 0; leg < 200 && Vector3.Distance(position, to) > 8; ++leg)
                {
                    if (via is Vector3 waypoint && Vector3.Distance(position, waypoint) <= 8) via = null;
                    var path = navigation.Find(new(map, position), new(map, to, true), () => false, default, new SIRouteHints(via, default));
                    via = path.Waypoint;
                    if (!path.Success || path.Points.Length < 2) break;
                    var raw = path.Points.ToArray();
                    if (heading is Vector2 previous)
                    {
                        ++legs;
                        if (StartsBackwards(raw, previous)) ++rawBack;
                        if (StartsBackwards(SIMovementRoute.SkipPassed(raw, (_, _) => true), previous))
                        {
                            ++skippedBack;
                            // how far the new leg goes back against the old heading, within its first 60 yards
                            float back = 0, along = 0;
                            for (int i = 1; i < raw.Length && along < 60; ++i)
                            {
                                along += Vector3.Distance(raw[i - 1], raw[i]);
                                back = Math.Max(back, -Vector2.Dot(new Vector2(raw[i].X - raw[0].X, raw[i].Y - raw[0].Y), previous));
                            }
                            if (back >= 20) ++farBack;
                            if (skippedBack <= 25)
                                Console.WriteLine($"  back {back:F0} yd at {position}, {Vector2.Distance(new(position.X, position.Y), new(to.X, to.Y)):F0} yd to go, " +
                                    $"leg {raw.Length} points{(path.Complete ? " complete" : "")}{(path.Partial ? " partial" : "")}");
                        }
                    }
                    // walk 60% of the leg, then plan again from there
                    float length = 0;
                    for (int i = 1; i < raw.Length; ++i) length += Vector3.Distance(raw[i - 1], raw[i]);
                    if (length < 1) break;
                    float target = path.Complete ? length : length * .6f, walked = 0;
                    for (int i = 1; i < raw.Length; ++i)
                    {
                        float step = Vector3.Distance(raw[i - 1], raw[i]);
                        if (walked + step >= target || i == raw.Length - 1)
                        {
                            position = Vector3.Lerp(raw[i - 1], raw[i], Math.Clamp((target - walked) / Math.Max(step, .001f), 0, 1));
                            var direction = new Vector2(raw[i].X - raw[i - 1].X, raw[i].Y - raw[i - 1].Y);
                            heading = direction.LengthSquared() > 0 ? Vector2.Normalize(direction) : heading;
                            break;
                        }
                        walked += step;
                    }
                    if (path.Complete) break;
                }
            }
            Console.WriteLine($"RESULT map {map}: {routes} routes, {legs} replanned legs; start backwards {rawBack} as planned, {skippedBack} after skipping passed corners, {farBack} of them 20+ yards");
        }

        static bool StartsBackwards(Vector3[] route, Vector2 heading)
        {
            for (int i = 1; i < route.Length; ++i)
            {
                var step = new Vector2(route[i].X - route[0].X, route[i].Y - route[0].Y);
                if (step.Length() < 2) continue;
                return Vector2.Dot(Vector2.Normalize(step), heading) < -.2f;
            }
            return false;
        }

        // Walks a bot's legs from one point to another the way the controller does: each leg starts where
        // the previous one ended, until a leg reaches the destination or planning fails.
        public static bool Walk(string data, int map, Vector3 from, Vector3 to, bool heightKnown = true, bool verbose = false)
        {
            if (Environment.GetEnvironmentVariable("SI_TRAVEL_GRAPH") != "0") SITravelGraphs.Ensure(data, map);
            var navigation = new SIWorldNavigation(data);
            if (verbose) SIWorldNavigation.Trace = Console.WriteLine;
            var position = from; float walked = 0; var watch = Stopwatch.StartNew();
            for (int leg = 1; leg <= 400; ++leg)
            {
                long started = Stopwatch.GetTimestamp();
                var path = navigation.Find(new(map, position), new(map, to, heightKnown), () => false);
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (!path.Success)
                {
                    // The controller accepts the closest reachable point when it is this near.
                    if (path.Error == SIGroundPath.UnreachableError && Vector3.Distance(position, to) <= 12)
                    { Console.WriteLine($"  arrived (closest reachable point) after {leg - 1} legs, {walked:F0} yd walked; {Vector3.Distance(position, to):F1} yd from target"); return true; }
                    Console.WriteLine($"  leg {leg}: FAILED at {position}: {path.Error}"); return false;
                }
                float length = 0;
                for (int i = 1; i < path.Points.Length; ++i) length += Vector3.Distance(path.Points[i - 1], path.Points[i]);
                walked += length; position = path.Points[^1];
                if (verbose) Console.WriteLine($"  leg {leg}: {length:F0} yd, {path.Points.Length} points, {ms:F1} ms{(path.Partial ? " partial" : "")}{(path.Complete ? " complete" : "")} -> {position}");
                if (path.Complete || Vector3.Distance(position, to) <= 4)
                {
                    Console.WriteLine($"  arrived after {leg} legs, {walked:F0} yd walked ({Vector3.Distance(from, to):F0} yd straight), {watch.Elapsed.TotalMilliseconds:F0} ms planning; end {position}, {Vector3.Distance(position, to):F1} yd from target");
                    return true;
                }
                if (length < .5f) { Console.WriteLine($"  leg {leg}: no progress at {position}"); return false; }
            }
            Console.WriteLine("  gave up after 400 legs");
            return false;
        }
    }
}
