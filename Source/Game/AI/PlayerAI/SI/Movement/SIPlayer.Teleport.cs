using System;

namespace Game.AI;

public sealed partial class SIPlayer
{
    internal void PrepareForTeleport(bool resetWork,bool interruptCast)
    {
        CancelTravel();_reachedGoal=null;_routeWaypoint=null;_movementFailed=false;_lastMovementTick=-1;
        _travelMount.Cancel(Now);
        if(!resetWork)return; // Short spell movement, such as Blink, keeps combat ownership.
        ReleaseJourney();
        CloseLootAnimation();EndEncounter(false);
        ResetPriorityTurnIn();
        SetTask(null);_suspendedTask=null;_trainingReturnTask=null;
        _questPickupAnchor=null;_workingQuestBatch=false;_nextReplan=0;_nextWorldSnapshot=0;
        _localQuestMap=-1;_followingTaxi=false;
        ResetProfessionGoal();ResetQuestingArea();
        if(interruptCast)me.CastStop();
        _nextThink=_clock+TimeSpan.FromSeconds(1);
        Status="Teleporting; replanning from the new location";
    }
}
