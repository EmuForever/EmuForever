using Framework.Constants;
using Game.AI;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.BattleGrounds
{
    internal enum BattlegroundObjectiveAction { Move, Interact, Attack, Escort, Defend }

    // An objective describes native gameplay. The controller must walk, interact or fight;
    // it never changes ownership, flag states, match scores or resurrection timers itself.
    internal sealed record BattlegroundObjective(string Key, string Label, Position Position,
        BattlegroundObjectiveAction Action, ObjectGuid Target, int Priority, long SpawnId = 0, bool CarryingFlag = false);

    public partial class Battleground
    {
        GameObjectData[] _siObjectSpawns;
        CreatureData[] _siCreatureSpawns;
        readonly HashSet<ObjectGuid> _siDroppedFlags = new();

        internal virtual IEnumerable<BattlegroundObjective> ObjectivesForSI(Player player) => [];
        internal virtual Position ExitWaypointForSI(Player player, Position destination) => null;
        internal int KillingBlowsForSI(ObjectGuid player) => PlayerScores.TryGetValue(player, out var score) ? score.KillingBlows : 0;

        private protected void TrackObjectiveObjectForSI(GameObject obj)
        {
            if (obj.GetGoType() is GameObjectTypes.NewFlagDrop or GameObjectTypes.FlagDrop) _siDroppedFlags.Add(obj.GetGUID());
        }

        private protected void SetClassicSpawnGroup(int group, bool active)
        {
            if (Global.ObjectMgr.GetSpawnGroupData(group) is not { } data || data.mapId != GetMapId()) return;
            if (GetBgMap().IsSpawnGroupActive(group) == active) return;
            if (active) GetBgMap().SpawnGroupSpawn(group, ignoreRespawn: true);
            else GetBgMap().SpawnGroupDespawn(group, true);
        }

        private protected IEnumerable<GameObjectData> ObjectSpawnsForSI() => _siObjectSpawns ??=
            Global.ObjectMgr.GetAllGameObjectData().Values.Where(spawn => spawn.MapId == GetMapId()).ToArray();

        private protected void OpenClassicStartDoors()
        {
            if (GetMapId() is not (30 or 489 or 529)) return;
            foreach (var spawn in ObjectSpawnsForSI())
                if (Global.ObjectMgr.GetGameObjectTemplate(spawn.Id)?.type == GameObjectTypes.Door &&
                    GetBgMap().GetGameObjectBySpawnId(spawn.SpawnId) is { } door && door.GetGoState() == GameObjectState.Ready)
                    door.UseDoorOrButton();
        }

        // Before the gates open: a spot a few yards inside the team's start gate, different for each player, so
        // bots wait spread out by the door they will leave through instead of stacking on the entry point.
        // A base's start doors grouped into its exits: doors on the same side of the line from this base's start
        // to the enemy start belong to one exit (WSG: each base has two, each with an inner and an outer gate).
        // Inner: the gate nearest the start, where players wait. Out: a few yards past the farthest gate, the
        // waypoint for leaving that way. (The middle of an exit sat between two closed gates: unreachable.)
        (Position Inner, Position Out)[][] _siExits;
        internal (Position Inner, Position Out)[] ExitGatesForSI(int teamIndex)
        {
            _siExits ??= new (Position, Position)[2][];
            if (_siExits[teamIndex] != null) return _siExits[teamIndex];
            var start = GetTeamStartPosition(teamIndex)?.Loc;
            var enemyStart = GetTeamStartPosition(1 - teamIndex)?.Loc;
            if (start == null || enemyStart == null) return _siExits[teamIndex] = [];
            var doors = ObjectSpawnsForSI()
                .Where(spawn => Global.ObjectMgr.GetGameObjectTemplate(spawn.Id)?.type == GameObjectTypes.Door)
                .Where(spawn => spawn.SpawnPoint.GetExactDist2d(start) < 120)
                .Select(spawn => spawn.SpawnPoint).ToArray();
            if (doors.Length == 0) return _siExits[teamIndex] = [];
            float ax = enemyStart.GetPositionX() - start.GetPositionX(), ay = enemyStart.GetPositionY() - start.GetPositionY();
            float alen = MathF.Sqrt(ax * ax + ay * ay); ax /= alen; ay /= alen;
            float mx = doors.Average(d => d.GetPositionX()), my = doors.Average(d => d.GetPositionY());
            float Side(Position d) => ax * (d.GetPositionY() - my) - ay * (d.GetPositionX() - mx);
            (Position, Position) Exit(Position[] group)
            {
                var inner = group.OrderBy(d => d.GetExactDist2d(start)).First();
                var outer = group.OrderByDescending(d => d.GetExactDist2d(start)).First();
                return (inner, new Position(outer.GetPositionX() + ax * 6, outer.GetPositionY() + ay * 6, outer.GetPositionZ()));
            }
            var left = doors.Where(d => Side(d) >= 0).ToArray(); var right = doors.Where(d => Side(d) < 0).ToArray();
            return _siExits[teamIndex] = left.Length == 0 || right.Length == 0 ? [Exit(doors)] : [Exit(left), Exit(right)];
        }

        // Which exit a player uses: stable per player, so it waits by the gate it will leave through.
        internal static int ExitIndexForSI(Player player, int exitCount) =>
            exitCount <= 1 ? 0 : (int)(unchecked((uint)player.GetGUID().GetCounter() * 2246822519u) >> 16) % exitCount;

        internal Position GateWaitSpotForSI(Player player)
        {
            int teamIndex = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID()));
            var start = GetTeamStartPosition(teamIndex)?.Loc;
            var exits = ExitGatesForSI(teamIndex);
            if (start == null || exits.Length == 0) return null;
            Position door = exits[ExitIndexForSI(player, exits.Length)].Inner;
            float dx = start.GetPositionX() - door.GetPositionX(), dy = start.GetPositionY() - door.GetPositionY();
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 1) return null;
            dx /= length; dy /= length;
            uint hash = unchecked((uint)player.GetGUID().GetCounter() * 2654435761u);
            float back = Math.Min(length - 1, 5 + hash % 9);          // 5-13 yd inside the gate
            float side = (hash / 9 % 11) - 5f;                         // -5..5 yd across it
            var spot = new Position(door.GetPositionX() + dx * back - dy * side, door.GetPositionY() + dy * back + dx * side,
                door.GetPositionZ(), MathF.Atan2(-dy, -dx));
            // Stay within the start area the battleground enforces (_CheckSafePositions).
            float maxDistSq = GetStartMaxDist();
            return maxDistSq == 0 || spot.GetExactDistSq(start) <= maxDistSq ? spot : null;
        }

        private protected static BattlegroundObjective ObjectiveForSI(WorldObject obj, string label,
            BattlegroundObjectiveAction action, int priority, bool carrying = false) =>
            new(obj.GetGUID().ToString(), label, new Position(obj), action, obj.GetGUID(), priority, CarryingFlag: carrying);

        private protected IEnumerable<GameObject> DroppedFlagsForSI()
        {
            _siDroppedFlags.RemoveWhere(guid => GetBgMap().GetGameObject(guid) == null);
            foreach (var guid in _siDroppedFlags)
                if (GetBgMap().GetGameObject(guid) is { } flag && flag.IsSpawned()) yield return flag;
        }

        private protected BattlegroundObjective FlagStandForSI(int entry, ObjectGuid guid, string label, int priority)
        {
            var flag = GetBgMap().GetGameObject(guid);
            if (flag != null) return ObjectiveForSI(flag, label, BattlegroundObjectiveAction.Interact, priority);
            var spawn = ObjectSpawnsForSI().FirstOrDefault(spawn => spawn.Id == entry);
            return spawn == null ? null : new("flag:" + spawn.SpawnId, label, spawn.SpawnPoint,
                BattlegroundObjectiveAction.Interact, default, priority, spawn.SpawnId);
        }

        private protected IEnumerable<BattlegroundObjective> CapturePointsForSI(Player player, IEnumerable<ObjectGuid> livePoints)
        {
            var known = new HashSet<long>();
            foreach (var guid in livePoints)
                if (GetBgMap().GetGameObject(guid) is { } point)
                {
                    known.Add(point.GetSpawnId());
                    yield return CapturePointForSI(player, point.GetGoInfo(), point.GetPosition(), guid, point.GetSpawnId());
                }
            // Destinations remain available when their grids have not been visited yet.
            foreach (var spawn in ObjectSpawnsForSI())
                if (!known.Contains(spawn.SpawnId) && Global.ObjectMgr.GetGameObjectTemplate(spawn.Id) is { type: GameObjectTypes.CapturePoint } template)
                    yield return CapturePointForSI(player, template, spawn.SpawnPoint, default, spawn.SpawnId);
        }

        BattlegroundObjective CapturePointForSI(Player player, GameObjectTemplate template, Position pos, ObjectGuid guid, long spawnId)
        {
            var state = (BattlegroundCapturePointState)(int)GetBgMap().GetWorldStateValue(template.CapturePoint.worldState1);
            bool alliance = GetPlayerTeam(player.GetGUID()) == Team.Alliance;
            bool held = state == (alliance ? BattlegroundCapturePointState.AllianceCaptured : BattlegroundCapturePointState.HordeCaptured);
            bool assault = state == (alliance ? BattlegroundCapturePointState.ContestedAlliance : BattlegroundCapturePointState.ContestedHorde);
            bool contested = state is BattlegroundCapturePointState.ContestedAlliance or BattlegroundCapturePointState.ContestedHorde;
            return new("point:" + template.entry, (held || assault ? "Defend " : "Capture ") + template.name,
                new Position(pos), held || assault ? BattlegroundObjectiveAction.Defend : BattlegroundObjectiveAction.Interact,
                guid, held ? 65 : assault ? 40 : contested ? 0 : 15, spawnId);
        }

        private protected IEnumerable<BattlegroundObjective> CreaturesForSI(IEnumerable<int> entries, string label, int priority)
        {
            var wanted = entries.ToHashSet();
            _siCreatureSpawns ??= Global.ObjectMgr.GetAllCreatureData().Values.Where(spawn => spawn.MapId == GetMapId()).ToArray();
            foreach (var spawn in _siCreatureSpawns)
            {
                if (!wanted.Contains(spawn.Id)) continue;
                if (spawn.spawnGroupData != null && !GetBgMap().IsSpawnGroupActive(spawn.spawnGroupData.groupId)) continue;
                var creature = GetBgMap().GetCreatureBySpawnId(spawn.SpawnId);
                if (creature != null && (!creature.IsAlive() || !creature.IsInWorld)) continue;
                yield return new("creature:" + spawn.SpawnId, label, creature == null ? spawn.SpawnPoint : new Position(creature),
                    BattlegroundObjectiveAction.Attack, creature?.GetGUID() ?? default, priority, spawn.SpawnId);
            }
        }
    }
}

