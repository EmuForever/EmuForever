using Framework.Algorithms;
using Framework.Collections;
using Framework.Constants;
using Game.DataStorage;
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
        var template=Global.ObjectMgr.GetCreatureTemplate(entry);
        if(template==null||_trainingOffers==null)return long.MaxValue;
        float discount=me.GetReputationPriceDiscount(CliDB.FactionTemplateStorage.LookupByKey(template.Faction));
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
        int map=me.GetMapId();var origin=Here;
        float walkingSpeed=Math.Max(1,me.GetSpeed(UnitMoveType.Run));
        float Walk(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)/walkingSpeed;
        var prices=new Dictionary<int,long>();
        var trainers=new List<SISpawn>();
        foreach(var spawn in Knowledge.Spawns(_trainingEntries,Array.Empty<int>(),map))
        {
            var candidate=new SITask{Kind=SITaskKind.Train,Destination=spawn};
            if(!SpawnAvailable(spawn)||IsLearnedRouteBlocked(P(spawn.Position))||!FriendlyTemplate(spawn.Entry)||Blacklisted(candidate.Key))continue;
            if(!prices.TryGetValue(spawn.Entry,out long price))prices[spawn.Entry]=price=MinimumTrainingCost(spawn.Entry);
            if(price!=long.MaxValue)trainers.Add(spawn);
        }
        if(trainers.Count==0)return null;
        var nearest=trainers.MinBy(s=>Walk(origin,s.Position));
        var best=new SITrainerTrip(nearest,null,Array.Empty<int>(),Walk(origin,nearest.Position),0);

        // A bounded, per-selection view of native known nodes. No SQL, map scans,
        // terrain searches or mutable player state cross to a background worker.
        var flag=me.GetTeam()==Team.Alliance?TaxiNodeFlags.ShowOnAllianceMap:TaxiNodeFlags.ShowOnHordeMap;
        bool AllowedNode(TaxiNodesRecord node)
        {
            if(node.ContinentID!=map||!node.IsPartOfTaxiNetwork||!node.HasFlag(flag)||!me.m_taxi.IsTaximaskNodeKnown(node.Id))return false;
            var condition=CliDB.PlayerConditionStorage.LookupByKey(node.ConditionID);
            return condition==null||ConditionManager.IsPlayerMeetingCondition(me,condition);
        }
        var nodes=CliDB.TaxiNodesStorage.Values.Where(AllowedNode)
            .OrderBy(n=>Vector3.DistanceSquared(origin,n.Pos)).Take(256).ToArray();
        if(nodes.Length<2)return best;
        var index=new Dictionary<int,int>(nodes.Length);
        for(int i=0;i<nodes.Length;++i)index.Add(nodes[i].Id,i);
        int start=nodes.Length;
        var graph=new EdgeWeightedDigraph(start+1);
        var boarding=new Dictionary<int,SISpawn>();
        foreach(var stop in Knowledge.FlightMasters)
        {
            var spawn=stop.Spawn;
            int id=me.GetTeam()==Team.Alliance?stop.AllianceNode:stop.HordeNode;
            if(spawn.Map!=map||!index.TryGetValue(id,out int vertex)||!FriendlyTemplate(spawn.Entry)||!SpawnAvailable(spawn)||
                IsLearnedRouteBlocked(P(spawn.Position))||Vector3.DistanceSquared(spawn.Position,nodes[vertex].Pos)>2500||
                spawn.SpawnId==_failedTrainerBoarding&&Now<_trainerFlightRetryAt)continue;
            if(!boarding.TryGetValue(vertex,out var previous)||Walk(origin,spawn.Position)<Walk(origin,previous.Position))boarding[vertex]=spawn;
        }
        if(boarding.Count==0)return best;
        foreach(var pair in boarding)graph.AddEdge(new DirectedEdge(start,pair.Key,Walk(origin,pair.Value.Position)+15));

        float flightSpeed=Math.Max(1,30*(1+me.GetTotalAuraModifier((AuraType)388)/100f));
        int edges=0;
        for(int from=0;from<nodes.Length;++from)
        {
            if(!CliDB.TaxiPathSetBySource.TryGetValue(nodes[from].Id,out var destinations))continue;
            foreach(var pair in destinations)
            {
                if(!index.TryGetValue(pair.Key,out int to)||edges>=4096||
                    !CliDB.TaxiPathNodesByPath.TryGetValue(pair.Value.Id,out var path)||path.Length<2||path.Length>4096||path.Any(p=>p==null))continue;
                float seconds=0;
                for(int p=1;p<path.Length;++p)
                {
                    if(!path[p-1].HasFlag(TaxiPathNodeFlags.Teleport)&&path[p-1].ContinentID==path[p].ContinentID)
                        seconds+=Vector3.Distance(path[p-1].Loc,path[p].Loc)/flightSpeed;
                    seconds+=(float)path[p-1].Delay;
                }
                if(!float.IsFinite(seconds)||seconds<=0)continue;
                graph.AddEdge(new DirectedEdge(from,to,seconds));++edges;
            }
        }
        var routes=new DijkstraShortestPath(graph,start);
        for(int destination=0;destination<nodes.Length;++destination)
        {
            if(!routes.HasPathTo(destination)||routes.DistanceTo(destination)>=best.Seconds)continue;
            var route=routes.PathTo(destination).ToArray();
            if(route.Length<2||!boarding.TryGetValue(route[0].To,out var master))continue;
            var itinerary=route.Select(edge=>nodes[edge.To].Id).ToArray();
            ulong rawFare=0;
            var masterTemplate=Global.ObjectMgr.GetCreatureTemplate(master.Entry);
            float discount=me.GetReputationPriceDiscount(CliDB.FactionTemplateStorage.LookupByKey(masterTemplate.Faction));
            ulong paidByLeg=0;
            for(int i=1;i<itinerary.Length;++i)
            {
                Global.ObjectMgr.GetTaxiPath(itinerary[i-1],itinerary[i],out _,out uint fare);rawFare+=fare;
                ulong legFare=me.ApplyClassicLegacyPriceMod((ulong)Math.Ceiling(fare*discount),4);
                paidByLeg=legFare>ulong.MaxValue-paidByLeg?ulong.MaxValue:paidByLeg+legFare;
            }
            if(rawFare>uint.MaxValue)continue;
            // Activation checks the discounted total; subsequent path switches
            // round individual fares. Reserve the greater amount for lessons.
            ulong totalFare=Math.Max(paidByLeg,me.ApplyClassicLegacyPriceMod((ulong)Math.Ceiling(rawFare*discount),4));
            if(totalFare>(ulong)me.GetMoney())continue;
            foreach(var trainer in trainers)
            {
                // Do not spend the lesson money on the flight and arrive unable
                // to train. Native activation rechecks the actual fare as well.
                if((long)totalFare>me.GetMoney()-prices[trainer.Entry])continue;
                float seconds=(float)routes.DistanceTo(destination)+Walk(nodes[destination].Pos,trainer.Position);
                if(seconds<best.Seconds)best=new(trainer,master,itinerary,seconds,(long)totalFare);
            }
        }
        return best;
    }

    SIBehaviorResult TravelToTrainerByFlight()
    {
        if(_trainerTrip?.Boarding is not SISpawn boarding||_trainerFlightTaken)return SIBehaviorResult.Success;
        var master=me.GetMap().GetCreatureBySpawnId(boarding.SpawnId);
        SIBehaviorResult approach;
        if(master==null)
            approach=Travel(P(boarding.Position),"Travelling to flight master for training",rememberFailure:false);
        else if(!master.IsAlive()||!master.IsInWorld||!me.InSamePhase(master)||me.IsHostileTo(master))
            approach=SIBehaviorResult.Failure;
        else
            approach=MoveWithin(master,3.5f,"Taking a flight to a class trainer",npcInteraction:true);
        if(approach==SIBehaviorResult.Running)return approach;
        if(approach==SIBehaviorResult.Success&&master!=null)
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
}
