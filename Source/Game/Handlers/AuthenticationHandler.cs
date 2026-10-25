// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework;
using Framework.Constants;
using Game.Networking.Packets;
using System;

namespace Game
{
    public partial class WorldSession
    {
        public void SendAuthResponse(BattlenetRpcErrorCode code, bool queued, uint queuePos = 0)
        {
            AuthResponse response = new();
            response.Result = code;

            if (code == BattlenetRpcErrorCode.Ok)
            {
                response.SuccessInfo = new();

                response.SuccessInfo = new AuthResponse.AuthSuccessInfo();
                response.SuccessInfo.ActiveExpansionLevel = (byte)GetExpansion();
                response.SuccessInfo.AccountExpansionLevel = (byte)GetAccountExpansion();
                response.SuccessInfo.VirtualRealmAddress = Global.WorldMgr.GetVirtualRealmAddress();
                response.SuccessInfo.Time = LoopTime.UnixServerTime;

                var realm = Global.WorldMgr.GetRealm();

                // Send current home realm. Also there is no need to send it later in realm queries.
                response.SuccessInfo.VirtualRealms.Add(new VirtualRealmInfo(realm.Id.GetAddress(), true, false, realm.Name, realm.NormalizedName));

                if (HasPermission(RBACPermissions.UseCharacterTemplates))
                    foreach (var templ in Global.CharacterTemplateDataStorage.GetCharacterTemplates().Values)
                        response.SuccessInfo.Templates.Add(templ);

                response.SuccessInfo.AvailableClasses = Global.ObjectMgr.GetClassExpansionRequirements();
            }

            if (queued)
            {
                AuthWaitInfo waitInfo = new();
                waitInfo.WaitCount = queuePos;
                response.WaitInfo = waitInfo;
            }

            SendPacket(response);
        }

        public void SendAuthWaitQueue(uint position)
        {
            if (position != 0)
            {
                WaitQueueUpdate waitQueueUpdate = new();
                waitQueueUpdate.WaitInfo.WaitCount = position;
                waitQueueUpdate.WaitInfo.WaitTime = 0;
                waitQueueUpdate.WaitInfo.HasFCM = false;
                SendPacket(waitQueueUpdate);
            }
            else
                SendPacket(new WaitQueueFinish());
        }

        public void SendClientCacheVersion(uint version)
        {
            ClientCacheVersion cache = new();
            cache.CacheVersion = version;
            SendPacket(cache);
        }

        public void SendSetTimeZoneInformation()
        {
            TimeSpan timezoneOffset = Timezone.GetSystemZoneOffset();
            string realTimezone = Timezone.GetSystemZoneName();
            string clientSupportedTZ = Timezone.FindClosestClientSupportedTimezone(realTimezone, timezoneOffset);

            SetTimeZoneInformation packet = new();
            packet.ServerTimeTZ = clientSupportedTZ;
            packet.GameTimeTZ = clientSupportedTZ;
            packet.ServerRegionalTZ = clientSupportedTZ;

            SendPacket(packet);
        }

        public void SendFeatureSystemStatusGlueScreen()
        {
            FeatureSystemStatusGlueScreen features = new();
            features.BpayStoreAvailable = true; // Classic 1.60: C_StoreSecure.IsAvailable() at character select
            features.BpayStoreDisabledByParentalControls = false;
            features.CharUndeleteEnabled = WorldConfig.Values[WorldCfg.FeatureSystemCharacterUndeleteEnabled].Bool;
            features.BpayStoreEnabled = WorldConfig.Values[WorldCfg.FeatureSystemBpayStoreEnabled].Bool;
            features.MaxCharactersPerRealm = WorldConfig.Values[WorldCfg.CharactersPerRealm].Int32 ;
            features.MinimumExpansionLevel = Expansion.Classic;
            features.MaximumExpansionLevel = (Expansion)WorldConfig.Values[WorldCfg.Expansion].Int32;

            var europaTicketConfig = new EuropaTicketConfig();
            europaTicketConfig.ThrottleState.MaxTries = 10;
            europaTicketConfig.ThrottleState.PerMilliseconds = 60000;
            europaTicketConfig.ThrottleState.TryCount = 1;
            europaTicketConfig.ThrottleState.LastResetTimeBeforeNow = 111111;
            europaTicketConfig.TicketsEnabled = WorldConfig.Values[WorldCfg.SupportTicketsEnabled].Bool;
            europaTicketConfig.BugsEnabled = WorldConfig.Values[WorldCfg.SupportBugsEnabled].Bool;
            europaTicketConfig.ComplaintsEnabled = WorldConfig.Values[WorldCfg.SupportComplaintsEnabled].Bool;
            europaTicketConfig.SuggestionsEnabled = WorldConfig.Values[WorldCfg.SupportSuggestionsEnabled].Bool;

            features.EuropaTicketSystemStatus = europaTicketConfig;
            features.GameRuleValues.AddRange(Global.WorldMgr.GetGameRules());

            SendPacket(features);

            // Client variables as the official server sends them (reference AuthHandler.cpp).
            MirrorVars variables = new();
            variables.Variables.AddRange(new (string, string, int)[]
            {
                ("raidLockoutExtendEnabled", "1", 0),
                ("sellAllJunkEnabled", "1", 0),
                ("bypassItemLevelScalingCode", "0", 0),
                // 0 = the legacy Store window fed by the BattlePay world packets; 1 = the shop2 catalog shop over REST
                ("shop2Enabled", "0", 0),
                ("shop2HostUrlRequests", "https://trinity.actual.battle.net:8443", 0),
                ("shop2HostUrlAuth", "https://trinity.actual.battle.net:8443", 0),
                ("shop2UseConnectedRealmGameServiceRegionId", "0", 0),
                ("bpayStoreEnable", "1", 0),
                // Social window (O) layout as the official server sends it: side tabs with Recent Allies and the raid list
                ("recentAlliesEnabledClient", "1", 0),
                ("recentAlliesRequestDataThrottle", "180", 0),
                ("socialUIEnabledClient", "1", 0),
                ("battleNetTitleFriendsEnabledClient", "1", 0),
                ("battleNetTitleFriendTagsEnabledClient", "1", 0),
                ("battleNetTitleFriendCustomNamesEnabledClient", "1", 0),
                ("socialUIFriendsListEnabledClient", "1", 0),
                ("socialUIRaidListEnabledClient", "1", 0),
                ("socialUISocialQueueEnabledClient", "1", 0),
                ("legacyFriendSystemEnabledClient", "0", 0),
                ("browserEnabled", "1", 0),
                ("housingEnableCreateGuildNeighborhood", "0", 0),
                ("housingEnableDeleteHouse", "0", 0),
                ("housingServiceEnabled", "0", 0),
                ("housingEnableMoveHouse", "0", 0),
                ("housingEnableCreateCharterNeighborhood", "0", 0),
                ("housingEnableBuyHouse", "0", 0),
                ("housingMarketEnabled", "0", 0),
            });
            SendPacket(variables);
        }
    }
}
