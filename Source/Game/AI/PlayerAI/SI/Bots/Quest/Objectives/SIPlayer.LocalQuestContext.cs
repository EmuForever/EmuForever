using Game.Entities;
using System;
using System.Numerics;
using System.Linq;
namespace Game.AI;

public sealed partial class SIPlayer
{
    int _localQuestMap=-1,_localQuestArea=-1;
    Vector3 _lastQuestPosition;
    long _lastQuestPositionAt;
    void ReviewLocalQuestContext()
    {
        var position=Here;int map=me.GetMapId(),area=me.GetAreaId();
        bool first=_localQuestMap<0;
        double elapsed=Math.Max(0,Now-_lastQuestPositionAt)/1000.0;
        // Ordinary running across an area boundary keeps its owned order. A summon,
        // teleport or login must instead plan from the new location, not the old log.
        bool relocated=!first&&(_localQuestMap!=map||Vector3.Distance(position,_lastQuestPosition)>
            Math.Max(40,me.GetSpeed(Framework.Constants.UnitMoveType.Run)*elapsed+20));
        bool changed=first||relocated||_localQuestArea!=area;
        _localQuestMap=map;_localQuestArea=area;_lastQuestPosition=position;_lastQuestPositionAt=Now;
        if(!changed)return;
        if(relocated)
        {
            SetTask(null);_trainingReturnTask=null;_suspendedTask=null;
            _nextTrainingScan=Now+1000;
        }
        ResetQuestingArea();_questPickupAnchor=new Position(me.GetPosition());_workingQuestBatch=!first&&!relocated&&QuestLog().Any(q=>QuestLevelEligible(q.Quest));
        _nextNearbyQuestScan=0;_nextReplan=0;_nextQuestLevelCheck=0;
        _nextLevelGapScan=0;
    }
    void ResetQuestingArea()
    {
        _authorizedQuestZoneTrips.Clear();_questPointZones.Clear();
        _questPickupAnchor=null;_workingQuestBatch=false;_nextNearbyQuestScan=0;
    }
}