namespace Game.BattleGrounds.Zones.WarsongGluch
{
    partial class BgWarsongGluch
    {
        internal override IEnumerable<BattlegroundObjective> ObjectivesForSI(Player player)
        {
            int team = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID())), enemy = 1 - team;
            if (GetFlagCarrierGUID(enemy) == player.GetGUID())
            {
                WorldObject home = GetBgMap().GetAreaTrigger(_capturePointAreaTriggers[team]) ??
                    (WorldObject)GetBgMap().GetGameObject(_flags[team]);
                if (home != null)
                    yield return ObjectiveForSI(home, "Bring the enemy flag home; wait for our flag to return if necessary",
                        BattlegroundObjectiveAction.Move, 0, true);
                else if (FlagStandForSI(GetMapId() == 489 ? team == 0 ? 179830 : 179831 : team == 0 ? GameobjectIds.AllianceFlagInBase : GameobjectIds.HordeFlagInBase,
                    _flags[team], "Bring the enemy flag home", 0) is { } fallback)
                    yield return fallback with { Action = BattlegroundObjectiveAction.Move, CarryingFlag = true };
                yield break;
            }
            var drops = DroppedFlagsForSI().ToArray();
            int DropTeam(GameObject drop) => GetMapId() == 489 ? ClassicFlagTeam(drop.GetEntry()) :
                drop.GetOwnerGUID() == _flags[0] ? 0 : drop.GetOwnerGUID() == _flags[1] ? 1 : -1;
            var ownDrop = drops.FirstOrDefault(drop => DropTeam(drop) == team);
            var enemyDrop = drops.FirstOrDefault(drop => DropTeam(drop) == enemy);
            var thief = Global.ObjAccessor.FindPlayer(GetFlagCarrierGUID(team));
            if (thief?.GetMap() != GetBgMap()) thief = null;
            var carrier = Global.ObjAccessor.FindPlayer(GetFlagCarrierGUID(enemy));
            if (carrier?.GetMap() != GetBgMap()) carrier = null;

