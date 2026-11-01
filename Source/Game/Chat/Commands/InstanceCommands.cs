// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using System;
using System.Collections.Generic;

namespace Game.Chat
{
    [CommandGroup("instance")]
    class InstanceCommands
    {
        [Command("getbossstate", RBACPermissions.CommandInstanceGetBossState)]
        static bool HandleInstanceGetBossStateCommand(CommandHandler handler, int encounterId, PlayerIdentifier player)
        {
            // Character name must be provided when using this from console.
            if (player == null || handler.GetSession() == null)
            {
                handler.SendSysMessage(ForeverStrings.CmdSyntax);
                return false;
            }

            if (player == null)
                player = PlayerIdentifier.FromSelf(handler);

            if (player.IsConnected())
            {
                handler.SendSysMessage(ForeverStrings.PlayerNotFound);
                return false;
            }

            InstanceMap map = player.GetConnectedPlayer().GetMap().ToInstanceMap();
            if (map == null)
            {
                handler.SendSysMessage(ForeverStrings.NotDungeon);
                return false;
            }

            if (map.GetInstanceScript() == null)
            {
                handler.SendSysMessage(ForeverStrings.NoInstanceData);
                return false;
            }

            if (encounterId > map.GetInstanceScript().GetEncounterCount())
            {
                handler.SendSysMessage(ForeverStrings.BadValue);
                return false;
            }

            EncounterState state = map.GetInstanceScript().GetBossState(encounterId);
            handler.SendSysMessage(ForeverStrings.CommandInstGetBossState, encounterId, state);
            return true;
        }

        [Command("listbinds", RBACPermissions.CommandInstanceListbinds)]
        static bool HandleInstanceListBindsCommand(CommandHandler handler)
        {
            Player player = handler.GetSelectedPlayer();
            if (player == null)
                player = handler.GetSession().GetPlayer();

            ServerTime now = LoopTime.ServerTime;
            var instanceLocks = Global.InstanceLockMgr.GetInstanceLocksForPlayer(player.GetGUID());
            foreach (InstanceLock instanceLock in instanceLocks)
            {
                MapDb2Entries entries = new(instanceLock.GetMapId(), instanceLock.GetDifficultyId());
                string timeleft = !instanceLock.IsExpired() ? Time.SpanToTimeString(instanceLock.GetEffectiveExpiryTime() - now) : "-";
                handler.SendSysMessage(ForeverStrings.CommandListBindInfo,
                    entries.Map.Id, entries.Map.MapName[Global.WorldMgr.GetDefaultDbcLocale()],
                    entries.MapDifficulty.DifficultyID, CliDB.DifficultyStorage.LookupByKey(entries.MapDifficulty.DifficultyID).Name,
                    instanceLock.GetInstanceId(),
                    handler.GetForeverString(instanceLock.IsExpired() ? ForeverStrings.Yes : ForeverStrings.No),
                    handler.GetForeverString(instanceLock.IsExtended() ? ForeverStrings.Yes : ForeverStrings.No),
                    timeleft);
            }

            handler.SendSysMessage(ForeverStrings.CommandListBindPlayerBinds, instanceLocks.Count);
            return true;
        }

        [Command("setbossstate", RBACPermissions.CommandInstanceSetBossState)]
        static bool HandleInstanceSetBossStateCommand(CommandHandler handler, int encounterId, EncounterState state, PlayerIdentifier player)
        {
            // Character name must be provided when using this from console.
            if (player == null || handler.GetSession() == null)
            {
                handler.SendSysMessage(ForeverStrings.CmdSyntax);
                return false;
            }

            if (player == null)
                player = PlayerIdentifier.FromSelf(handler);

            if (!player.IsConnected())
            {
                handler.SendSysMessage(ForeverStrings.PlayerNotFound);
                return false;
            }

            InstanceMap map = player.GetConnectedPlayer().GetMap().ToInstanceMap();
            if (map == null)
            {
                handler.SendSysMessage(ForeverStrings.NotDungeon);
                return false;
            }

            if (map.GetInstanceScript() == null)
            {
                handler.SendSysMessage(ForeverStrings.NoInstanceData);
                return false;
            }

            // Reject improper values.
            if (encounterId > map.GetInstanceScript().GetEncounterCount())
            {
                handler.SendSysMessage(ForeverStrings.BadValue);
                return false;
            }

            map.GetInstanceScript().SetBossState(encounterId, state);
            handler.SendSysMessage(ForeverStrings.CommandInstSetBossState, encounterId, state);
            return true;
        }

