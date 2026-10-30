using Framework.Collections;
using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI;

// An itinerary built from the native taxi network, not a stored walking graph.
sealed record SITrainerTrip(SISpawn Trainer,SISpawn? Boarding,int[] TaxiNodes,float Seconds,long Fare);

public sealed partial class SIPlayer
{
    SITrainerTrip _trainerTrip;
    bool _trainerFlightTaken;
    Vector3 _trainerProgressPosition;
    int _trainerProgressMap;
    long _trainerFlightRetryAt,_failedTrainerBoarding;
    bool _trainerTravelNeedsReplan;

    long MinimumTrainingCost(int entry)
    {
        if(Global.ObjectMgr.GetCreatureTemplate(entry)==null||_trainingOffers==null)return long.MaxValue;
        float discount=PriceDiscount(entry);
        long cheapest=long.MaxValue;
        foreach(var trainer in SITrainerCatalog.ForCreature(entry,me.GetClass()))
        {
            if(!_trainingOffers.TryGetValue(trainer.Id,out var offered))continue;
            foreach(var spell in trainer.GetTeachableSpells(me))
                if(offered.Contains(spell.SpellId))
                {
                    long cost=(long)(spell.MoneyCost*discount);
                    if(cost<=me.GetMoney())cheapest=Math.Min(cheapest,cost);
                }
        }
        return cheapest;
    }

    SITrainerTrip ChooseTrainerTrip()
    {
        // ServiceSpawns already skips unavailable, hostile, route-blocked and blacklisted trainers, nearest first.
        var prices=new Dictionary<int,long>();
        long Price(int entry)=>prices.TryGetValue(entry,out long price)?price:prices[entry]=MinimumTrainingCost(entry);
        SISpawn? found=ServiceSpawns(_trainingEntries,SITaskKind.Train).Where(spawn=>Price(spawn.Entry)!=long.MaxValue)
            .Select(spawn=>(SISpawn?)spawn).FirstOrDefault();
        if(found is not SISpawn nearest)return null;
        var best=new SITrainerTrip(nearest,null,Array.Empty<int>(),Vector3.Distance(Here,nearest.Position)/Math.Max(1,me.GetSpeed(UnitMoveType.Run)),0);
        if(_goal=="social")return best; // City errands cannot take a taxi out of town.

        var flight=ChooseFlightTrip(nearest.Position,prices[nearest.Entry],0);
        return flight==null?best:new(nearest,flight.Boarding,flight.Nodes,flight.Seconds,flight.Fare);
    }

    SIBehaviorResult TravelToTrainerByFlight()
    {
        if(_trainerTrip?.Boarding is not SISpawn boarding||_trainerFlightTaken)return SIBehaviorResult.Success;
        var approach=ApproachFlightMaster(boarding,"Taking a flight to a class trainer",out var master);
        if(approach==SIBehaviorResult.Running)return approach;
        if(approach==SIBehaviorResult.Success)
        {
            long lesson=MinimumTrainingCost(_trainerTrip.Trainer.Entry);
            if(lesson!=long.MaxValue&&lesson<=me.GetMoney())
            {
                CancelTravel();me.SetSelection(master.GetGUID());
                if(me.GetSession().TakeFlightForSI(master,_trainerTrip.TaxiNodes,lesson))
                {_trainerFlightTaken=true;_followingTaxi=true;_task?.Suspend(Now);Status="Flying to a class trainer";return SIBehaviorResult.Running;}
            }
        }
        _failedTrainerBoarding=boarding.SpawnId;_trainerFlightRetryAt=Now+60000;
        return SIBehaviorResult.Failure;
    }

    // Walks to a planned flight master. Success means it is in interaction range and returned in `master`;
    // a master that is not loaded yet is approached at its spawn point, and missing there is a failure.
    SIBehaviorResult ApproachFlightMaster(SISpawn boarding,string reason,out Creature master)
    {
        master=me.GetMap().GetCreatureBySpawnId(boarding.SpawnId);
        if(master==null)
        {
            var result=Travel(P(boarding.Position),reason,rememberFailure:false);
            // At the spawn point with no live flight master: it is not spawned right now.
            return result==SIBehaviorResult.Success?SIBehaviorResult.Failure:result;
        }
        if(!master.IsAlive()||!master.IsInWorld||!me.InSamePhase(master)||me.IsHostileTo(master))return SIBehaviorResult.Failure;
        return MoveWithin(master,3.5f,reason,npcInteraction:true);
    }
}
