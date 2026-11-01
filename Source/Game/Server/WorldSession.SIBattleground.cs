using Framework.Constants;
using Game.BattleGrounds;
using Game.Entities;
using Game.Networking.Packets;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Game;

public partial class WorldSession
{
    readonly ConcurrentQueue<(int Generation, ObjectGuid Player, Action Action)> _siBattlegroundRequests = new();
    int _siBattlegroundGeneration, _siBattlegroundPending;
    internal string SIBattlegroundJoinResult => Volatile.Read(ref _siBattlegroundJoinResult);
    string _siBattlegroundJoinResult;

    // Queue mutation and world ports share the ordinary unsafe session phase.
    // Map workers only request them; stale joins are discarded after a new order.
    bool RequestBattlegroundForSI(Action action)
    {
        if (!IsSIControlled || _player == null || Interlocked.CompareExchange(ref _siBattlegroundPending, 1, 0) != 0) return false;
        _siBattlegroundRequests.Enqueue((Volatile.Read(ref _siBattlegroundGeneration), _player.GetGUID(), action));
        return true;
    }

    void ProcessSIBattlegroundRequests()
    {
        while (_siBattlegroundRequests.TryDequeue(out var request))
        {
            try
            {
                if (request.Generation == Volatile.Read(ref _siBattlegroundGeneration) && _player?.GetGUID() == request.Player)
                    request.Action();
            }
            catch (Exception error) { Log.outError(LogFilter.SI, "SI battleground request failed: " + error); }
            finally { Interlocked.Exchange(ref _siBattlegroundPending, 0); }
        }
    }

    internal bool JoinBattlegroundForSI(Creature battlemaster)
    {
        if (battlemaster == null) return false;
        var guid = battlemaster.GetGUID();
        return RequestBattlegroundForSI(() =>
        {
            if (_player.Controller?.WantsBattleground == true)
            {
                bool joined = JoinBattlegroundNowForSI(_player.GetMap().GetCreature(guid));
                Volatile.Write(ref _siBattlegroundJoinResult, joined ? "Queue confirmed; waiting for an invitation" :
                    _player.GetGroup() is { } group && group.GetLeaderGUID() != _player.GetGUID()
                        ? "Not queued: only the group leader can join the group to a battleground"
                        : "Not queued: native battleground join was refused; retrying another battlemaster shortly");
            }
        });
    }

    // The PvP panel's queue: BattlemasterJoin carries no NPC, so no trip to a battlemaster is needed.
    internal bool JoinBattlegroundRemotelyForSI(BattlegroundTypeId type) => RequestBattlegroundForSI(() =>
    {
        if (_player.Controller?.WantsBattleground != true) return;
        bool joined = false;
        if (_player.GetBGAccessByLevel(type))
        {
            var queue = Global.BattlegroundMgr.BGQueueTypeId(type, BattlegroundQueueIdType.Battleground, false, 0);
            var request = new BattlemasterJoin(null);
            request.QueueIDs.Add(unchecked((long)queue.GetPacked()));
            HandleBattlemasterJoin(request);
            joined = _player.InBattlegroundQueueForBattlegroundQueueType(queue);
        }
        Volatile.Write(ref _siBattlegroundJoinResult, joined ? "Queue confirmed; waiting for an invitation" :
            _player.GetGroup() is { } group && group.GetLeaderGUID() != _player.GetGUID()
                ? "Not queued: only the group leader can join the group to a battleground"
                : "Not queued: native battleground join was refused; retrying shortly");
    });

    bool JoinBattlegroundNowForSI(Creature battlemaster)
    {
        if (!IsSIControlled || _player == null || battlemaster == null ||
            _player.GetNPCIfCanInteractWith(battlemaster.GetGUID(), NPCFlags1.BattleMaster, NPCFlags2.None) == null)
            return false;
        var type = Global.BattlegroundMgr.GetBattleMasterBG(battlemaster.GetEntry());
        if (type == BattlegroundTypeId.None || !_player.GetBGAccessByLevel(type)) return false;
        var queue = Global.BattlegroundMgr.BGQueueTypeId(type, BattlegroundQueueIdType.Battleground, false, 0);
        HandleBattlemasterHello(new Hello(null) { Unit = battlemaster.GetGUID() });
        var request = new BattlemasterJoin(null);
        request.QueueIDs.Add(unchecked((long)queue.GetPacked()));
        HandleBattlemasterJoin(request);
        return _player.InBattlegroundQueueForBattlegroundQueueType(queue);
    }

    internal bool HasBattlegroundInviteForSI()
    {
        if (!IsSIControlled || _player == null) return false;
        for (int slot = 0; slot < SharedConst.MaxPlayerBGQueues; ++slot)
        {
            var queue = _player.GetBattlegroundQueueTypeId(slot);
            if (queue != default && !queue.Rated && queue.TeamSize == 0 &&
                _player.IsInvitedForBattlegroundQueueType(queue)) return true;
        }
        return false;
    }

    internal bool AcceptBattlegroundInviteForSI() => RequestBattlegroundForSI(() =>
    {
        if (_player.Controller?.WantsBattleground == true) AcceptBattlegroundInviteNowForSI();
    });

    bool AcceptBattlegroundInviteNowForSI()
    {
        if (!IsSIControlled || _player == null || _player.IsBeingTeleported() || _player.InBattleground()) return false;
        for (int slot = 0; slot < SharedConst.MaxPlayerBGQueues; ++slot)
        {
            var queue = _player.GetBattlegroundQueueTypeId(slot);
            if (queue == default || queue.Rated || queue.TeamSize != 0 || !_player.IsInvitedForBattlegroundQueueType(queue)) continue;
            HandleBattleFieldPort(BattlegroundPortForSI(slot, queue, true));
            return _player.InBattleground() || _player.IsBeingTeleported();
        }
        return false;
    }

    BattlefieldPort BattlegroundPortForSI(int slot, BattlegroundQueueTypeId queue, bool accepted) => new(null)
    {
        AcceptedInvite = accepted,
        Ticket = new RideTicket
        {
            RequesterGuid = _player.GetGUID(), Id = slot, Type = RideType.Battlegrounds,
            JoinTime = _player.GetBattlegroundQueueJoinTime(queue)
        }
    };

    internal void CancelBattlegroundQueuesForSI()
    {
        if (!IsSIControlled || _player == null) return;
        int generation = Interlocked.Increment(ref _siBattlegroundGeneration);
        Volatile.Write(ref _siBattlegroundJoinResult, null);
        _siBattlegroundRequests.Enqueue((generation, _player.GetGUID(), CancelBattlegroundQueuesNowForSI));
    }

    void CancelBattlegroundQueuesNowForSI()
    {
        for (int slot = 0; slot < SharedConst.MaxPlayerBGQueues; ++slot)
        {
            var queue = _player.GetBattlegroundQueueTypeId(slot);
            // The active match's slot is retained until the native leave path removes it.
            if (queue == default || queue.Rated || queue.TeamSize != 0 ||
                !Global.BattlegroundMgr.GetBattlegroundQueue(queue).GetPlayerGroupInfoData(_player.GetGUID(), out _)) continue;
            HandleBattleFieldPort(BattlegroundPortForSI(slot, queue, false));
        }
    }

    internal void LeaveFinishedBattlegroundForSI() => RequestBattlegroundForSI(LeaveFinishedBattlegroundNowForSI);

    void LeaveFinishedBattlegroundNowForSI()
    {
        if (IsSIControlled && _player?.GetBattleground()?.GetStatus() == BattlegroundStatus.WaitLeave)
            HandleBattlefieldLeave(new BattlefieldLeave(null));
    }

}
