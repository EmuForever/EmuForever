using Framework.Constants;
using Game.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI;
public sealed partial class SIPlayer
{
    ObjectGuid _flightMaster;
    long _nextFlightScan;
    readonly Dictionary<ObjectGuid,long> _flightRetry=new();
    bool TickFlightPaths()
    {
        if(!WorkingGoal||me.IsInCombat()||me.IsInFlight())return false;
        if(CommittedToJourney())return false;
        if(_flightMaster.IsEmpty()&&CommittedToObjective())return false;
        Creature master=_flightMaster.IsEmpty()?null:ObjectAccessor.GetCreature(me,_flightMaster);
        if(master==null&&Now>=_nextFlightScan)
        {
            _nextFlightScan=Now+3000;
            if(_flightRetry.Count>=256)_flightRetry.Clear();
            foreach(var npc in NearbyCreatures(_settings.TargetSearchRadius).Where(c=>c.IsAlive()&&c.HasNpcFlag(NPCFlags1.FlightMaster)&&
                !me.IsHostileTo(c)&&me.CanSeeOrDetect(c,distanceCheck:false)&&_flightRetry.GetValueOrDefault(c.GetGUID())<=Now).OrderBy(me.GetExactDist))
            {
                int node=Global.ObjectMgr.GetNearestTaxiNode(npc.GetPosition3D(),npc.GetMapId(),me.GetTeam());
                if(node==0||me.m_taxi.IsTaximaskNodeKnown(node))continue;
                master=npc;_flightMaster=npc.GetGUID();CancelTravel();break;
            }
        }
        if(master==null)return false;
        if(!master.IsAlive()||!master.IsInWorld){_flightMaster=ObjectGuid.Empty;CancelTravel();return false;}
        _task?.Suspend(Now);me.SetSelection(master.GetGUID());
        var result=MoveWithin(master,3.5f,"Discovering flight path: "+master.GetName(),npcInteraction:true);
        if(result==SIBehaviorResult.Running)return true;
        if(result==SIBehaviorResult.Success)
        {
            var usable=me.GetNPCIfCanInteractWith(master.GetGUID(),NPCFlags1.FlightMaster,NPCFlags2.None);
            if(usable!=null&&me.GetSession().SendLearnNewTaxiNode(usable))
            {
                // A newly discovered connection can turn an existing walking
                // trainer commute into a much easier flight itinerary.
                _trainerTravelNeedsReplan=true;_nextTrainingScan=0;
            }
        }
        else _flightRetry[master.GetGUID()]=Now+300000;
        _flightMaster=ObjectGuid.Empty;_task?.Resume(Now);return true;
    }
}
