// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Arenas;
using System;

namespace Game.Chat
{
    [CommandGroup("arena")]
    class ArenaCommands
    {
        [Command("create", RBACPermissions.CommandArenaCreate, true)]
        static bool HandleArenaCreateCommand(CommandHandler handler, PlayerIdentifier captain, string name, ArenaTypes type)
        {
            if (Global.ArenaTeamMgr.GetArenaTeamByName(name) != null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNameExists, name);
                return false;
            }
            
            if (captain == null)
                captain = PlayerIdentifier.FromTargetOrSelf(handler);
            if (captain == null)
                return false;

            if (Global.CharacterCacheStorage.GetCharacterArenaTeamIdByGuid(captain.GetGUID(), (byte)type) != 0)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorSize, captain.GetName());
                return false;
            }

            ArenaTeam arena = new();

            if (!arena.Create(captain.GetGUID(), (byte)type, name, 4293102085, 101, 4293253939, 4, 4284049911))
            {
                handler.SendSysMessage(ForeverStrings.BadValue);
                return false;
            }

            Global.ArenaTeamMgr.AddArenaTeam(arena);
            handler.SendSysMessage(ForeverStrings.ArenaCreate, arena.GetName(), arena.GetId(), arena.GetArenaType(), arena.GetCaptain());

            return true;
        }

        [Command("disband", RBACPermissions.CommandArenaDisband, true)]
        static bool HandleArenaDisbandCommand(CommandHandler handler, int teamId)
        {
            ArenaTeam arena = Global.ArenaTeamMgr.GetArenaTeamById(teamId);
            if (arena == null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNotFound, teamId);
                return false;
            }

            if (arena.IsFighting())
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorCombat);
                return false;
            }

            string name = arena.GetName();
            arena.Disband();

            handler.SendSysMessage(ForeverStrings.ArenaDisband, name, teamId);
            return true;
        }

        [Command("rename", RBACPermissions.CommandArenaRename, true)]
        static bool HandleArenaRenameCommand(CommandHandler handler, string oldName, string newName)
        {
            ArenaTeam arena = Global.ArenaTeamMgr.GetArenaTeamByName(oldName);
            if (arena == null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNameNotFound, oldName);
                return false;
            }

            if (Global.ArenaTeamMgr.GetArenaTeamByName(newName) != null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNameExists, oldName);
                return false;
            }

            if (arena.IsFighting())
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorCombat);
                return false;
            }

            if (!arena.SetName(newName))
            {
                handler.SendSysMessage(ForeverStrings.ArenaRename, arena.GetId(), oldName, newName);
                return true;
            }

            handler.SendSysMessage(ForeverStrings.BadValue);
            return false;
        }

        [Command("captain", RBACPermissions.CommandArenaCaptain)]
        static bool HandleArenaCaptainCommand(CommandHandler handler, int teamId, PlayerIdentifier target)
        {
            ArenaTeam arena = Global.ArenaTeamMgr.GetArenaTeamById(teamId);
            if (arena == null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNotFound, teamId);
                return false;
            }

            if (arena.IsFighting())
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorCombat);
                return false;
            }

            if (target == null)
                target = PlayerIdentifier.FromTargetOrSelf(handler);
            if (target == null)
                return false;
            
            if (!arena.IsMember(target.GetGUID()))
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNotMember, target.GetName(), arena.GetName());
                return false;
            }

            if (arena.GetCaptain() == target.GetGUID())
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorCaptain, target.GetName(), arena.GetName());
                return false;
            }

            if (!Global.CharacterCacheStorage.GetCharacterNameByGuid(arena.GetCaptain(), out string oldCaptainName))
                return false;

            arena.SetCaptain(target.GetGUID());
            handler.SendSysMessage(ForeverStrings.ArenaCaptain, arena.GetName(), arena.GetId(), oldCaptainName, target.GetName());

            return true;
        }

        [Command("info", RBACPermissions.CommandArenaInfo, true)]
        static bool HandleArenaInfoCommand(CommandHandler handler, int teamId)
        {
            ArenaTeam arena = Global.ArenaTeamMgr.GetArenaTeamById(teamId);
            if (arena == null)
            {
                handler.SendSysMessage(ForeverStrings.ArenaErrorNotFound, teamId);
                return false;
            }

            handler.SendSysMessage(ForeverStrings.ArenaInfoHeader, arena.GetName(), arena.GetId(), arena.GetRating(), arena.GetArenaType(), arena.GetArenaType());
            foreach (var member in arena.GetMembers())
                handler.SendSysMessage(ForeverStrings.ArenaInfoMembers, member.Name, member.Guid, member.PersonalRating, (arena.GetCaptain() == member.Guid ? "- Captain" : ""));

            return true;
        }

        [Command("lookup", RBACPermissions.CommandArenaLookup)]
        static bool HandleArenaLookupCommand(CommandHandler handler, string needle)
        {
            if (needle.IsEmpty())
                return false;

            bool found = false;
            foreach (var (_, team) in Global.ArenaTeamMgr.GetArenaTeamMap())
            {
                if (team.GetName().Equals(needle, StringComparison.OrdinalIgnoreCase))
                {
                    if (handler.GetSession() != null)
                    {
                        handler.SendSysMessage(ForeverStrings.ArenaLookup, team.GetName(), team.GetId(), team.GetArenaType(), team.GetArenaType());
                        found = true;
                        continue;
                    }
                }
            }

            if (!found)
                handler.SendSysMessage(ForeverStrings.ArenaErrorNameNotFound, needle);

            return true;
        }
    }
}
