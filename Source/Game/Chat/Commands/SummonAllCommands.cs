// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Chat
{
    class SummonAllCommands
    {
        // Where each summoned player stood before .summonall, kept until .returnall sends them back.
        // In memory only: a restart forgets it and the players stay where they were summoned.
        class SavedLocation
        {
            public WorldLocation Location;
            public int InstanceId;
            public int ZoneId;
            // an SI player switched to the duel goal by .summonall, resumed by .returnall
            public bool ResumeBot;
        }

        static readonly Dictionary<ObjectGuid, SavedLocation> _savedLocations = new();
        static readonly object _lock = new();

        // Teleport every online player to the GM
        [CommandNonGroup("summonall", RBACPermissions.CommandSummon)]
        static bool HandleSummonAllCommand(CommandHandler handler)
        {
            Player gm = handler.GetSession().GetPlayer();
            Map map = gm.GetMap();
            if (map.IsBattlegroundOrArena() || map.IsDungeon())
            {
                handler.SendSysMessage("You can only use .summonall outside of instances and battlegrounds.");
                return false;
            }

            if (gm.IsInFlight())
                gm.FinishTaxiFlight();
            else
                gm.SaveRecallPosition();

            int summoned = 0, skipped = 0;
            lock (_lock)
            {
                foreach (Player target in Global.ObjAccessor.GetPlayers().ToList())
                {
                    if (target == gm || !target.IsInWorld)
                        continue;

                    if (target.GetSession().PlayerLoading() || target.IsBeingTeleported() || target.GetMap().IsBattlegroundOrArena()
                        || target.GetSession().GetSecurity() > handler.GetSession().GetSecurity())
                    {
                        ++skipped;
                        continue;
                    }

                    // keep the original spot of a player who was already summoned and not returned yet
                    if (!_savedLocations.TryGetValue(target.GetGUID(), out SavedLocation saved))
                        _savedLocations[target.GetGUID()] = saved = SaveLocation(target);

                    // an SI player left questing walks back to its questing area; the duel goal keeps it at the GM,
                    // challenging the players around it
                    if (target.GetSession().IsServerControlled && target.Controller != null)
                    {
                        target.Controller.Order("duel");
                        saved.ResumeBot = true;
                    }

                    if (target.IsInFlight())
                        target.FinishTaxiFlight();

                    // a sunflower spiral around the GM, so they do not all land on one spot (~45 yards across for 800)
                    float distance = 2.0f + 1.5f * MathF.Sqrt(summoned);
                    float angle = summoned * 2.39996f;
                    gm.GetClosePoint(out float x, out float y, out float z, target.GetCombatReach(), distance, angle);

                    if (!target.TeleportTo(gm.GetMapId(), x, y, z, target.GetOrientation(), 0, map.GetInstanceId()))
                    {
                        if (saved.ResumeBot)
                            target.Controller.Order("resume");
                        _savedLocations.Remove(target.GetGUID());
                        ++skipped;
                        continue;
                    }

                    PhasingHandler.InheritPhaseShift(target, gm);
                    target.UpdateObjectVisibility();

                    if (handler.NeedReportToTarget(target))
                        target.SendSysMessage(CypherStrings.SummonedBy, handler.PlayerLink(gm.GetName()));

                    ++summoned;
                }
            }

            handler.SendSysMessage($"Summoned {summoned} player(s), skipped {skipped}. Use .returnall to send them back.");
            return true;
        }

        // Teleport every player summoned by .summonall back to where they were
        [CommandNonGroup("returnall", RBACPermissions.CommandSummon)]
        static bool HandleReturnAllCommand(CommandHandler handler)
        {
            int returned = 0, offline = 0, pending = 0;
            lock (_lock)
            {
                if (_savedLocations.Count == 0)
                {
                    handler.SendSysMessage("No players are waiting to be returned.");
                    return true;
                }

                foreach (var (guid, saved) in _savedLocations.ToList())
                {
                    Player target = Global.ObjAccessor.FindConnectedPlayer(guid);
                    if (target == null)
                    {
                        // logged out at the GM's spot: put them back in the DB so they log in where they were
                        Player.SavePositionInDB(saved.Location, saved.ZoneId, guid);
                        _savedLocations.Remove(guid);
                        ++offline;
                        continue;
                    }

                    // logging in, mid-teleport or in a battleground: keep them for the next .returnall
                    if (!target.IsInWorld || target.GetSession().PlayerLoading() || target.IsBeingTeleported() || target.GetMap().IsBattlegroundOrArena())
                    {
                        ++pending;
                        continue;
                    }

                    if (target.IsInFlight())
                        target.FinishTaxiFlight();

                    int? instanceId = saved.InstanceId != 0 ? saved.InstanceId : null;
                    if (!target.TeleportTo(saved.Location, 0, instanceId))
                    {
                        ++pending;
                        continue;
                    }

                    target.UpdateObjectVisibility();
                    if (saved.ResumeBot && target.Controller != null)
                        target.Controller.Order("resume");
                    _savedLocations.Remove(guid);
                    ++returned;
                }
            }

            handler.SendSysMessage($"Returned {returned} player(s), {offline} offline player(s) moved in the database, {pending} still waiting (run .returnall again).");
            return true;
        }

        static SavedLocation SaveLocation(Player target)
        {
            // a player on a flight path is saved at the end of the path rather than in mid-air
            if (target.IsInFlight())
            {
                List<int> path = target.m_taxi.GetPath();
                TaxiNodesRecord node = path.Count != 0 ? CliDB.TaxiNodesStorage.LookupByKey(path[^1]) : null;
                if (node != null)
                {
                    WorldLocation nodeLoc = new(node.ContinentID, node.Pos.X, node.Pos.Y, node.Pos.Z, target.GetOrientation());
                    return new SavedLocation
                    {
                        Location = nodeLoc,
                        InstanceId = 0,
                        ZoneId = Global.TerrainMgr.GetZoneId(PhasingHandler.EmptyPhaseShift, nodeLoc)
                    };
                }
            }

            return new SavedLocation
            {
                Location = new WorldLocation(target.GetMapId(), target.GetPositionX(), target.GetPositionY(), target.GetPositionZ(), target.GetOrientation()),
                InstanceId = target.GetMap().IsDungeon() ? target.GetInstanceId() : 0,
                ZoneId = target.GetZoneId()
            };
        }
    }
}
