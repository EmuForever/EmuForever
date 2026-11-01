using Framework.Constants;
using Game.Entities;
using Game.Entities.GameObjectType;
using Game.Networking.Packets;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Game.BattleGrounds.Zones.DarkspearIslands
{
    partial class BgDarkspearIslands : Battleground
    {
        TimeTracker _pointsTimer;
        TimeTracker _flagCheckTimer;
        int[] _honorScoreTics = new int[SharedConst.PvpTeamsCount];
        int[] _reputationScoreTics = new int[SharedConst.PvpTeamsCount];
        int _honorTics, _reputationTics;
        bool _nearVictoryAnnounced;
        ObjectGuid _flagGUID;
        List<ObjectGuid> _doors = new();

        public BgDarkspearIslands(BattlegroundTemplate battlegroundTemplate) : base(battlegroundTemplate)
        {
            BgObjects = new ObjectGuid[ObjectTypes.Max];
            BgCreatures = new ObjectGuid[CreatureTypes.Max];
            _pointsTimer = new(Misc.PointsTickTime);
            _flagCheckTimer = new(Misc.FlagCheckTime);
        }

        public override bool SetupBattleground()
        {
            for (int node = 0; node < Nodes.Count; ++node)
            {
                NodeInfo info = Nodes.All[node];
                if (!AddObject(ObjectTypes.CapturePoint + node, info.CapturePointEntry, info.Position, 0, 0, 0, 0)
                    || !AddObject(ObjectTypes.Aura + node, GameobjectIds.NeutralAura, info.Position, 0, 0, 0, 0))
                    return false;
                UpdateWorldState(info.StateWorldState, (int)BattlegroundCapturePointState.Neutral);
                UpdateWorldState(info.IconWorldStateAlliance, 0);
                UpdateWorldState(info.IconWorldStateHorde, 0);
            }

            if (!AddObject(ObjectTypes.Flag, GameobjectIds.Flag, Misc.FlagPosition, 0, 0, 0, 0)
                || !AddObject(ObjectTypes.SkullPile, GameobjectIds.SkullPile, Misc.SkullPilePosition, 0, 0, 0, 0))
                return false;
            UpdateWorldState(WorldStateIds.FlagIcon, 1);
            UpdateWorldState(WorldStateIds.AllianceBases, 0);
            UpdateWorldState(WorldStateIds.HordeBases, 0);
            UpdateWorldState(WorldStateIds.AllianceFlagCarried, IconStates.Hidden);
            UpdateWorldState(WorldStateIds.HordeFlagCarried, IconStates.Hidden);

            if (!AddSpiritGuide(CreatureTypes.SpiritMainAlliance, Misc.SpiritGuideAlliance, BattleGroundTeamId.Alliance)
                || !AddSpiritGuide(CreatureTypes.SpiritMainHorde, Misc.SpiritGuideHorde, BattleGroundTeamId.Horde))
                return false;

            UpdateWorldState(WorldStateIds.MaxResources, Misc.MaxTeamScore);
            UpdateWorldState(WorldStateIds.AllianceResources, 0);
            UpdateWorldState(WorldStateIds.HordeResources, 0);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            m_TeamScores[BattleGroundTeamId.Alliance] = 0;
            m_TeamScores[BattleGroundTeamId.Horde] = 0;
            _honorScoreTics = [0, 0];
            _reputationScoreTics = [0, 0];
            _nearVictoryAnnounced = false;
            _flagGUID = ObjectGuid.Empty;
            _pointsTimer.Reset(Misc.PointsTickTime);
            bool bonusWeek = Global.BattlegroundMgr.IsBGWeekend(GetTypeID()) || Systems.PvP.ForeverPvpRanks.CallToArmsMapId() == GetMapId();
            _honorTics = bonusWeek ? Misc.BonusHonorTicks : Misc.HonorTicks;
            _reputationTics = bonusWeek ? Misc.BonusReputationTicks : Misc.ReputationTicks;
        }

        public override void AddPlayer(Player player, BattlegroundQueueTypeId queueId)
        {
            bool isInBattleground = IsPlayerInBattleground(player.GetGUID());
            base.AddPlayer(player, queueId);
            if (!isInBattleground)
                PlayerScores[player.GetGUID()] = new BgDarkspearIslandsScore(player.GetGUID(), player.GetBGTeam());
        }

        // Map spawns (the `gameobject` table, map 2997): the start gates are collected here and opened when the battle begins.
        // Buffs need nothing from the script; they are plain trap spawns that respawn on their own.
        public override void OnGameObjectCreate(GameObject gameObject)
        {
            TrackObjectiveObjectForSI(gameObject);
            switch (gameObject.GetEntry())
            {
                case GameobjectIds.Flag:
                    _flagGUID = gameObject.GetGUID();
                    break;
                case GameobjectIds.AllianceGate:
                case GameobjectIds.HordeGate:
                    _doors.Add(gameObject.GetGUID());
                    if (GetStatus() == BattlegroundStatus.InProgress)   // grid loaded again after the start
                        gameObject.SetGoState(GameObjectState.Active);
                    break;
            }
        }

        // The gates stay open for the rest of the match. Using them as doors closed them again after the template's 3 s auto-close,
        // and despawning a NoDespawn map spawn with no respawn time put them straight back closed.
        public override void StartingEventOpenDoors()
        {
            int opened = 0;
            foreach (ObjectGuid door in _doors)
            {
                GameObject gameObject = GetBgMap().GetGameObject(door);
                if (gameObject == null)
                    continue;

                gameObject.SetGoState(GameObjectState.Active);
                ++opened;
            }

            if (opened != 2)
                Log.outError(LogFilter.Battleground, $"Darkspear Islands (instance {GetInstanceID()}): opened {opened} of 2 start gates.");
        }

        // "Bases: N" under each side's score: the scoreboard widget's text reads these (hotfixed UiWidgetStringSource 22292/22293).
        void UpdateBaseCounts()
        {
            UpdateWorldState(WorldStateIds.AllianceBases, ControlledBaseCount(BattleGroundTeamId.Alliance));
            UpdateWorldState(WorldStateIds.HordeBases, ControlledBaseCount(BattleGroundTeamId.Horde));
        }

        public override void PostUpdateImpl(TimeSpan diff)
        {
            if (GetStatus() != BattlegroundStatus.InProgress)
                return;

            _pointsTimer.Update(diff);
            if (_pointsTimer.Passed())
            {
                _pointsTimer.Reset(Misc.PointsTickTime);
                for (int team = 0; team < SharedConst.PvpTeamsCount; ++team)
                {
                    int bases = ControlledBaseCount(team);
                    if (bases > 0)
                        AddPoints(team, Misc.TickPoints[bases - 1]);
                }
            }

            // A carrier scores by reaching a base its side holds.
            _flagCheckTimer.Update(diff);
            if (_flagCheckTimer.Passed())
            {
                _flagCheckTimer.Reset(Misc.FlagCheckTime);
                UpdateBaseCounts();
                Player carrier = FlagCarrier();
                if (carrier != null && carrier.IsAlive())
                {
                    int team = GetTeamIndexByTeamId(GetPlayerTeam(carrier.GetGUID()));
                    for (int node = 0; node < Nodes.Count; ++node)
                        if (NodeOwner(node) == team && carrier.GetExactDist2d(Nodes.All[node].Position) <= Misc.FlagCaptureRadius)
                        {
                            CaptureFlag(carrier, team);
                            break;
                        }
                }
            }
        }

        void AddPoints(int team, int points)
        {
            m_TeamScores[team] = Math.Min(Misc.MaxTeamScore, m_TeamScores[team] + points);
            _honorScoreTics[team] += points;
            _reputationScoreTics[team] += points;

            Team teamId = team == BattleGroundTeamId.Alliance ? Team.Alliance : Team.Horde;
            while (_honorScoreTics[team] >= _honorTics)
            {
                RewardHonorToTeam(GetBonusHonorFromKill(1), teamId);
                _honorScoreTics[team] -= _honorTics;
            }
            while (_reputationScoreTics[team] >= _reputationTics)
            {
                RewardReputationToTeam(team == BattleGroundTeamId.Alliance ? Misc.ReputationAlliance : Misc.ReputationHorde, Misc.ReputationPerTick, teamId);
                _reputationScoreTics[team] -= _reputationTics;
            }

            UpdateWorldState(team == BattleGroundTeamId.Alliance ? WorldStateIds.AllianceResources : WorldStateIds.HordeResources, m_TeamScores[team]);

            if (!_nearVictoryAnnounced && m_TeamScores[team] >= Misc.WarningNearVictoryScore && m_TeamScores[team] < Misc.MaxTeamScore)
            {
                _nearVictoryAnnounced = true;
                SendText(team == BattleGroundTeamId.Alliance ? "The Alliance is near victory!" : "The Horde is near victory!", ChatMsg.BgSystemNeutral);
                PlaySoundToAll(team == BattleGroundTeamId.Alliance ? SoundIds.NearVictoryAlliance : SoundIds.NearVictoryHorde);
            }

            if (m_TeamScores[team] >= Misc.MaxTeamScore)
                EndBattleground(teamId);
        }

        int NodeOwner(int node)
        {
            switch ((BattlegroundCapturePointState)(int)GetBgMap().GetWorldStateValue(Nodes.All[node].StateWorldState))
            {
                case BattlegroundCapturePointState.AllianceCaptured: return BattleGroundTeamId.Alliance;
                case BattlegroundCapturePointState.HordeCaptured: return BattleGroundTeamId.Horde;
                default: return BattleGroundTeamId.Neutral;
            }
        }

        int ControlledBaseCount(int team)
        {
            int count = 0;
            for (int node = 0; node < Nodes.Count; ++node)
                if (NodeOwner(node) == team)
                    ++count;
            return count;
        }

        // Capture point events: ContestedHorde, CaptureHorde, DefendedHorde, ContestedAlliance, CaptureAlliance, DefendedAlliance.
        public override void ProcessEvent(WorldObject target, int eventId, WorldObject invoker)
        {
            int node = (eventId - EventIds.First) / EventIds.Stride;
            int kind = (eventId - EventIds.First) % EventIds.Stride;
            if (eventId < EventIds.First || node < 0 || node >= Nodes.Count || kind < EventIds.ContestedHorde || kind > EventIds.DefendedAlliance)
            {
                base.ProcessEvent(target, eventId, invoker);
                return;
            }

            NodeInfo info = Nodes.All[node];
            int team = kind >= EventIds.ContestedAlliance ? BattleGroundTeamId.Alliance : BattleGroundTeamId.Horde;
            string side = team == BattleGroundTeamId.Alliance ? "Alliance" : "Horde";
            ChatMsg chat = team == BattleGroundTeamId.Alliance ? ChatMsg.BgSystemAlliance : ChatMsg.BgSystemHorde;
            Player player = invoker?.ToPlayer();
            GameObject capturePoint = target?.ToGameObject() ?? GetBGObject(ObjectTypes.CapturePoint + node);

            switch (kind - (team == BattleGroundTeamId.Alliance ? EventIds.ContestedAlliance - EventIds.ContestedHorde : 0))
            {
                case EventIds.ContestedHorde:   // assaulted: banner contested, its graveyard closes for everyone
                    SetNodeVisual(node, capturePoint, team == BattleGroundTeamId.Alliance ? GameobjectIds.AllianceContestedBannerDisplay : GameobjectIds.HordeContestedBannerDisplay,
                        GameobjectIds.NeutralAura);
                    DelCreature(CreatureTypes.SpiritNode + node);
                    SetNodeIcons(info, team, IconStates.Assaulted);
                    SendText($"The {side} has assaulted the {info.Name}!", chat, player);
                    PlaySoundToAll(team == BattleGroundTeamId.Alliance ? SoundIds.NodeAssaultedAlliance : SoundIds.NodeAssaultedHorde);
                    if (player != null)
                        UpdatePlayerScore(player, ScoreType.BasesAssaulted, 1);
                    break;
                case EventIds.CaptureHorde:     // held for the full minute
                    TakeNode(node, team, capturePoint);
                    SendText($"The {side} has captured the {info.Name}!", chat);
                    break;
                case EventIds.DefendedHorde:    // retaken by its owner before the minute ran out
                    TakeNode(node, team, capturePoint);
                    SendText($"The {side} has defended the {info.Name}!", chat, player);
                    if (player != null)
                        UpdatePlayerScore(player, ScoreType.BasesDefended, 1);
                    break;
            }
        }

        void TakeNode(int node, int team, GameObject capturePoint)
        {
            NodeInfo info = Nodes.All[node];
            bool alliance = team == BattleGroundTeamId.Alliance;
            SetNodeVisual(node, capturePoint, alliance ? GameobjectIds.AllianceBannerDisplay : GameobjectIds.HordeBannerDisplay,
                alliance ? GameobjectIds.AllianceAura : GameobjectIds.HordeAura);
            SetNodeIcons(info, team, IconStates.Captured);
            DelCreature(CreatureTypes.SpiritNode + node);
            AddSpiritGuide(CreatureTypes.SpiritNode + node, info.SpiritGuidePosition, team);
            PlaySoundToAll(alliance ? SoundIds.NodeCapturedAlliance : SoundIds.NodeCapturedHorde);
        }

        // The scoreboard's icon row (widget 7860): each side shows the bases it has assaulted (state 1) or holds (state 2).
        void SetNodeIcons(NodeInfo info, int team, int state)
        {
            bool alliance = team == BattleGroundTeamId.Alliance;
            UpdateWorldState(alliance ? info.IconWorldStateAlliance : info.IconWorldStateHorde, state);
            UpdateWorldState(alliance ? info.IconWorldStateHorde : info.IconWorldStateAlliance, IconStates.Hidden);
        }

        // The capture point keeps its state; its model follows it (contested / Alliance / Horde banner) with the matching glow.
        void SetNodeVisual(int node, GameObject capturePoint, int displayId, int auraEntry)
        {
            capturePoint?.SetDisplayId(displayId);
            DelObject(ObjectTypes.Aura + node);
            AddObject(ObjectTypes.Aura + node, auraEntry, Nodes.All[node].Position, 0, 0, 0, 0);
        }

        Player FlagCarrier()
        {
            GameObject flag = GetBgMap().GetGameObject(_flagGUID);
            return flag == null ? null : Global.ObjAccessor.FindPlayer(flag.GetFlagCarrierGUID());
        }

        void CaptureFlag(Player carrier, int team)
        {
            GameObject flag = GetBgMap().GetGameObject(_flagGUID);
            if (flag == null)
                return;

            // Removed normally (not dropped): the flag goes back to its stand after the respawn time.
            carrier.RemoveAurasDueToSpell(Misc.SpellFlag);
            flag.HandleCustomTypeCommand(new SetNewFlagState(FlagState.Respawning, carrier));

            bool alliance = team == BattleGroundTeamId.Alliance;
            SendText($"{carrier.GetName()} has captured the flag!", alliance ? ChatMsg.BgSystemAlliance : ChatMsg.BgSystemHorde, carrier);
            PlaySoundToAll(alliance ? SoundIds.FlagCapturedAlliance : SoundIds.FlagCapturedHorde);
            UpdatePlayerScore(carrier, ScoreType.FlagCaptures, 1);

            int bases = ControlledBaseCount(team);
            if (bases > 0)
                AddPoints(team, Misc.FlagPoints[bases - 1]);
        }

        public override void OnFlagStateChange(GameObject flagInBase, FlagState oldValue, FlagState newValue, Player player)
        {
            UpdateWorldState(WorldStateIds.FlagIcon, newValue == FlagState.InBase ? 1 : 0);
            // The icon row's flag icons light up while that side carries the flag (expressions 51172/51173: state == 2).
            Team carrierTeam = newValue == FlagState.Taken && player != null ? GetPlayerTeam(player.GetGUID()) : Team.Other;
            UpdateWorldState(WorldStateIds.AllianceFlagCarried, carrierTeam == Team.Alliance ? IconStates.Captured : IconStates.Hidden);
            UpdateWorldState(WorldStateIds.HordeFlagCarried, carrierTeam == Team.Horde ? IconStates.Captured : IconStates.Hidden);
            switch (newValue)
            {
                case FlagState.Taken:
                    if (player == null)
                        break;
                    bool alliance = GetPlayerTeam(player.GetGUID()) == Team.Alliance;
                    SendText($"{player.GetName()} has taken the flag!", alliance ? ChatMsg.BgSystemAlliance : ChatMsg.BgSystemHorde, player);
                    PlaySoundToAll(alliance ? SoundIds.FlagPickedUpAlliance : SoundIds.FlagPickedUpHorde);
                    player.RemoveAurasWithInterruptFlags(SpellAuraInterruptFlags.PvPActive);
                    break;
                case FlagState.Dropped:
                    if (player != null && Global.SpellMgr.GetSpellInfo(BattlegroundConst.SpellRecentlyDroppedNeutralFlag, Difficulty.None) != null)
                        player.CastSpell(player, BattlegroundConst.SpellRecentlyDroppedNeutralFlag, true);
                    SendText("The flag has been dropped!", ChatMsg.BgSystemNeutral);
                    break;
                case FlagState.InBase:
                    if (oldValue != FlagState.Taken)
                    {
                        SendText("The flag has been reset!", ChatMsg.BgSystemNeutral);
                        PlaySoundToAll(SoundIds.FlagReset);
                    }
                    break;
            }
        }

        // A carrier leaving the battleground drops the flag where they stood, as a death does.
        public override void EventPlayerDroppedFlag(Player player)
        {
            if (FlagCarrier() == player)
                player.RemoveAurasDueToSpell(Misc.SpellFlag, default, 0, AuraRemoveMode.Expire);
        }

        public override void HandleKillPlayer(Player victim, Player killer)
        {
            if (GetStatus() != BattlegroundStatus.InProgress)
                return;

            base.HandleKillPlayer(victim, killer);
        }

        // The nearest graveyard this side may use: its home graveyard and those of the bases it holds.
        public override WorldSafeLocsEntry GetClosestGraveyard(Player player)
        {
            int team = GetTeamIndexByTeamId(GetPlayerTeam(player.GetGUID()));
            WorldSafeLocsEntry best = Global.ObjectMgr.GetWorldSafeLoc(team == BattleGroundTeamId.Alliance ? GraveyardIds.MainAlliance : GraveyardIds.MainHorde);
            float bestDist = best != null ? player.GetExactDist2d(best.Loc) : float.MaxValue;
            for (int node = 0; node < Nodes.Count; ++node)
            {
                if (NodeOwner(node) != team)
                    continue;
                WorldSafeLocsEntry graveyard = Global.ObjectMgr.GetWorldSafeLoc(Nodes.All[node].GraveyardId);
                if (graveyard != null && player.GetExactDist2d(graveyard.Loc) < bestDist)
                {
                    best = graveyard;
                    bestDist = player.GetExactDist2d(graveyard.Loc);
                }
            }
            return best;
        }

        public override WorldSafeLocsEntry GetExploitTeleportLocation(Team team)
        {
            return Global.ObjectMgr.GetWorldSafeLoc(team == Team.Alliance ? GraveyardIds.MainAlliance : GraveyardIds.MainHorde);
        }

        public override Team GetPrematureWinner()
        {
            if (GetTeamScore(BattleGroundTeamId.Alliance) > GetTeamScore(BattleGroundTeamId.Horde))
                return Team.Alliance;
            if (GetTeamScore(BattleGroundTeamId.Horde) > GetTeamScore(BattleGroundTeamId.Alliance))
                return Team.Horde;
            return base.GetPrematureWinner();
        }

        public override void EndBattleground(Team winner)
        {
            if (winner == Team.Alliance || winner == Team.Horde)
                RewardHonorToTeam(GetBonusHonorFromKill(1), winner);
            RewardHonorToTeam(GetBonusHonorFromKill(1), Team.Alliance);
            RewardHonorToTeam(GetBonusHonorFromKill(1), Team.Horde);
            base.EndBattleground(winner);
        }

        public override bool UpdatePlayerScore(Player player, ScoreType type, int value, bool doAddHonor = true)
        {
            if (!base.UpdatePlayerScore(player, type, value, doAddHonor))
                return false;

            switch (type)
            {
                case ScoreType.FlagCaptures:
                    player.UpdateCriteria(CriteriaType.TrackedWorldStateUIModified, PvpStats.FlagCaptures);
                    break;
                case ScoreType.BasesAssaulted:
                    player.UpdateCriteria(CriteriaType.TrackedWorldStateUIModified, PvpStats.BasesAssaulted);
                    break;
                case ScoreType.BasesDefended:
                    player.UpdateCriteria(CriteriaType.TrackedWorldStateUIModified, PvpStats.BasesDefended);
                    break;
            }
            return true;
        }

        // Plain battleground text: the client has no broadcast texts for this battleground's events.
        void SendText(string text, ChatMsg type, Player source = null)
        {
            ChatPkt packet = new();
            packet.Initialize(type, Language.Universal, source, null, text);
            SendPacketToAll(packet);
        }
    }

    class BgDarkspearIslandsScore : BattlegroundScore
    {
        int _flagCaptures, _basesAssaulted, _basesDefended;

        public BgDarkspearIslandsScore(ObjectGuid playerGuid, Team team) : base(playerGuid, team) { }

        public override void UpdateScore(ScoreType type, int value)
        {
            switch (type)
            {
                case ScoreType.FlagCaptures: _flagCaptures += value; break;
                case ScoreType.BasesAssaulted: _basesAssaulted += value; break;
                case ScoreType.BasesDefended: _basesDefended += value; break;
                default: base.UpdateScore(type, value); break;
            }
        }

        public override void BuildPvPLogPlayerDataPacket(out PVPMatchStatistics.PVPMatchPlayerStatistics playerData)
        {
            base.BuildPvPLogPlayerDataPacket(out playerData);
            playerData.Stats.Add(new PVPMatchStatistics.PVPMatchPlayerPVPStat(PvpStats.FlagCaptures, _flagCaptures));
            playerData.Stats.Add(new PVPMatchStatistics.PVPMatchPlayerPVPStat(PvpStats.BasesAssaulted, _basesAssaulted));
            playerData.Stats.Add(new PVPMatchStatistics.PVPMatchPlayerPVPStat(PvpStats.BasesDefended, _basesDefended));
        }

        public override int GetAttr1() { return _flagCaptures; }
        public override int GetAttr2() { return _basesAssaulted; }
        public override int GetAttr3() { return _basesDefended; }
    }

    #region Constants
    record NodeInfo(string Name, int CapturePointEntry, Position Position, int StateWorldState,
        int IconWorldStateAlliance, int IconWorldStateHorde, int GraveyardId, Position SpiritGuidePosition);

    static class Nodes
    {
        public const int Camp = 0, Ruins = 1, AbandonedTower = 2, ShipwreckCove = 3, Count = 4;

        // Positions: AreaPOI 8768/8771/8772/8773. Icon states: widget 7860, the scoreboard's icon row (Alliance left, Horde right).
        // Spirit guides stand a few yards from the graveyard release point (AnoCore world, from a 2026-10-10 match recording;
        // the Camp guide's facing is not recorded there).
        public static readonly NodeInfo[] All =
        [
            new("Camp", GameobjectIds.CapturePointCamp, new Position(-353.79f, 2119.96f, 46.22f, 4.63f), 30837, 30854, 30853, GraveyardIds.Camp,
                new Position(-456.285f, 2076.62f, 47.347f, 5.84f)),
            new("Ruins", GameobjectIds.CapturePointRuins, new Position(-349.66f, 1739.70f, 5.92f, 1.6f), 30838, 30855, 30856, GraveyardIds.Ruins,
                new Position(-456.48f, 1819.62f, 22.932f, 5.7072f)),
            new("Abandoned Tower", GameobjectIds.CapturePointAbandonedTower, new Position(-111.77f, 1746.61f, 46.26f, 1.55f), 30839, 30857, 30858, GraveyardIds.AbandonedTower,
                new Position(-12.466f, 1785.07f, 47.345f, 2.7053f)),
            new("Shipwreck Cove", GameobjectIds.CapturePointShipwreckCove, new Position(-109.47f, 2128.69f, 5.65f, 4.65f), 30840, 30852, 30851, GraveyardIds.ShipwreckCove,
                new Position(-16.509f, 2045.49f, 22.94f, 2.1293f)),
        ];
    }

    struct Misc
    {
        public static TimeSpan PointsTickTime = (Seconds)2;
        public static TimeSpan FlagCheckTime = (Milliseconds)250;
        public const int MaxTeamScore = 2000;
        public const int WarningNearVictoryScore = 1800;
        public static readonly int[] TickPoints = [1, 2, 5, 10];     // per 2 s with 1-4 bases
        public static readonly int[] FlagPoints = [75, 85, 100, 500]; // flag capture with 1-4 bases
        public const float FlagCaptureRadius = 8.0f;

        public const int HonorTicks = 260, BonusHonorTicks = 160;
        public const int ReputationTicks = 160, BonusReputationTicks = 120, ReputationPerTick = 10;
        public const int ReputationAlliance = 2799;   // Theramore Expeditionary Force
        public const int ReputationHorde = 2798;      // Darkspear Raiders

        public const int SpellFlag = 1284560;         // Darkspear Islands Flag (carrier aura; drops the flag through aura 398)

        public static readonly Position FlagPosition = new(-224.74f, 1935.58f, 2.65f, 0.0f);   // AreaPOI 8774
        public static readonly Position SkullPilePosition = new(-225.86722f, 1935.5658f, 2.3075516f, 0.31707686f);   // under the flag, taken in game

        // Main spirit guides beside the start points (AnoCore world)
        public static readonly Position SpiritGuideAlliance = new(144.106f, 1952.8f, 51.719f, 2.7576f);
        public static readonly Position SpiritGuideHorde = new(-606.217f, 1896.32f, 51.719f, 0.2561f);
    }

    struct GraveyardIds
    {
        public const int MainAlliance = 1157001;   // north island base, also the Alliance start
        public const int MainHorde = 1157002;      // south island base, also the Horde start
        public const int Camp = 1157003;
        public const int Ruins = 1157004;
        public const int AbandonedTower = 1157005;
        public const int ShipwreckCove = 1157006;
    }

    struct GameobjectIds
    {
        public const int CapturePointCamp = 1157101;
        public const int CapturePointRuins = 1157102;
        public const int CapturePointAbandonedTower = 1157103;
        public const int CapturePointShipwreckCove = 1157104;
        public const int Flag = 1157110;
        public const int FlagDrop = 1157111;
        public const int SkullPile = 1157112;      // troll skull pile under the flag stand
        public const int AllianceGate = 661005;    // start gates (official sniff): map spawns, opened at the start
        public const int HordeGate = 661004;

        public const int AllianceAura = 180100;
        public const int HordeAura = 180101;
        public const int NeutralAura = 180102;

        // Full battlefield banners, pole and base included (the map has no posts of its own); the templates start on 6211, neutral.
        public const int AllianceBannerDisplay = 5651;
        public const int HordeBannerDisplay = 5652;
        public const int AllianceContestedBannerDisplay = 5653;
        public const int HordeContestedBannerDisplay = 5654;
    }

    // Capture point template events: First + node * Stride + kind.
    struct EventIds
    {
        public const int First = 1157200;
        public const int Stride = 10;
        public const int ContestedHorde = 1, CaptureHorde = 2, DefendedHorde = 3;
        public const int ContestedAlliance = 4, CaptureAlliance = 5, DefendedAlliance = 6;
    }

    struct ObjectTypes
    {
        public const int CapturePoint = 0;    // 0-3
        public const int Aura = 4;            // 4-7
        public const int Flag = 8;
        public const int SkullPile = 9;
        public const int Max = 10;
    }

    struct CreatureTypes
    {
        public const int SpiritMainAlliance = 0;
        public const int SpiritMainHorde = 1;
        public const int SpiritNode = 2;      // 2-5
        public const int Max = 6;
    }

    struct WorldStateIds
    {
        public const int AllianceResources = 1776;
        public const int HordeResources = 1777;
        public const int MaxResources = 1780;
        public const int FlagIcon = 31175;    // AreaPOI 8774: 1 = flag on its stand
        public const int AllianceBases = 1779;  // bases held (Arathi Basin's states, shown under the score)
        public const int HordeBases = 1778;
        public const int AllianceFlagCarried = 29785;  // 2 while the Alliance carries the flag (icon row, expression 51172)
        public const int HordeFlagCarried = 29786;     // 2 while the Horde carries it (expression 51173)
    }

    // DoubleStateIconRow icon states: hidden, first state (assaulted / flag taken), second state (captured).
    struct IconStates
    {
        public const int Hidden = 0;
        public const int Assaulted = 1;
        public const int Captured = 2;
    }

    struct PvpStats
    {
        public const int FlagCaptures = 1026;
        public const int BasesAssaulted = 1027;
        public const int BasesDefended = 1028;
    }

    struct SoundIds
    {
        public const int NodeCapturedAlliance = 8173;
        public const int NodeCapturedHorde = 8213;
        public const int NodeAssaultedAlliance = 8212;
        public const int NodeAssaultedHorde = 8174;
        public const int NearVictoryAlliance = 8456;
        public const int NearVictoryHorde = 8457;
        public const int FlagPickedUpAlliance = 8212;
        public const int FlagPickedUpHorde = 8174;
        public const int FlagCapturedAlliance = 8173;
        public const int FlagCapturedHorde = 8213;
        public const int FlagReset = 8192;
    }
    #endregion
}
