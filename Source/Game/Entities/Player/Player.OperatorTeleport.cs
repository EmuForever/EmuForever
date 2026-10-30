using Framework.Constants;
using Game.Chat;
using System;
using System.Threading;

namespace Game.Entities;

public partial class Player
{
    sealed record OperatorTeleport(WorldLocation Destination,int? Instance,ObjectGuid Issuer);
    OperatorTeleport _operatorTeleport;
    internal bool HasPendingOperatorTeleport=>Volatile.Read(ref _operatorTeleport)!=null;
    internal bool HasPendingTeleportDispatch=>m_bHasDelayedTeleport;

    // Cross-player commands enqueue a value snapshot, independent of SI think,
    // cast, loot and path-planning states. The newest operator request wins.
    internal void RequestOperatorTeleport(WorldLocation destination,int? instance,ObjectGuid issuer)
    {
        Interlocked.Exchange(ref _operatorTeleport,new(new WorldLocation(destination),instance,issuer));
    }

    bool ProcessOperatorTeleport()
    {
        if(!HasPendingOperatorTeleport)return false;
        // A real transfer must finish first. An undispatched delayed teleport can
        // be superseded because the player has not left its current map yet.
        if(IsBeingTeleported()&&!HasPendingTeleportDispatch)return true;
        var request=Interlocked.Exchange(ref _operatorTeleport,null);
        if(request==null)return false;
        if(HasPendingTeleportDispatch)
        {SetDelayedTeleportFlag(false);SetSemaphoreTeleportNear(false);SetSemaphoreTeleportFar(false);}
        if(IsInFlight())FinishTaxiFlight();else SaveRecallPosition();
        bool accepted=TeleportTo(request.Destination,0,request.Instance);
        var issuer=Global.ObjAccessor.FindConnectedPlayer(request.Issuer);
        if(accepted&&issuer!=null)
        {
            PhasingHandler.InheritPhaseShift(this,issuer);
            if(IsInWorld)UpdateObjectVisibility();
        }
        else if(!accepted&&issuer!=null)
            new CommandHandler(issuer.GetSession()).SendSysMessage($"Teleport of {GetName()} was rejected by the destination's normal map/instance rules.");
        return true;
    }

    internal bool DispatchDelayedTeleport(Func<WorldLocation,TeleportToOptions,int?,bool> dispatch=null)
    {
        if(!HasPendingTeleportDispatch||!IsAlive()||IsCanDelayTeleport())return false;
        var location=new WorldLocation(teleportDest);
        var options=m_teleport_options;var instance=m_teleport_instanceId;
        SetDelayedTeleportFlag(false);
        SetSemaphoreTeleportNear(false);SetSemaphoreTeleportFar(false);
        // Preserve instance selection when replaying a teleport after an update.
        return dispatch!=null?dispatch(location,options,instance):TeleportTo(location,options,instance);
    }
}
