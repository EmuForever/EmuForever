using Framework.Constants;
using Game.Entities;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.BattleGrounds
{
    internal sealed class ClassicBattlegroundPoint
    {
        internal BattlegroundCapturePointState State = BattlegroundCapturePointState.Neutral;
        internal int LastOwner = BattleGroundTeamId.Neutral;
        internal TimeSpan Remaining;
        internal bool Assault(int team)
        {
            var held = team == 0 ? BattlegroundCapturePointState.AllianceCaptured : BattlegroundCapturePointState.HordeCaptured;
            var contested = team == 0 ? BattlegroundCapturePointState.ContestedAlliance : BattlegroundCapturePointState.ContestedHorde;
            if (State == held || State == contested) return false;
            State = LastOwner == team ? held : contested;
            Remaining = LastOwner == team ? TimeSpan.Zero : TimeSpan.FromMinutes(1);
            return true;
        }
        internal bool Update(TimeSpan diff)
        {
            if (Remaining <= TimeSpan.Zero) return false;
            Remaining -= diff;
            if (Remaining > TimeSpan.Zero) return false;
            LastOwner = State == BattlegroundCapturePointState.ContestedAlliance ? 0 : 1;
            State = LastOwner == 0 ? BattlegroundCapturePointState.AllianceCaptured : BattlegroundCapturePointState.HordeCaptured;
            Remaining = TimeSpan.Zero;
            return true;
        }
    }
}

namespace Game.BattleGrounds.Zones.ArathisBasin
{
    partial class BgArathiBasin
    {
        readonly ClassicBattlegroundPoint[] _classicPoints = Enumerable.Range(0, 5).Select(_ => new ClassicBattlegroundPoint()).ToArray();
        static readonly int[][] ClassicPointWorldStates =
        [
            [1842, 1767, 1768, 1769, 1770], [1846, 1782, 1783, 1784, 1785], [1845, 1772, 1773, 1774, 1775],
            [1844, 1792, 1793, 1794, 1795], [1843, 1787, 1788, 1789, 1790]
        ];
        static readonly int[] ClassicNeutralBanners = [180087, 180088, 180089, 180090, 180091];
        GameObjectData[] ClassicPointSpawns => ObjectSpawnsForSI().Where(s => ClassicNeutralBanners.Contains(s.Id))
            .OrderBy(s => Array.IndexOf(ClassicNeutralBanners, s.Id)).ToArray();

        public override void EventPlayerClickedOnFlag(Player player, GameObject target)
        {
            if (GetMapId() != 529 || GetStatus() != BattlegroundStatus.InProgress || !player.CanUseBattlegroundObject(null)) return;
            var spawns = ClassicPointSpawns;
            if (spawns.Length != 5) return;
            int index = Enumerable.Range(0, 5).MinBy(i => target.GetExactDist2d(spawns[i].SpawnPoint));
            if (target.GetExactDist2d(spawns[index].SpawnPoint) > 15) return;
            int team = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID()));
            var point = _classicPoints[index];
            bool defended = point.LastOwner == team;
            if (!point.Assault(team)) return;
            UpdatePlayerScore(player, defended ? ScoreType.BasesDefended : ScoreType.BasesAssaulted, 1);
            UpdateClassicPoint(index);
        }

        void TickClassicPoints(TimeSpan diff)
        {
            if (GetMapId() != 529 || GetStatus() != BattlegroundStatus.InProgress) return;
            for (int i = 0; i < 5; ++i)
                if (_classicPoints[i].Update(diff)) UpdateClassicPoint(i);
        }

        void UpdateClassicPoint(int index)
        {
            var state = _classicPoints[index].State;
            int active = state switch
            {
                BattlegroundCapturePointState.AllianceCaptured => 1, BattlegroundCapturePointState.HordeCaptured => 2,
                BattlegroundCapturePointState.ContestedAlliance => 3, BattlegroundCapturePointState.ContestedHorde => 4, _ => 0
            };
            for (int i = 0; i < 5; ++i) UpdateWorldState(ClassicPointWorldStates[index][i], active == i ? 1 : 0);
            int groupState = state switch
            {
                BattlegroundCapturePointState.ContestedAlliance => 1, BattlegroundCapturePointState.ContestedHorde => 2,
                BattlegroundCapturePointState.AllianceCaptured => 3, BattlegroundCapturePointState.HordeCaptured => 4, _ => 0
            };
            for (int i = 0; i < 6; ++i) SetClassicSpawnGroup(529000 + index * 10 + i, i == groupState || i == 5 && groupState is 1 or 2);
            if (index == 2)
            { SetClassicSpawnGroup(529050, groupState == 3); SetClassicSpawnGroup(529051, groupState == 4); }
            GetBgMap().UpdateSpawnGroupConditions();
        }

        IEnumerable<BattlegroundObjective> ClassicObjectivesForSI(Player player)
        {
            bool alliance = GetPlayerTeam(player.GetGUID()) == Team.Alliance;
            var spawns = ClassicPointSpawns;
            for (int i = 0; i < spawns.Length; ++i)
            {
                var state = _classicPoints[i].State;
                bool held = state == (alliance ? BattlegroundCapturePointState.AllianceCaptured : BattlegroundCapturePointState.HordeCaptured);
                bool assault = state == (alliance ? BattlegroundCapturePointState.ContestedAlliance : BattlegroundCapturePointState.ContestedHorde);
                int entry = state switch
                {
                    BattlegroundCapturePointState.AllianceCaptured => 180058, BattlegroundCapturePointState.HordeCaptured => 180060,
                    BattlegroundCapturePointState.ContestedAlliance => 180059, BattlegroundCapturePointState.ContestedHorde => 180061, _ => spawns[i].Id
                };
                var spawn = ObjectSpawnsForSI().FirstOrDefault(s => s.Id == entry && s.SpawnPoint.GetExactDist2d(spawns[i].SpawnPoint) < 15) ?? spawns[i];
                var banner = GetBgMap().GetGameObjectBySpawnId(spawn.SpawnId);
                yield return new("classic-ab:" + i, (held || assault ? "Defend " : "Capture ") + Global.ObjectMgr.GetGameObjectTemplate(spawns[i].Id)?.name,
                    spawn.SpawnPoint, held || assault ? BattlegroundObjectiveAction.Defend : BattlegroundObjectiveAction.Interact,
                    banner?.GetGUID() ?? default, held ? 65 : assault ? 40 : _classicPoints[i].LastOwner == (alliance ? 0 : 1) ? 0 : 15, spawn.SpawnId);
            }
        }
    }
}

