using Framework.Constants;
using Game.AI.SIMovement;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace Game.AI;

public sealed partial class SIPlayer
{
    SIFlightTrip _journeyFlight;
    SIPathPosition _journeyFlightGoal;
    SIPathPosition? _completedFlightGoal;
    SITask _journeyFlightTask;
    bool _boardingFlight,_journeyFlightTaken;
    string _journeyFlightReason,_journeyFlightMode;
    readonly Dictionary<long,long> _journeyFlightRetry=new();
    long _nextJourneyFlightScan,_boardingProgressAt;
    Vector3 _boardingProgress;

    void ResetJourneyFlight()
    {
        _journeyFlight=null;_journeyFlightTask=null;_journeyFlightTaken=false;
        _completedFlightGoal=null;_nextJourneyFlightScan=0;
    }
    static string FlightNodeName(int id)=>CliDB.TaxiNodesStorage.LookupByKey(id)?.Name[Locale.enUS]??("flight stop "+id);
    string JourneyFlightDescription=>_journeyFlight==null?"":string.Join(" -> ",Array.ConvertAll(_journeyFlight.Nodes,FlightNodeName));
    long JourneyFlightReserve()
    {
        long reserve=RequiredRepairCost();
        if(_task?.Kind==SITaskKind.Train&&_task.Destination is SISpawn trainer)
            reserve=Math.Max(reserve,MinimumTrainingCost(trainer.Entry));
        if(_task?.Kind==SITaskKind.ProfessionTrain&&_task.Destination is SISpawn professionTrainer)
        {
            float discount=PriceDiscount(professionTrainer.Entry);
            long lesson=Global.ObjectMgr.GetCreatureTrainerIds(professionTrainer.Entry).Select(Global.ObjectMgr.GetTrainer)
                .Where(t=>t!=null).SelectMany(t=>t.GetTeachableSpells(me)).Where(ProfessionLessonWanted)
                .Select(s=>(long)(s.MoneyCost*discount)).DefaultIfEmpty(long.MaxValue).Min();
            reserve=lesson>long.MaxValue-reserve?long.MaxValue:reserve+lesson;
        }
        if(_task?.Kind==SITaskKind.Vendor&&_task.VendorPurpose==SIVendorPurpose.Supplies)
            foreach(var (item,count) in _professionSupplies)
            {
                var template=Global.ObjectMgr.GetItemTemplate(item);
                if(template==null)continue;
                long packs=(long)Math.Ceiling((double)Math.Max(0,count-me.GetItemCount(item))/Math.Max(1,template.GetBuyCount()));
                long price=packs*template.GetBuyPrice();
                reserve=price>long.MaxValue-reserve?long.MaxValue:reserve+price;
            }
        return reserve;
    }
    bool TryJourneyFlight(SIPathPosition goal,string reason,bool walk,out SIBehaviorResult result)
    {
        result=SIBehaviorResult.Running;
        if(_boardingFlight)return false;
        if(_journeyFlight!=null&&goal.MapId==_journeyFlightGoal.MapId&&Vector3.Distance(goal.Point,_journeyFlightGoal.Point)<30)
            return TickJourneyFlight();
        if(_journeyFlight!=null||walk||!WorkingGoal||!SIQuestKnowledge.IsBuilt||
            !me.IsAlive()||me.IsInCombat()||_encounter.Active||me.IsInFlight()||me.GetTransport()!=null||
            _goal=="social"&&!_socialTravellingToCity||Distance(goal.Point)<1500||
            _trainerTrip?.Boarding.HasValue==true&&!_trainerFlightTaken)return false;
        if(_completedFlightGoal is SIPathPosition done&&done.MapId==goal.MapId&&Vector3.Distance(done.Point,goal.Point)<30)return false;
        if(Now<_nextJourneyFlightScan)return false;
        _nextJourneyFlightScan=Now+30000;
        var flight=ChooseFlightTrip(goal.Point,JourneyFlightReserve());
        if(flight==null)return false;
        _journeyFlight=flight;_journeyFlightGoal=goal;_journeyFlightTask=_task;_journeyFlightReason=reason;
        _journeyFlightMode=_goal;
        _journeyFlightTaken=false;_boardingProgress=Here;_boardingProgressAt=Now;
        CancelTravel();CommitJourney(goal.Point);
        TickJourneyFlight();return true;
    }
    bool TickJourneyFlight()
    {
        if(_journeyFlight==null)return false;
        if(!WorkingGoal||_goal!=_journeyFlightMode||!ReferenceEquals(_journeyFlightTask,_task)||_task!=null&&(TaskDone(_task)||Blacklisted(_task.Key)))
        {ResetJourneyFlight();return false;}
        if(me.IsInCombat()||_encounter.Active||_recovering||_resting)return false;
        if(Recuperate()||Rest()||Survive()){Behavior="Recovery";_task?.Suspend(Now);return true;}
        if(_journeyFlightTaken)
        {
            if(me.IsInFlight()||me.IsBeingTeleported())return true;
            _completedFlightGoal=_journeyFlightGoal;
            _journeyFlight=null;_journeyFlightTask=null;
            CancelTravel();CommitJourney(_completedFlightGoal.Value.Point);_task?.Resume(Now);
            Status="Landed; continuing "+_journeyFlightReason;return false;
        }
        if(StoragePressure()&&_task?.Kind!=SITaskKind.Vendor)return false;
        if(Vector3.DistanceSquared(Here,_boardingProgress)>4){_boardingProgress=Here;_boardingProgressAt=Now;}
        var boarding=_journeyFlight.Boarding;
        SIBehaviorResult approach;Creature master;
        _boardingFlight=true;
        try{approach=ApproachFlightMaster(boarding,"Flight master "+CreatureName(boarding.Entry)+": "+JourneyFlightDescription+"; then "+_journeyFlightReason,out master);}
        finally{_boardingFlight=false;}
        if(approach==SIBehaviorResult.Running&&Now-_boardingProgressAt<60000)
        {_task?.Suspend(Now);Behavior="FlightPaths";return true;}
        if(approach==SIBehaviorResult.Success)
        {
            CancelTravel();me.SetSelection(master.GetGUID());
            if(me.GetSession().TakeFlightForSI(master,_journeyFlight.Nodes,JourneyFlightReserve()))
            {_journeyFlightTaken=true;_followingTaxi=true;_task?.Suspend(Now);Behavior="Flight";Status="Flying "+JourneyFlightDescription+"; then "+_journeyFlightReason;return true;}
        }
        _journeyFlightRetry[boarding.SpawnId]=Now+60000;
        if(_journeyFlightRetry.Count>256)_journeyFlightRetry.Clear();
        _journeyFlight=null;_journeyFlightTask=null;CancelTravel();_nextJourneyFlightScan=Now+30000;
        Status="Flight unavailable; continuing the same task on foot";_task?.Resume(Now);return false;
    }
}
