// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.Entities;

namespace Game.Chat
{
    [CommandGroup("gm")]
    class GMCommands
    {
        [Command("chat", RBACPermissions.CommandGmChat)]
        static bool HandleGMChatCommand(CommandHandler handler, bool? enableArg)
        {
            WorldSession session = handler.GetSession();
            if (session != null)
            {
                if (!enableArg.HasValue)
                {
                    if (session.HasPermission(RBACPermissions.ChatUseStaffBadge) && session.GetPlayer().IsGMChat())
                        session.SendNotification(ForeverStrings.GmChatOn);
                    else
                        session.SendNotification(ForeverStrings.GmChatOff);
                    return true;
                }

                if (enableArg.HasValue)
                {
                    session.GetPlayer().SetGMChat(true);
                    session.SendNotification(ForeverStrings.GmChatOn);
                }
                else
                {
                    session.GetPlayer().SetGMChat(false);
                    session.SendNotification(ForeverStrings.GmChatOff);
                }

                return true;
            }

            handler.SendSysMessage(ForeverStrings.UseBol);
            return false;
        }

        [Command("fly", RBACPermissions.CommandGmFly)]
        static bool HandleGMFlyCommand(CommandHandler handler, bool enable)
        {
            Player target = handler.GetSelectedPlayer();
            if (target == null)
                target = handler.GetPlayer();

            if (enable)
            { 
                target.SetCanFly(true);
                target.SetCanTransitionBetweenSwimAndFly(true);
            }
            else
            { 
                target.SetCanFly(false);
                target.SetCanTransitionBetweenSwimAndFly(false);
            }

            handler.SendSysMessage(ForeverStrings.CommandFlymodeStatus, handler.GetNameLink(target), enable ? "on" : "off");
            return true;
        }

        [Command("ingame", RBACPermissions.CommandGmIngame, true)]
        static bool HandleGMListIngameCommand(CommandHandler handler)
        {
            bool first = true;
            bool footer = false;

            foreach (var player in Global.ObjAccessor.GetPlayers())
            {
                AccountTypes playerSec = player.GetSession().GetSecurity();
                if ((player.IsGameMaster() ||
                    (player.GetSession().HasPermission(RBACPermissions.CommandsAppearInGmList) &&
                     playerSec <= (AccountTypes)WorldConfig.Values[WorldCfg.GmLevelInGmList].Int32)) &&
                    (handler.GetSession() == null || player.IsVisibleGloballyFor(handler.GetSession().GetPlayer())))
                {
                    if (first)
                    {
                        first = false;
                        footer = true;
                        handler.SendSysMessage(ForeverStrings.GmsOnSrv);
                        handler.SendSysMessage("========================");
                    }
                    int size = player.GetName().Length;
                    byte security = (byte)playerSec;
                    int max = ((16 - size) / 2);
                    int max2 = max;
                    if ((max + max2 + size) == 16)
                        max2 = max - 1;
                    if (handler.GetSession() != null)
                        handler.SendSysMessage("|    {0} GMLevel {1}", player.GetName(), security);
                    else
                        handler.SendSysMessage("|{0}{1}{2}|   {3}  |", max, " ", player.GetName(), max2, " ", security);
                }
            }
            if (footer)
                handler.SendSysMessage("========================");
            if (first)
                handler.SendSysMessage(ForeverStrings.GmsNotLogged);
            return true;
        }

        [Command("list", RBACPermissions.CommandGmList, true)]
        static bool HandleGMListFullCommand(CommandHandler handler)
        {
            // Get the accounts with GM Level >0
            PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.SEL_GM_ACCOUNTS);
            stmt.SetUInt8(0, (byte)AccountTypes.Moderator);
            stmt.SetInt32(1, Global.WorldMgr.GetRealm().Id.Index);
            SQLResult result = DB.Login.Query(stmt);

            if (!result.IsEmpty())
            {
                handler.SendSysMessage( ForeverStrings.Gmlist);
                handler.SendSysMessage("========================");
                // Cycle through them. Display username and GM level
                do
                {
                    string name = result.Read<string>(0);
                    byte security = result.Read<byte>(1);
                    int max = (16 - name.Length) / 2;
                    int max2 = max;
                    if ((max + max2 + name.Length) == 16)
                        max2 = max - 1;
                    string padding = "";
                    if (handler.GetSession() != null)
                        handler.SendSysMessage($"|    {name} GMLevel {security}");
                    else
                        handler.SendSysMessage($"|{padding.PadRight(max)}{name}{padding.PadRight(max2)}|   {security}  |");
                } while (result.NextRow());
                handler.SendSysMessage("========================");
            }
            else
                handler.SendSysMessage( ForeverStrings.GmlistEmpty);
            return true;
        }

        [Command("off", RBACPermissions.CommandGm)]
        static bool HandleGMOffCommand(CommandHandler handler)
        {
            handler.GetPlayer().SetGameMaster(false);
            handler.GetPlayer().UpdateTriggerVisibility();
            handler.GetSession().SendNotification(ForeverStrings.GmOff);
            return true;
        }

        [Command("on", RBACPermissions.CommandGm)]
        static bool HandleGMOnCommand(CommandHandler handler)
        {
            handler.GetPlayer().SetGameMaster(true);
            handler.GetPlayer().UpdateTriggerVisibility();
            handler.GetSession().SendNotification(ForeverStrings.GmOn);
            return true;
        }

        [Command("visible", RBACPermissions.CommandGmVisible)]
        static bool HandleGMVisibleCommand(CommandHandler handler, bool? visibleArg)
        {
            Player _player = handler.GetSession().GetPlayer();

            if (!visibleArg.HasValue)
            {
                handler.SendSysMessage(ForeverStrings.YouAre, 
                    _player.IsGMVisible() 
                    ? Global.ObjectMgr.GetForeverString(ForeverStrings.Visible) 
                    : Global.ObjectMgr.GetForeverString(ForeverStrings.Invisible));

                return true;
            }

            int VISUAL_AURA = 37800;

            if (visibleArg.Value)
            {
                if (_player.HasAura(VISUAL_AURA, ObjectGuid.Empty))
                    _player.RemoveAurasDueToSpell(VISUAL_AURA);

                _player.SetGMVisible(true);
                _player.UpdateObjectVisibility();
                handler.GetSession().SendNotification(ForeverStrings.InvisibleVisible);
            }
            else
            {
                _player.AddAura(VISUAL_AURA, _player);
                _player.SetGMVisible(false);
                _player.UpdateObjectVisibility();
                handler.GetSession().SendNotification(ForeverStrings.InvisibleInvisible);
            }

            return true;
        }
    }
}