        [Command("stats", RBACPermissions.CommandInstanceStats, true)]
        static bool HandleInstanceStatsCommand(CommandHandler handler)
        {
            handler.SendSysMessage($"instances loaded: {Global.MapMgr.GetNumInstances()}");
            handler.SendSysMessage($"players in instances: {Global.MapMgr.GetNumPlayersInInstances()}");

            InstanceLocksStatistics statistics = Global.InstanceLockMgr.GetStatistics();

            handler.SendSysMessage(ForeverStrings.CommandInstStatSaves, statistics.InstanceCount);
            handler.SendSysMessage(ForeverStrings.CommandInstStatPlayersbound, statistics.PlayerCount);

            return true;
        }

        [Command("unbind", RBACPermissions.CommandInstanceUnbind)]
        static bool HandleInstanceUnbindCommand(CommandHandler handler, [VariantArg<int, string>] object mapArg, int? difficultyArg)
        {
            Player player = handler.GetSelectedPlayer();
            if (player == null)
                player = handler.GetSession().GetPlayer();

            int? mapId = null;
            Difficulty? difficulty = null;

            if (mapArg is int)
                mapId = (int)mapArg;

            if (difficultyArg.HasValue && CliDB.DifficultyStorage.ContainsKey((Difficulty)difficultyArg.Value))
                difficulty = (Difficulty)difficultyArg;

            List<InstanceLock> locksReset = new();
            List<InstanceLock> locksNotReset = new();

            Global.InstanceLockMgr.ResetInstanceLocksForPlayer(player.GetGUID(), mapId, difficulty, locksReset, locksNotReset);

            ServerTime now = LoopTime.ServerTime;
            foreach (InstanceLock instanceLock in locksReset)
            {
                MapDb2Entries entries = new(instanceLock.GetMapId(), instanceLock.GetDifficultyId());
                string timeleft = !instanceLock.IsExpired() ? Time.SpanToTimeString(instanceLock.GetEffectiveExpiryTime() - now) : "-";
                handler.SendSysMessage(ForeverStrings.CommandInstUnbindUnbinding,
                    entries.Map.Id, entries.Map.MapName[Global.WorldMgr.GetDefaultDbcLocale()],
                    entries.MapDifficulty.DifficultyID, CliDB.DifficultyStorage.LookupByKey(entries.MapDifficulty.DifficultyID).Name,
                    instanceLock.GetInstanceId(),
                    handler.GetForeverString(instanceLock.IsExpired() ? ForeverStrings.Yes : ForeverStrings.No),
                    handler.GetForeverString(instanceLock.IsExtended() ? ForeverStrings.Yes : ForeverStrings.No),
                    timeleft);
            }

            handler.SendSysMessage(ForeverStrings.CommandInstUnbindUnbound, locksReset.Count);

            foreach (InstanceLock instanceLock in locksNotReset)
            {
                MapDb2Entries entries = new(instanceLock.GetMapId(), instanceLock.GetDifficultyId());
                string timeleft = !instanceLock.IsExpired() ? Time.SpanToTimeString(instanceLock.GetEffectiveExpiryTime() - now) : "-";
                handler.SendSysMessage(ForeverStrings.CommandInstUnbindFailed,
                    entries.Map.Id, entries.Map.MapName[Global.WorldMgr.GetDefaultDbcLocale()],
                    entries.MapDifficulty.DifficultyID, CliDB.DifficultyStorage.LookupByKey(entries.MapDifficulty.DifficultyID).Name,
                    instanceLock.GetInstanceId(),
                    handler.GetForeverString(instanceLock.IsExpired() ? ForeverStrings.Yes : ForeverStrings.No),
                    handler.GetForeverString(instanceLock.IsExtended() ? ForeverStrings.Yes : ForeverStrings.No),
                    timeleft);
            }

            player.SendRaidInfo();

            return true;
        }
    }
}
