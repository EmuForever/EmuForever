using Framework.Algorithms;
using Framework.Collections;
using Framework.Constants;
using Game.DataStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI;

sealed record SIFlightTrip(SISpawn Boarding,int[] Nodes,Vector3 Landing,float Seconds,long Fare);

public sealed partial class SIPlayer
{
    SIFlightTrip ChooseFlightTrip(Vector3 goal,long reservedMoney,float minimumSaving=15)
    {
        int map=me.GetMapId();var origin=Here;
        float walkingSpeed=Math.Max(1,me.GetSpeed(UnitMoveType.Run));
        float Walk(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)/walkingSpeed;
        float walkingSeconds=Walk(origin,goal);
        SIFlightTrip best=null;
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
        if(nodes.Length<2)return null;
        var index=new Dictionary<int,int>(nodes.Length);
        for(int i=0;i<nodes.Length;++i)index.Add(nodes[i].Id,i);
        int start=nodes.Length;
        var graph=new EdgeWeightedDigraph(start+1);
        var boarding=new Dictionary<int,SISpawn>();
        foreach(var stop in Knowledge.FlightMasters)
        {
            var spawn=stop.Spawn;
            int id=me.GetTeam()==Team.Alliance?stop.AllianceNode:stop.HordeNode;
            if(spawn.Map!=map||!index.TryGetValue(id,out int vertex)||!FriendlyTemplate(spawn.Entry)||!SpawnAvailable(spawn)||!ServiceSpawnVisible(spawn)||
                IsLearnedRouteBlocked(P(spawn.Position))||Vector3.DistanceSquared(spawn.Position,nodes[vertex].Pos)>2500||
                (spawn.SpawnId==_failedTrainerBoarding&&Now<_trainerFlightRetryAt)||_journeyFlightRetry.GetValueOrDefault(spawn.SpawnId)>Now)continue;
            if(!boarding.TryGetValue(vertex,out var previous)||Walk(origin,spawn.Position)<Walk(origin,previous.Position))boarding[vertex]=spawn;
        }
        if(boarding.Count==0)return null;
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
            if(!routes.HasPathTo(destination)||routes.DistanceTo(destination)>=(best?.Seconds??walkingSeconds))continue;
            var route=routes.PathTo(destination).ToArray();
            if(route.Length<2||!boarding.TryGetValue(route[0].To,out var master))continue;
            var itinerary=route.Select(edge=>nodes[edge.To].Id).ToArray();
            ulong rawFare=0;
            float discount=PriceDiscount(master.Entry);
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
            if(reservedMoney<0||reservedMoney>me.GetMoney()||(long)totalFare>me.GetMoney()-reservedMoney)continue;
            float seconds=(float)routes.DistanceTo(destination)+Walk(nodes[destination].Pos,goal);
            if(seconds+minimumSaving<walkingSeconds&&(best==null||seconds<best.Seconds))
                best=new(master,itinerary,nodes[destination].Pos,seconds,(long)totalFare);
        }
        return best;
    }
}
