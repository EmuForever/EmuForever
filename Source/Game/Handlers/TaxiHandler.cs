// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Movement;
using Game.Networking;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public partial class WorldSession
    {
        [WorldPacketHandler(ClientOpcodes.EnableTaxiNode, Processing = PacketProcessing.ThreadSafe)]
        void HandleEnableTaxiNodeOpcode(EnableTaxiNode enableTaxiNode)
        {
            Creature unit = GetPlayer().GetNPCIfCanInteractWith(enableTaxiNode.Unit, NPCFlags1.FlightMaster, NPCFlags2.None);
            if (unit != null)
                SendLearnNewTaxiNode(unit);
        }

        [WorldPacketHandler(ClientOpcodes.TaxiNodeStatusQuery, Processing = PacketProcessing.ThreadSafe)]
        void HandleTaxiNodeStatusQuery(TaxiNodeStatusQuery taxiNodeStatusQuery)
        {
            SendTaxiStatus(taxiNodeStatusQuery.UnitGUID);
        }

        public void SendTaxiStatus(ObjectGuid guid)
        {
            // cheating checks
            Player player = GetPlayer();
            Creature unit = ObjectAccessor.GetCreature(player, guid);
            if (unit == null || unit.IsHostileTo(player) || !unit.HasNpcFlag(NPCFlags1.FlightMaster))
            {
                Log.outDebug(LogFilter.Network, "WorldSession.SendTaxiStatus - {0} not found.", guid.ToString());
                return;
            }

            // find taxi node
            int nearest = Global.ObjectMgr.GetNearestTaxiNode(unit.GetPosition3D(), unit.GetMapId(), player.GetTeam());

            TaxiNodeStatusPkt data = new();
            data.Unit = guid;

            if (nearest == 0)
                data.Status = TaxiNodeStatus.None;
            else if (unit.GetReactionTo(player) >= ReputationRank.Neutral)
                data.Status = player.m_taxi.IsTaximaskNodeKnown(nearest) ? TaxiNodeStatus.Learned : TaxiNodeStatus.Unlearned;
            else
                data.Status = TaxiNodeStatus.NotEligible;

            SendPacket(data);
        }

        [WorldPacketHandler(ClientOpcodes.TaxiQueryAvailableNodes, Processing = PacketProcessing.ThreadSafe)]
        void HandleTaxiQueryAvailableNodes(TaxiQueryAvailableNodes taxiQueryAvailableNodes)
        {
            // cheating checks
            Creature unit = GetPlayer().GetNPCIfCanInteractWith(taxiQueryAvailableNodes.Unit, NPCFlags1.FlightMaster, NPCFlags2.None);
            if (unit == null)
            {
                Log.outDebug(LogFilter.Network, "WORLD: HandleTaxiQueryAvailableNodes - {0} not found or you can't interact with him.", taxiQueryAvailableNodes.Unit.ToString());
                return;
            }

            // remove fake death
            if (GetPlayer().HasUnitState(UnitState.Died))
                GetPlayer().RemoveAurasByType(AuraType.FeignDeath);

            // unknown taxi node case
            if (SendLearnNewTaxiNode(unit))
                return;

            // known taxi node case
            SendTaxiMenu(unit);
        }

        public void SendTaxiMenu(Creature unit)
        {
            // find current node
            int curloc = Global.ObjectMgr.GetNearestTaxiNode(unit.GetPosition3D(),unit.GetMapId(), GetPlayer().GetTeam());
            if (curloc == 0)
                return;

            bool lastTaxiCheaterState = GetPlayer().IsTaxiCheater();
            if (unit.GetEntry() == 29480)
                GetPlayer().SetTaxiCheater(true); // Grimwing in Ebon Hold, special case. NOTE: Not perfect, Zul'Aman should not be included according to WoWhead, and I think taxicheat includes it.

            ShowTaxiNodes data = new();
            ShowTaxiNodesWindowInfo windowInfo = new();
            windowInfo.UnitGUID = unit.GetGUID();
            windowInfo.CurrentNode = curloc;

            data.WindowInfo = windowInfo;

            GetPlayer().m_taxi.AppendTaximaskTo(data, lastTaxiCheaterState);

            byte[] reachableNodes = new byte[CliDB.TaxiNodesMask.Length];
            TaxiPathGraph.GetReachableNodesMask(CliDB.TaxiNodesStorage.LookupByKey(curloc), reachableNodes);
            for (var i = 0; i < reachableNodes.Length; ++i)
            {
                data.CanLandNodes[i] &= reachableNodes[i];
                data.CanUseNodes[i] &= reachableNodes[i];
            }

            SendPacket(data);

            GetPlayer().SetTaxiCheater(lastTaxiCheaterState);
        }

        public void SendDoFlight(int mountDisplayId, int path, int pathNode = 0)
        {
            // remove fake death
            if (GetPlayer().HasUnitState(UnitState.Died))
                GetPlayer().RemoveAurasByType(AuraType.FeignDeath);

            if (mountDisplayId != 0)
                GetPlayer().Mount(mountDisplayId);

            GetPlayer().GetMotionMaster().MoveTaxiFlight(path, pathNode);
        }

        public bool SendLearnNewTaxiNode(Creature unit)
        {
            // find current node
            int curloc = Global.ObjectMgr.GetNearestTaxiNode(unit.GetPosition3D(), unit.GetMapId(), GetPlayer().GetTeam());

            if (curloc == 0)
                return true;

            if (GetPlayer().m_taxi.SetTaximaskNode(curloc))
            {
                SendPacket(new NewTaxiPath());

                TaxiNodeStatusPkt data = new();
                data.Unit = unit.GetGUID();
                data.Status = TaxiNodeStatus.Learned;
                SendPacket(data);

                return true;
            }
            else
                return false;
        }

        public void SendDiscoverNewTaxiNode(int nodeid)
        {
            if (GetPlayer().m_taxi.SetTaximaskNode(nodeid))
                SendPacket(new NewTaxiPath());
        }

        [WorldPacketHandler(ClientOpcodes.ActivateTaxi, Processing = PacketProcessing.ThreadSafe)]
        void HandleActivateTaxi(ActivateTaxi activateTaxi)
        {
            Creature unit = GetPlayer().GetNPCIfCanInteractWith(activateTaxi.Vendor, NPCFlags1.FlightMaster, NPCFlags2.None);
            if (unit == null)
            {
                Log.outDebug(LogFilter.Network, "WORLD: HandleActivateTaxiOpcode - {0} not found or you can't interact with it.", activateTaxi.Vendor.ToString());
                SendActivateTaxiReply(ActivateTaxiReply.TooFarAway);
                return;
            }

            int curloc = Global.ObjectMgr.GetNearestTaxiNode(unit.GetPosition3D(), unit.GetMapId(), GetPlayer().GetTeam());
            if (curloc == 0)
                return;

            TaxiNodesRecord from = CliDB.TaxiNodesStorage.LookupByKey(curloc);
            TaxiNodesRecord to = CliDB.TaxiNodesStorage.LookupByKey(activateTaxi.Node);
            if (to == null)
                return;

            if (!GetPlayer().IsTaxiCheater())
            {
                if (!GetPlayer().m_taxi.IsTaximaskNodeKnown(curloc) || !GetPlayer().m_taxi.IsTaximaskNodeKnown(activateTaxi.Node))
                {
                    SendActivateTaxiReply(ActivateTaxiReply.NotVisited);
                    return;
                }
            }

            int preferredMountDisplay = 0;
            MountRecord mount = CliDB.MountStorage.LookupByKey(activateTaxi.FlyingMountID);
            if (mount != null)
            {
                if (GetPlayer().HasSpell(mount.SourceSpellID))
                {
                    var mountDisplays = Global.DB2Mgr.GetMountDisplays(mount.Id);
                    if (mountDisplays != null)
                    {
                        List<MountXDisplayRecord> usableDisplays = mountDisplays.Where(mountDisplay =>
                        {
                            PlayerConditionRecord playerCondition = CliDB.PlayerConditionStorage.LookupByKey(mountDisplay.PlayerConditionID);
                            if (playerCondition != null)
                                return ConditionManager.IsPlayerMeetingCondition(GetPlayer(), playerCondition);

                            return true;
                        }).ToList();

                        if (!usableDisplays.Empty())
                            preferredMountDisplay = usableDisplays.SelectRandom().CreatureDisplayInfoID;
                    }
                }
            }

            List<int> nodes = new();
            TaxiPathGraph.GetCompleteNodeRoute(from, to, GetPlayer(), nodes);
            GetPlayer().ActivateTaxiPathTo(nodes, unit, 0, preferredMountDisplay);
        }

        internal bool TakeFlightForSI(Creature master,IReadOnlyList<int> nodes,long reservedMoney=0)
        {
            var player=GetPlayer();
            if(!IsServerControlled||player==null||master==null||nodes.Count<2||nodes.Count>256||
                player.GetNPCIfCanInteractWith(master.GetGUID(),NPCFlags1.FlightMaster,NPCFlags2.None)!=master)return false;
            int source=Global.ObjectMgr.GetNearestTaxiNode(master.GetPosition3D(),master.GetMapId(),player.GetTeam());
            if(source==0||nodes[0]!=source)return false;
            var flag=player.GetTeam()==Team.Alliance?TaxiNodeFlags.ShowOnAllianceMap:TaxiNodeFlags.ShowOnHordeMap;
            ulong rawFare=0,paidByLeg=0;
            float discount=player.GetReputationPriceDiscount(master);
            // SI uses the normal discovered network, not taxi-cheat/scripted
            // flights. Validate every transfer instead of skipping bad nodes.
            for(int i=0;i<nodes.Count;++i)
            {
                var node=CliDB.TaxiNodesStorage.LookupByKey(nodes[i]);
                if(node==null||!node.IsPartOfTaxiNetwork||!node.HasFlag(flag)||!player.m_taxi.IsTaximaskNodeKnown(node.Id))return false;
                var condition=CliDB.PlayerConditionStorage.LookupByKey(node.ConditionID);
                if(condition!=null&&!ConditionManager.IsPlayerMeetingCondition(player,condition))return false;
                if(i>0)
                {
                    Global.ObjectMgr.GetTaxiPath(nodes[i-1],nodes[i],out int path,out uint fare);
                    if(path==0||!CliDB.TaxiPathNodesByPath.TryGetValue(path,out var points)||points.Length<2||points.Any(p=>p==null))return false;
                    rawFare+=fare;
                    ulong legFare=player.ApplyClassicLegacyPriceMod((ulong)Math.Ceiling(fare*discount),4);
                    paidByLeg=legFare>ulong.MaxValue-paidByLeg?ulong.MaxValue:paidByLeg+legFare;
                }
            }
            if(rawFare>uint.MaxValue||reservedMoney<0||reservedMoney>player.GetMoney())return false;
            ulong actualFare=Math.Max(paidByLeg,player.ApplyClassicLegacyPriceMod((ulong)Math.Ceiling(rawFare*discount),4));
            if(actualFare>(ulong)(player.GetMoney()-reservedMoney))return false;
            // Native activation owns payment, mounts, packets and taxi movement.
            bool activated=player.ActivateTaxiPathTo(new List<int>(nodes),master);
            return activated||player.IsInFlight()||player.IsBeingTeleported();
        }

        public void SendActivateTaxiReply(ActivateTaxiReply reply = ActivateTaxiReply.Ok)
        {
            ActivateTaxiReplyPkt data = new();
            data.Reply = reply;
            SendPacket(data);
        }

        [WorldPacketHandler(ClientOpcodes.TaxiRequestEarlyLanding, Processing = PacketProcessing.ThreadSafe)]
        void HandleTaxiRequestEarlyLanding(TaxiRequestEarlyLanding taxiRequestEarlyLanding)
        {
            FlightPathMovementGenerator flight = GetPlayer().GetMotionMaster().GetCurrentMovementGenerator() as FlightPathMovementGenerator;
            if (flight != null)
            {
                if (GetPlayer().m_taxi.RequestEarlyLanding())
                {
                    flight.LoadPath(GetPlayer(), flight.GetPath()[flight.GetCurrentNode()].NodeIndex);
                    flight.Reset(GetPlayer());
                }
            }
        }
    }
}
