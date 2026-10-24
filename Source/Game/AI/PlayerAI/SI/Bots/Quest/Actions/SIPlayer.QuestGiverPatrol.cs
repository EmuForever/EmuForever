using System;
using System.Numerics;

namespace Game.AI
{
    // One cursor per owned pickup task; waypoint geometry stays shared in the
    // database-backed manager rather than being copied for every SI player.
    internal sealed class SIQuestGiverPatrol
    {
        internal readonly WaypointPath Path;
        internal readonly int NodeCount;
        internal int Node, Direction = 1, Remaining, Blocked;
        internal float ClosestDistance;

        internal SIQuestGiverPatrol(WaypointPath path, Vector3 position)
        {
            Path = path;
            NodeCount = path.Nodes.Count;
            float nearest = float.MaxValue;
            for (int i = 0; i < path.Nodes.Count; ++i)
            {
                var node = path.Nodes[i];
                float distance = Vector3.DistanceSquared(position, new(node.X, node.Y, node.Z));
                if (distance < nearest) { nearest = distance; Node = i; }
            }
            ClosestDistance = MathF.Sqrt(nearest);
            Remaining = path.Flags.HasFlag(WaypointPathFlags.FollowPathBackwardsFromEndToStart)
                ? Math.Max(1, 2 * (path.Nodes.Count - 1)) : path.Nodes.Count;
            if (Node == path.Nodes.Count - 1 && Node != 0) Direction = -1;
        }

        internal void Advance()
        {
            --Remaining;
            if (Remaining == 0) return;
            if (Path.Flags.HasFlag(WaypointPathFlags.FollowPathBackwardsFromEndToStart))
            {
                if (Node + Direction < 0 || Node + Direction >= Path.Nodes.Count) Direction = -Direction;
                Node += Direction;
            }
            else Node = (Node + 1) % Path.Nodes.Count;
            ClosestDistance = float.MaxValue;
        }

    }

    public sealed partial class SIPlayer
    {
        SIBehaviorResult? SearchQuestGiverPatrol(SITask task, SISpawn spawn)
        {
            if (task.Kind != SITaskKind.PickUp || spawn.GameObject || spawn.Map != me.GetMapId()) return null;
            if (!task.PatrolChecked)
            {
                task.PatrolChecked = true;
                // Match Creature.GetCreatureAddon precedence: a spawn addon,
                // including one with PathId=0, overrides the template addon.
                var addon = Global.ObjectMgr.GetCreatureAddon(spawn.SpawnId) ??
                    Global.ObjectMgr.GetCreatureTemplateAddon(spawn.Entry);
                int pathId = addon?.PathId ?? 0;
                if (pathId == 0)
                    pathId = Global.SmartAIMgr.GetAmbientPatrolPathId(spawn.Entry, spawn.SpawnId);
                var path = Global.WaypointMgr.GetPath(pathId);
                if (path == null || path.Nodes.Count == 0) return null;
                task.PatrolSearch = new SIQuestGiverPatrol(path, Here);
                CancelTravel();
            }
            var search = task.PatrolSearch;
            if (search == null) return null;
            // Reloads/GM waypoint edits must not leave a cursor indexing old data.
            if (!ReferenceEquals(Global.WaypointMgr.GetPath(search.Path.Id), search.Path) ||
                search.Path.Nodes.Count != search.NodeCount ||
                search.Node < 0 || search.Node >= search.Path.Nodes.Count)
                return FailInteractionTask(task, "Quest giver patrol changed; retrying its database route.");

            // At most eight coincident/unreachable nodes per decision. A finite
            // circuit prevents endless patrols for dead, event-disabled or phased
            // out givers. Release unavailable work rather than standing at the end.
            for (int work = 0; work < 8; ++work)
            {
                if (search.Remaining == 0)
                    return FailInteractionTask(task, "Quest giver not found along its database patrol; retrying later.", spawnMissing: true);
                var node = search.Path.Nodes[search.Node];
                var point = new Vector3(node.X, node.Y, node.Z);
                float distance = Vector3.Distance(Here, point);
                if (search.ClosestDistance == float.MaxValue) search.ClosestDistance = distance;
                if (distance < search.ClosestDistance - 4)
                {
                    search.ClosestDistance = distance;
                    task.LastProgressAt = Now;
                }
                string label = $"Searching quest giver patrol {search.Path.Id}, point {node.Id}: {task.Label}";
                var result = Travel(P(point), label, arrival: 4, destinationTolerance: 1, rememberFailure: false);
                if (result == SIBehaviorResult.Running) return result;
                if (result == SIBehaviorResult.Failure)
                {
                    CancelTravel();
                    if (++search.Blocked >= 3)
                        return FailInteractionTask(task, "Quest giver patrol is inaccessible; retrying later.");
                }
                else
                {
                    search.Blocked = 0;
                    task.LastProgressAt = Now;
                }
                search.Advance();
            }
            return SIBehaviorResult.Running;
        }
    }
}