            // Rank living SI teammates rather than relying on GUID parity or
            // objective scores: a real half-team split must survive uneven IDs.
            var teammates = GetPlayers().Keys.Select(Global.ObjAccessor.FindPlayer)
                .Where(p => p != null && p.Controller != null && p.IsAlive() && p.GetMap() == GetBgMap() &&
                    p.GetGUID() != carrier?.GetGUID() && GetPlayerTeam(p.GetGUID()) == GetPlayerTeam(player.GetGUID()))
                .Select(p => p.GetGUID()).Append(player.GetGUID()).Distinct().OrderBy(g => g.GetCounter()).ToArray();
            var assignment = SIBattlegroundPolicy.WarsongAssignment(Array.IndexOf(teammates, player.GetGUID()),
                teammates.Length, carrier != null, thief != null || ownDrop != null);
            BattlegroundObjective recover = ownDrop != null
                ? ObjectiveForSI(ownDrop, "Return our dropped flag", BattlegroundObjectiveAction.Interact, 0)
                : thief != null ? ObjectiveForSI(thief, "Recover our flag from the enemy carrier", BattlegroundObjectiveAction.Attack, 0) : null;
            if (assignment == SIWarsongAssignment.Recover && recover != null)
            { yield return recover; yield break; }
            if (carrier != null)
            {
                yield return ObjectiveForSI(carrier, "Escort and protect our flag carrier", BattlegroundObjectiveAction.Escort, 0);
                yield break;
            }
            if (enemyDrop != null)
            { yield return ObjectiveForSI(enemyDrop, "Recover the enemy flag", BattlegroundObjectiveAction.Interact, 0); yield break; }
            if (FlagStandForSI(GetMapId() == 489 ? enemy == 0 ? 179830 : 179831 : enemy == 0 ? GameobjectIds.AllianceFlagInBase : GameobjectIds.HordeFlagInBase,
                _flags[enemy], "Take the enemy flag", 0) is { } stand)
                yield return GetFlagState(enemy) == FlagState.InBase ? stand : stand with
                { Action = BattlegroundObjectiveAction.Move, Label = "Advance to the enemy flag room while the flags reset" };
        }

        // Leaving either flag room for somewhere far: go out through this player's exit of that base, so the
        // team splits between both exits instead of all taking the shortest one.
        internal override Position ExitWaypointForSI(Player player, Position destination)
        {
            if (GetMapId() != 489) return null;
            for (int team = 0; team < 2; ++team)
            {
                var stand = ObjectSpawnsForSI().FirstOrDefault(spawn => spawn.Id == (team == 0 ? 179830 : 179831))?.SpawnPoint;
                if (stand == null || player.GetExactDist2d(stand) > 60 || destination.GetExactDist2d(stand) < 80) continue;
                var exits = ExitGatesForSI(team);
                return exits.Length == 0 ? null : exits[ExitIndexForSI(player, exits.Length)].Out;
            }
            return null;
        }
    }
}

