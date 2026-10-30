using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.AI;
public sealed partial class SIPlayer
{
    // The "duel" goal: stay where the order found the bot (a .summonall gathering, say) and challenge
    // nearby players of the same level or lower, one at a time. Accepting and fighting duels is TickDuel's.
    const float DuelAreaRadius = 25, DuelSearchRadius = 30, DuelChallengeRange = 6;
    const long DuelRequestTimeout = 30000, DuelApproachTimeout = 15000, DuelRechallengeDelay = 60000;
    const int DuelSpell = 7266;

    Position _duelAnchor;
    int _duelAnchorMap = -1;
    long _nextDuelChallenge, _duelCandidateUntil, _duelRequestedAt;
    ObjectGuid _duelCandidate;
    readonly Dictionary<ObjectGuid, long> _duelRecentlyChallenged = new();

    void ResetDuelGoal()
    {
        _duelAnchor = null; _duelAnchorMap = -1; _duelCandidate = ObjectGuid.Empty;
        _duelRecentlyChallenged.Clear();
    }

    SIBehaviorResult TickDuelGoalBehavior()
    {
        // the anchor is taken after the summon lands, not when the order is read mid-teleport
        if (_duelAnchor == null || _duelAnchorMap != me.GetMapId())
        {
            _duelAnchor = new Position(me.GetPositionX(), me.GetPositionY(), me.GetPositionZ(), me.GetOrientation());
            _duelAnchorMap = me.GetMapId();
            _nextDuelChallenge = Now + Random.Shared.Next(2000, 10001);
        }

        Player candidate = _duelCandidate.IsEmpty() ? null : Global.ObjAccessor.GetPlayer(me, _duelCandidate);
        if (candidate != null && (Now >= _duelCandidateUntil || !CanChallengeToDuel(candidate)))
        {
            _duelRecentlyChallenged[_duelCandidate] = Now;
            _duelCandidate = ObjectGuid.Empty; candidate = null;
        }
        else if (candidate == null)
            _duelCandidate = ObjectGuid.Empty;

        if (candidate == null && me.GetExactDist2d(_duelAnchor) > DuelAreaRadius)
        {
            Travel(_duelAnchor, "Returning to the duel area", 4);
            Status = "Returning to the duel area"; return SIBehaviorResult.Running;
        }

        if (!DuelResourcesFull())
        {
            RecoverForDuel();
            Status = "Recovering before the next duel"; return SIBehaviorResult.Running;
        }

        if (candidate == null)
        {
            if (Now < _nextDuelChallenge) { Status = "Waiting for a duel"; return SIBehaviorResult.Running; }
            _nextDuelChallenge = Now + Random.Shared.Next(5000, 15001);
            candidate = FindDuelCandidate();
            if (candidate == null) { Status = "Looking for someone to duel"; return SIBehaviorResult.Running; }
            _duelCandidate = candidate.GetGUID(); _duelCandidateUntil = Now + DuelApproachTimeout;
        }

        if (me.GetExactDist(candidate) > DuelChallengeRange || !me.IsWithinLOSInMap(candidate))
        {
            Travel(candidate.GetPosition(), "Approaching a duel opponent", DuelChallengeRange - 2);
            Status = $"Approaching {candidate.GetName()} to duel"; return SIBehaviorResult.Running;
        }

        CancelTravel();
        StandUp(); Dismount();
        me.SetFacingToObject(candidate);
        me.CastSpell(candidate, DuelSpell);
        _duelRecentlyChallenged[_duelCandidate] = Now;
        _duelCandidate = ObjectGuid.Empty;
        if (me.duel != null)
        {
            _duelRequestedAt = Now;
            Report("duel_challenge", $"Challenged {candidate.GetName()} (level {candidate.GetLevel()})");
            Status = $"Challenged {candidate.GetName()}";
        }
        else
            Status = $"Could not challenge {candidate.GetName()}";
        return SIBehaviorResult.Running;
    }

    Player FindDuelCandidate()
    {
        foreach (var (guid, at) in new List<KeyValuePair<ObjectGuid, long>>(_duelRecentlyChallenged))
            if (Now - at >= DuelRechallengeDelay) _duelRecentlyChallenged.Remove(guid);

        return NearestPlayer(DuelSearchRadius, player => CanChallengeToDuel(player) && !_duelRecentlyChallenged.ContainsKey(player.GetGUID()));
    }

    bool CanChallengeToDuel(Player player) =>
        player != me && player.IsInWorld && player.IsAlive() && player.duel == null && !player.IsGameMaster() &&
        !player.IsInFlight() && !player.IsInCombat() && !player.IsBeingTeleported() && player.GetLevel() <= me.GetLevel() &&
        me.GetExactDist2d(player) <= DuelSearchRadius && me.IsWithinLOSInMap(player);

    // A challenge we sent: wait for the answer, and withdraw it if none comes.
    bool TickOwnDuelRequest(DuelInfo duel)
    {
        if (duel.Initiator != me) return false;
        StandUp();
        if (_duelRequestedAt == 0) _duelRequestedAt = Now;
        if (Now - _duelRequestedAt >= DuelRequestTimeout)
        {
            me.GetSession().RespondToDuelForSI(false);
            _duelRequestedAt = 0;
            Status = $"Withdrew duel request to {duel.Opponent.GetName()}"; return true;
        }
        Status = $"Waiting for {duel.Opponent.GetName()} to accept the duel"; return true;
    }
}
