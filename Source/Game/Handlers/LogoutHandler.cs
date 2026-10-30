// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using Game.Networking;
using Game.Networking.Packets;

namespace Game
{
    public partial class WorldSession
    {
        internal bool PreventSIIdleLogout(bool idle)
        {
            if(!idle||_player?.IsSelfSIControlled!=true)return false;
            _player.KeepSelfSIActive();
            SetLogoutStartTime(ServerTime.Zero);
            SendPacket(new LogoutCancelAck());
            return true;
        }
        [WorldPacketHandler(ClientOpcodes.LogoutRequest)]
        void HandleLogoutRequest(LogoutRequest logoutRequest)
        {
            if(PreventSIIdleLogout(logoutRequest.IdleLogout))return;
            Player pl = GetPlayer();
            if (!GetPlayer().GetLootGUID().IsEmpty())
                GetPlayer().SendLootReleaseAll();

            bool instantLogout = GetPlayer().IsInFlight();
            if (!logoutRequest.IdleLogout)
                instantLogout |= (GetPlayer().HasPlayerFlag(PlayerFlags.Resting) && !GetPlayer().IsInCombat())
                    || HasPermission(RBACPermissions.InstantLogout);

            bool canLogoutInCombat = pl.HasPlayerFlag(PlayerFlags.Resting);

            int reason = 0;
            if (pl.IsInCombat() && !canLogoutInCombat)
                reason = 1;
            else if (pl.IsFalling())
                reason = 3;                                         // is jumping or falling
            else if (pl.duel != null || pl.HasAura(9454)) // is dueling or frozen by GM via freeze command
                reason = 2;                                         // FIXME - Need the correct value

            LogoutResponse logoutResponse = new();
            logoutResponse.LogoutResult = reason;
            logoutResponse.Instant = instantLogout;
            SendPacket(logoutResponse);

            Log.outInfo(LogFilter.Network,
                $"Logout request from {GetPlayerInfo()}: idle {logoutRequest.IdleLogout}, result {reason}, instant {instantLogout}");

            if (reason != 0)
            {
                SetLogoutStartTime(ServerTime.Zero);
                return;
            }

            // instant logout in taverns/cities or on taxi or for admins, gm's, mod's if its enabled in worldserver.conf
            if (instantLogout)
            {
                LogoutPlayer(true);
                return;
            }

            // not set flags if player can't free move to prevent lost state at logout cancel
            if (pl.CanFreeMove())
            {
                if (pl.GetStandState() == UnitStandStateType.Stand)
                    pl.SetStandState(UnitStandStateType.Sit);
                pl.SetRooted(true);
                pl.SetUnitFlag(UnitFlags.Stunned);
                _logoutRooted = true;
            }

            SetLogoutStartTime(LoopTime.ServerTime);
        }

        [WorldPacketHandler(ClientOpcodes.LogoutCancel)]
        void HandleLogoutCancel(LogoutCancel packet)
        {
            // Player have already logged out serverside, too late to cancel
            if (GetPlayer() == null)
                return;

            // The client can send CANCEL as its countdown reaches zero. Finish an
            // already expired logout before clearing its timer in this packet handler.
            if (ShouldLogOut(LoopTime.ServerTime) && m_playerLoading.IsEmpty())
            {
                LogoutPlayer(true);
                return;
            }

            SetLogoutStartTime(ServerTime.Zero);

            SendPacket(new LogoutCancelAck());

            Log.outInfo(LogFilter.Network, $"Logout cancelled by {GetPlayerInfo()}");

            // not remove flags if can't free move - its not set in Logout request code.
            // The logout request rooted and stunned the player itself, so always undo that here; otherwise a
            // cancelled idle logout could leave the character stuck in place.
            if (_logoutRooted || GetPlayer().CanFreeMove())
            {
                _logoutRooted = false;

                //!we can move again
                GetPlayer().SetRooted(false);

                //! Stand Up
                GetPlayer().SetStandState(UnitStandStateType.Stand);

                //! DISABLE_ROTATE
                GetPlayer().RemoveUnitFlag(UnitFlags.Stunned);
            }
        }
    }
}