namespace Game.BattleGrounds.Zones.ArathisBasin
{
    partial class BgArathiBasin
    {
        internal override IEnumerable<BattlegroundObjective> ObjectivesForSI(Player player) =>
            GetMapId() == 529 ? ClassicObjectivesForSI(player) : CapturePointsForSI(player, _capturePoints);
    }
}

namespace Game.BattleGrounds.Zones.DarkspearIslands
{
    partial class BgDarkspearIslands
    {
        internal override IEnumerable<BattlegroundObjective> ObjectivesForSI(Player player)
        {
            int team = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID()));
            var carrier = FlagCarrier();
            if (carrier == player)
            {
                for (int node = 0; node < Nodes.Count; ++node)
                    if (NodeOwner(node) == team)
                        yield return new("score:" + node, "Deliver the flag to " + Nodes.All[node].Name, Nodes.All[node].Position,
                            BattlegroundObjectiveAction.Move, default, 0, CarryingFlag: true);
                // With no owned base the carrier must help secure one, while keeping the flag.
                if (ControlledBaseCount(team) != 0) yield break;
            }
            var points = new List<ObjectGuid>();
            for (int node = 0; node < Nodes.Count; ++node)
            {
                var point = GetBGObject(ObjectTypes.CapturePoint + node);
                if (point != null) points.Add(point.GetGUID());
            }
            foreach (var objective in CapturePointsForSI(player, points))
                yield return objective with { CarryingFlag = carrier == player };
            if (carrier == player) yield break;
            foreach (var drop in DroppedFlagsForSI())
                yield return ObjectiveForSI(drop, "Recover the Darkspear flag", BattlegroundObjectiveAction.Interact, 10);
            if (carrier != null)
                yield return ObjectiveForSI(carrier, GetPlayerTeam(carrier.GetGUID()) == GetPlayerTeam(player.GetGUID()) ? "Escort our flag carrier" : "Stop the enemy flag carrier",
                    GetPlayerTeam(carrier.GetGUID()) == GetPlayerTeam(player.GetGUID()) ? BattlegroundObjectiveAction.Escort : BattlegroundObjectiveAction.Attack, 10);
            else if (ControlledBaseCount(team) > 0 && GetBgMap().GetGameObject(_flagGUID)?.GetFlagState() == FlagState.InBase &&
                FlagStandForSI(GameobjectIds.Flag, _flagGUID, "Take the Darkspear flag", 20) is { } stand)
                yield return stand;
        }
    }
}

