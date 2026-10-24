using Game.Entities;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        readonly Dictionary<(string Task, int Map, int X, int Y), long> _searchedAreas = new();
        static (string, int, int, int) SearchAreaKey(string task, int map, Vector3 point) =>
            (task, map, (int)MathF.Floor(point.X / (task.StartsWith("Objective:",StringComparison.Ordinal)?4:60)),
                (int)MathF.Floor(point.Y / (task.StartsWith("Objective:",StringComparison.Ordinal)?4:60)));
        bool SearchAreaAvailable(string task, Vector3 point) =>
            _searchedAreas.GetValueOrDefault(SearchAreaKey(task, me.GetMapId(), point)) <= Now;
        void RememberSearchedArea(SITask task, Vector3 point)
        {
            foreach (var key in _searchedAreas.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray()) _searchedAreas.Remove(key);
            _searchedAreas[SearchAreaKey(task.Key, me.GetMapId(), point)] = Now + 90000;
        }
        void Sweep(SITask task, string detail = null)
        {
            if (task.Hotspots.Count == 0) { DeferSearchTask(task); return; }
            if (task.Hotspot < 0 || task.Hotspot >= task.Hotspots.Count) task.Hotspot = NextHotspot(task);
            if (task.Hotspot < 0) { DeferSearchTask(task); return; }
            Vector3 hotspot = task.Hotspots[task.Hotspot];
            // Z=0 POIs are resolved by the pathfinder after loading the destination mesh tile.
            Position point = P(hotspot);
            string reason = $"{task.Label} ({(task.Kind==SITaskKind.Objective?"known spawn":"area")} {task.Hotspot + 1}/{task.Hotspots.Count})" + (detail != null ? ": " + detail : "");
            SIBehaviorResult move = Travel(point, reason, 6, heightKnown: hotspot.Z != 0);
            if (move == SIBehaviorResult.Running) { task.HotspotWaitUntil = 0; return; }
            if (move == SIBehaviorResult.Success)
            {
                if (hotspot.Z == 0) task.Hotspots[task.Hotspot] = new(hotspot.X, hotspot.Y, Here.Z);
                // World grids/respawns and moving creatures get several targeting scans here. Merely
                // reaching a hotspot must not immediately rotate to the next one on every update.
                if (task.HotspotWaitUntil == 0) task.HotspotWaitUntil = Now + 750;
                Status = "Searching nearby: " + task.Label;
                if (Now < task.HotspotWaitUntil) return;
            }
            else RememberBlockedRoute(point, "Search area is unreachable.");
            RememberSearchedArea(task, hotspot);
            task.Visited.Add(task.Hotspot); task.HotspotWaitUntil = 0; task.Hotspot = NextHotspot(task);
            if (task.Hotspot < 0) DeferSearchTask(task);
        }

        void DeferSearchTask(SITask task)
        {
            // A temporary travel failure must not abandon the quest or keep ownership forever.
            // Depleted areas are deferred individually. Keep the quest eligible so
            // replanning can select the same objective's next populated area.
            Status = "Trying another task; no reachable area for " + task.Label;
            Report("task_skipped", Status);
            SetTask(null); _nextReplan = 0;
        }

        int NextHotspot(SITask task)
        {
            return Enumerable.Range(0, task.Hotspots.Count).Where(i => !task.Visited.Contains(i) &&
                SearchAreaAvailable(task.Key, task.Hotspots[i]) && !IsLearnedRouteBlocked(P(task.Hotspots[i])))
                .OrderBy(i => Distance(task.Hotspots[i])).DefaultIfEmpty(-1).First();
        }
    }
}