namespace Game.BattleGrounds.Zones.WarsongGluch
{
    partial class BgWarsongGluch
    {
        readonly FlagState[] _classicFlagState = [FlagState.InBase, FlagState.InBase];
        readonly ObjectGuid[] _classicFlagCarrier = new ObjectGuid[2];
        readonly ObjectGuid[] _classicFlagDrop = new ObjectGuid[2];
        readonly TimeSpan[] _classicFlagTimer = new TimeSpan[2];
        bool _classicFlagReset;
        internal static int ClassicFlagAura(int flagTeam) => flagTeam == 0 ? 23335 : 23333;
        internal static int ClassicFlagTeam(int entry) => entry is 179830 or 179785 ? 0 : entry is 179831 or 179786 ? 1 : -1;

        public override void EventPlayerClickedOnFlag(Player player, GameObject target)
        {
            if (GetMapId() != 489 || GetStatus() != BattlegroundStatus.InProgress || !player.CanUseBattlegroundObject(null)) return;
            int flagTeam = ClassicFlagTeam(target.GetEntry());
            if (flagTeam < 0) return;
            bool drop = target.GetEntry() is 179785 or 179786;
            if (_classicFlagState[flagTeam] != (drop ? FlagState.Dropped : FlagState.InBase)) return;
            if (drop && _classicFlagDrop[flagTeam] != target.GetGUID()) return;
            int team = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID()));
            if (flagTeam == team)
            {
                if (!drop) return;
                ResetClassicFlag(flagTeam);
                UpdatePlayerScore(player, ScoreType.FlagReturns, 1);
                SendBroadcastText(flagTeam == 0 ? BroadcastTextIds.AllianceFlagReturned : BroadcastTextIds.HordeFlagReturned,
                    flagTeam == 0 ? ChatMsg.BgSystemAlliance : ChatMsg.BgSystemHorde, player);
                PlaySoundToAll(SoundIds.FlagReturned);
                return;
            }
            player.RemoveAurasByType(AuraType.Mounted);
            player.RemoveAurasWithInterruptFlags(SpellAuraInterruptFlags.PvPActive);
            player.CastSpell(player, ClassicFlagAura(flagTeam), true);
            if (!player.HasAura(ClassicFlagAura(flagTeam))) return;
            _classicFlagState[flagTeam] = FlagState.Taken;
            _classicFlagCarrier[flagTeam] = player.GetGUID();
            _classicFlagTimer[flagTeam] = TimeSpan.Zero;
            if (drop) target.Delete();
            _classicFlagDrop[flagTeam] = default;
            AddPlayerPosition(new BattlegroundPlayerPosition
            {
                Guid = player.GetGUID(), Pos = player.GetPosition(),
                IconID = flagTeam == 0 ? BattlegroundConst.PlayerPositionIconAllianceFlag : BattlegroundConst.PlayerPositionIconHordeFlag
            });
            SetClassicSpawnGroup(489001 + flagTeam, false);
            UpdateFlagState(flagTeam == 0 ? Team.Alliance : Team.Horde, FlagState.Taken);
            SendBroadcastText(flagTeam == 0 ? BroadcastTextIds.AllianceFlagPickedUp : BroadcastTextIds.HordeFlagPickedUp,
                flagTeam == 0 ? ChatMsg.BgSystemHorde : ChatMsg.BgSystemAlliance, player);
            PlaySoundToAll(flagTeam == 0 ? SoundIds.AllianceFlagPickedUp : SoundIds.HordeFlagPickedUp);
        }

        public override void EventPlayerDroppedFlag(Player player)
        {
            if (GetMapId() != 489 || _classicFlagReset) return;
            for (int team = 0; team < 2; ++team)
            {
                if (_classicFlagCarrier[team] != player.GetGUID()) continue;
                if (GetStatus() != BattlegroundStatus.InProgress) { ResetClassicFlag(team); continue; }
                RemovePlayerPosition(player.GetGUID());
                _classicFlagCarrier[team] = default;
                _classicFlagState[team] = FlagState.Dropped;
                _classicFlagTimer[team] = TimeSpan.FromSeconds(10);
                player.RemoveAurasDueToSpell(ClassicFlagAura(team));
                if (Global.SpellMgr.HasSpellInfo(BattlegroundConst.SpellRecentlyDroppedAllianceFlag, player.GetMap().GetDifficultyID()))
                    player.CastSpell(player, BattlegroundConst.SpellRecentlyDroppedAllianceFlag, true);
                // A dropped flag belongs to the match, not the player who died.
                // Player summons register ownership; changing that owner to a
                // flag stand is invalid and can also remove the drop on release.
                var drop = GameObject.CreateGameObject(team == 0 ? 179785 : 179786, GetBgMap(),
                    player.GetPosition(), Quaternion.Identity, 255, GameObjectState.Ready);
                if (drop != null)
                {
                    PhasingHandler.InheritPhaseShift(drop, player);
                    drop.SetRespawnTime(TimeSpan.FromSeconds(10)); drop.SetSpawnedByDefault(false);
                    _classicFlagDrop[team] = drop.GetGUID();
                    if (GetBgMap().AddToMap(drop)) TrackObjectiveObjectForSI(drop);
                    else { drop.Dispose(); ResetClassicFlag(team); continue; }
                }
                else { ResetClassicFlag(team); continue; }
                UpdateFlagState(team == 0 ? Team.Alliance : Team.Horde, FlagState.Dropped);
            }
        }

        void ResetClassicFlag(int team)
        {
            RemovePlayerPosition(_classicFlagCarrier[team]);
            _classicFlagReset = true;
            try { Global.ObjAccessor.FindPlayer(_classicFlagCarrier[team])?.RemoveAurasDueToSpell(ClassicFlagAura(team)); }
            finally { _classicFlagReset = false; }
            GetBgMap().GetGameObject(_classicFlagDrop[team])?.Delete();
            _classicFlagCarrier[team] = _classicFlagDrop[team] = default;
            _classicFlagTimer[team] = TimeSpan.Zero; _classicFlagState[team] = FlagState.InBase;
            SetClassicSpawnGroup(489001 + team, true);
            UpdateFlagState(team == 0 ? Team.Alliance : Team.Horde, FlagState.InBase);
        }

        void TickClassicFlags(TimeSpan diff)
        {
            if (GetMapId() != 489 || GetStatus() != BattlegroundStatus.InProgress) return;
            for (int flag = 0; flag < 2; ++flag)
            {
                if (_classicFlagState[flag] is FlagState.Dropped or FlagState.Respawning)
                { _classicFlagTimer[flag] -= diff; if (_classicFlagTimer[flag] <= TimeSpan.Zero) ResetClassicFlag(flag); }
                if (_classicFlagState[flag] != FlagState.Taken) continue;
                var carrier = Global.ObjAccessor.FindPlayer(_classicFlagCarrier[flag]);
                if (carrier == null || carrier.GetMap() != GetBgMap() || !IsPlayerInBattleground(carrier.GetGUID()))
                { ResetClassicFlag(flag); continue; }
                if (!carrier.IsAlive() || !carrier.HasAura(ClassicFlagAura(flag))) { EventPlayerDroppedFlag(carrier); continue; }
                int home = 1 - flag;
                var stand = GetBgMap().GetGameObject(_flags[home]);
                if (stand == null || _classicFlagState[home] != FlagState.InBase || carrier.GetExactDist2d(stand) > 8 ||
                    Math.Abs(carrier.GetPositionZ() - stand.GetPositionZ()) > 5) continue;
                var winner = GetPlayerTeam(carrier.GetGUID());
                ResetClassicFlag(flag);
                AddPoint(winner); UpdateTeamScore(home); SetLastFlagCapture(winner);
                UpdatePlayerScore(carrier, ScoreType.FlagCaptures, 1);
                RewardHonorToTeam(GetBonusHonorFromKill(2), winner);
                RewardReputationToTeam(winner == Team.Alliance ? 890 : 889, m_ReputationCapture, winner);
                SendBroadcastText(winner == Team.Alliance ? BroadcastTextIds.CapturedHordeFlag : BroadcastTextIds.CapturedAllianceFlag,
                    winner == Team.Alliance ? ChatMsg.BgSystemAlliance : ChatMsg.BgSystemHorde, carrier);
                PlaySoundToAll(winner == Team.Alliance ? SoundIds.FlagCapturedAlliance : SoundIds.FlagCapturedHorde);
                if (GetTeamScore(home) >= MiscConst.MaxTeamScore) EndBattleground(winner);
                else
                    for (int team = 0; team < 2; ++team)
                    {
                        _classicFlagState[team] = FlagState.Respawning;
                        _classicFlagTimer[team] = TimeSpan.FromMilliseconds(MiscConst.FlagRespawnTime);
                        SetClassicSpawnGroup(489001 + team, false);
                        UpdateFlagState(team == 0 ? Team.Alliance : Team.Horde, FlagState.Respawning);
                    }
            }
        }
    }
}