namespace Game.BattleGrounds.Zones.AlteracValley
{
    partial class BgAlteracValley
    {
        // Classic map 30 reuses banner entries at several nodes. Their location,
        // rather than the shared entry, identifies the graveyard or tower.
        internal static readonly Position[] ClassicNodePositionsForSI =
        [
            new(638.6f, -32.3f, 46.1f), new(669.2f, -294.1f, 30.3f), new(77.7f, -404.6f, 46.9f),
            new(-202.6f, -112.6f, 78.5f), new(-612.3f, -396.4f, 60.9f), new(-1082.5f, -346.7f, 54.9f),
            new(-1402.2f, -307.5f, 89.4f), new(553.8f, -78.7f, 51.9f), new(674, -143.1f, 63.7f),
            new(203.2f, -360.3f, 56.4f), new(-152.4f, -441.6f, 40.4f), new(-571.9f, -262.8f, 75),
            new(-768.9f, -363.7f, 90.9f), new(-1302.9f, -317, 113.9f), new(-1297.7f, -266.7f, 114.2f)
        ];

        internal override IEnumerable<BattlegroundObjective> ObjectivesForSI(Player player)
        {
            Team team = GetPlayerTeam(player.GetGUID());
            bool alliance = team == Team.Alliance;
            int Node(GameObjectData spawn) => GetMapId() == 30
                ? Enumerable.Range(0, ClassicNodePositionsForSI.Length).MinBy(i => spawn.SpawnPoint.GetExactDist2d(ClassicNodePositionsForSI[i]))
                : (int)GetNodeThroughObject(spawn.Id);
            bool Banner(GameObjectData spawn) => spawn.Id is 178925 or 178943 or 178940 or 179435 or 178365 or 178364 or 179286 or 179287 or 180418 ||
                Enum.IsDefined(typeof(GameObjectIds), spawn.Id) && ((GameObjectIds)spawn.Id).ToString().EndsWith("ControlledBanner", StringComparison.Ordinal);
            foreach (var group in ObjectSpawnsForSI().Where(Banner).GroupBy(Node))
            {
                var node = _nodes[group.Key];
                if (node.State == AVStates.PointDestroyed) continue;
                var active = group.Where(s => s.spawnGroupData == null || GetBgMap().IsSpawnGroupActive(s.spawnGroupData.groupId)).ToArray();
                if (active.Length == 0) continue;
                var spawn = active.FirstOrDefault(s => GetBgMap().GetGameObjectBySpawnId(s.SpawnId)?.IsSpawned() == true) ?? active[0];
                var banner = GetBgMap().GetGameObjectBySpawnId(spawn.SpawnId);
                bool own = node.Owner == team;
                bool contested = node.State == AVStates.PointAssaulted;
                yield return new("av-node:" + group.Key, (own ? "Defend " : "Assault ") + ((AVNodes)group.Key),
                    spawn.SpawnPoint, own ? BattlegroundObjectiveAction.Defend : BattlegroundObjectiveAction.Interact,
                    banner?.GetGUID() ?? default, own ? contested ? 35 : 75 : contested && node.PrevOwner == team ? 0 : node.Tower ? 15 : 25, spawn.SpawnId);
            }
            foreach (var objective in CreaturesForSI([alliance ? (int)CreatureIds.Galvangar : (int)CreatureIds.Balinda], "Defeat the enemy captain", 30))
                yield return objective;
            if (_mineInfo[0].Owner != team)
                foreach (var objective in CreaturesForSI([(int)CreatureIds.Morloch, (int)CreatureIds.UmiThorson, (int)CreatureIds.Keetar], "Capture Irondeep Mine", 40))
                    yield return objective;
            if (_mineInfo[1].Owner != team)
                foreach (var objective in CreaturesForSI([(int)CreatureIds.TaskmasterSnivvle, (int)CreatureIds.AgiRumblestomp, (int)CreatureIds.MashaSwiftcut], "Capture Coldtooth Mine", 40))
                    yield return objective;
            bool enemyTowersRemain = _nodes.Any(n => n.Tower && n.TotalOwner != team && n.State != AVStates.PointDestroyed);
            foreach (var objective in CreaturesForSI([alliance ? (int)CreatureIds.Drekthar : (int)CreatureIds.Vanndar],
                "Defeat the enemy general", enemyTowersRemain ? 85 : 0))
                yield return objective;
        }
    }
}
