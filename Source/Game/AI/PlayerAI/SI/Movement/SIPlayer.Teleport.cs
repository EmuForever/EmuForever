namespace Game.AI;

using System;
using System.Numerics;

public sealed partial class SIPlayer
{
    internal void PrepareForTeleport(bool resetWork,bool interruptCast)
    {
        CancelTravel();_reachedGoal=null;_routeWaypoint=null;_movementFailed=false;_lastMovementTick=-1;
        _travelMount.Cancel(Now);
        _settleOnFloor=true;
        if(!resetWork)return; // Short spell movement, such as Blink, keeps combat ownership.
        _questHearthQueuedAt=null;
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

    // Teleport destinations (graveyards, hearth points, summons, script coordinates) are stored a few yards off the
    // real floor: buried under the terrain or floating above it. A client drops its player onto the ground; a bot
    // has none and would stay where the coordinates put it, unable to plan a first step (it then fell back to
    // porting its ghost straight back to the corpse, or stood idle). Settle onto the nearest floor instead.
    const float SettleSearchUp=6,SettleSearchDown=30,SettleTolerance=.3f;
    bool _settleOnFloor;

    bool SettleOnFloor()
    {
        _settleOnFloor=false;
        if(!me.IsAlive()||me.InBattleground()||!me.IsInWorld||me.IsBeingTeleported()||me.IsInFlight()||me.GetTransport()!=null||me.IsFlying()||SwimmingSurface(Here))return false;
        var here=Here;
        // The navmesh is the floor routes are planned on, so it decides first: within the planner's start allowance
        // the bot can walk off as it is. Further off (buried under a rise, or the mesh standing on a rock the
        // coordinates sit inside), stand on the mesh, on the collision floor there when one agrees.
        Vector3? mesh=NearestMeshFloor(here);
        Vector3 target;
        if(mesh is Vector3 m)
        {
            if(Math.Abs(m.Z-here.Z)<=SIMovement.SIWalkingSurface.StartCorrection)return false;
            float agreed=FloorNear(m,1,1);
            target=new(m.X,m.Y,float.IsNaN(agreed)?m.Z:agreed);
        }
        else
        {
            if(!float.IsNaN(FloorNear(here,SettleTolerance,SettleTolerance)))return false; // already standing on a floor
            float below=FloorNear(here,SettleTolerance,SettleSearchDown);
            float above=FloorNear(here,SettleSearchUp,0);
            float floor=float.IsNaN(above)?below:float.IsNaN(below)?above:Math.Abs(above-here.Z)<Math.Abs(here.Z-below)?above:below;
            if(float.IsNaN(floor))return false;
            target=new(here.X,here.Y,floor);
        }
        Report("floor_settled",$"Teleport left the bot {here.Z-target.Z:F1} yd off the walkable floor at {here}; moved onto it");
        me.NearTeleportTo(target.X,target.Y,target.Z,me.GetOrientation());
        return true;
    }

    // Nearest navmesh floor within a short horizontal reach and a few yards up or down, or null without mmaps.
    Vector3? NearestMeshFloor(Vector3 point)
    {
        int terrainMap=PhasingHandler.GetTerrainMapId(me.GetPhaseShift(),me.GetMapId(),me.GetMap().GetTerrain(),point.X,point.Y);
        var query=Global.MMapMgr.GetNavMeshQuery(terrainMap,me.GetMapId(),me.GetInstanceId());
        if(query==null)return null;
        var filter=new Detour.dtQueryFilter();
        filter.setIncludeFlags((ushort)(Movement.NavTerrainFlag.Ground|Movement.NavTerrainFlag.GroundSteep|Movement.NavTerrainFlag.Water));
        ulong poly=0;var nearest=new float[3];
        if(Detour.dtStatusFailed(query.findNearestPoly([point.Y,point.Z,point.X],[1.5f,SettleSearchUp,1.5f],filter,ref poly,ref nearest))||poly==0)
            return null;
        return new Vector3(nearest[2],nearest[0],nearest[1]);
    }

    // A first step that fails on height straight up or down is the same buried/floating spot, found later.
    void SettleIfStartOffFloor(string reason)
    {
        if(reason.StartsWith("Route exceeds player walking slope or step height")||reason.StartsWith("No walkable ground near the start"))
            _settleOnFloor=true;
    }
}