namespace Game.BattleGrounds.Zones.AlteracValley
{
    partial class BgAlteracValley
    {
        AVNodes GetNodeThroughObject(GameObject obj) => GetMapId() == 30
            ? (AVNodes)Enumerable.Range(0, ClassicNodePositionsForSI.Length).MinBy(i => obj.GetExactDist2d(ClassicNodePositionsForSI[i]))
            : GetNodeThroughObject(obj.GetEntry());

        void SyncClassicAVGroups()
        {
            if (GetMapId() != 30) return;
            foreach (int group in Global.ObjectMgr.GetSpawnGroupsForMap(30))
            {
                if (group < 300000 || group > 302539) continue;
                int domain = (group - 300000) / 10, variant = (group - 300000) % 10;
                bool active = false;
                if (domain < 15)
                {
                    var node = _nodes[domain];
                    int state = node.Owner == Team.Other ? 5 : node.Owner == Team.Alliance
                        ? node.State == AVStates.PointAssaulted ? 0 : 1 : node.State == AVStates.PointAssaulted ? 2 : 3;
                    active = node.State != AVStates.PointDestroyed && variant == state;
                }
                else if (domain is >= 15 and <= 21)
                {
                    var node = _nodes[domain - 15];
                    int state = node.Owner == Team.Other ? 8 : node.Owner == Team.Alliance
                        ? GetData(MiscConst.DataDefenderTierAlliance) : 4 + GetData(MiscConst.DataDefenderTierHorde);
                    active = node.State == AVStates.PointControled && variant == state;
                }
                else if (domain is >= 22 and <= 29)
                {
                    var node = _nodes[domain - 15];
                    active = node.State == AVStates.PointControled && variant == (node.Owner == Team.Alliance ? 1 : 3);
                }
                else if (domain is >= 30 and <= 37)
                {
                    var node = _nodes[domain - 23];
                    int tier = node.Owner == Team.Alliance ? GetData(MiscConst.DataDefenderTierAlliance) : 4 + GetData(MiscConst.DataDefenderTierHorde);
                    active = node.State == AVStates.PointControled && variant == tier;
                }
                else if (domain is 46 or 47 or 50 or 51)
                {
                    Team owner = _mineInfo[domain is 46 or 50 ? 0 : 1].Owner;
                    active = variant == (owner == Team.Alliance ? 0 : owner == Team.Horde ? 1 : 2);
                }
                else if (domain is 48 or 49 or >= 52 and <= 62 or >= 65 and <= 67 or 100 or 101)
                    active = true;
                else if (domain == 253) active = GetStatus() != BattlegroundStatus.InProgress;
                SetClassicSpawnGroup(group, active);
            }
        }
    }
}
