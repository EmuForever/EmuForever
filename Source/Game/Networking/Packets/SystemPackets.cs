// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;
using System.Collections.Generic;
using Game.Entities;

namespace Game.Networking.Packets
{
    public class FeatureSystemStatus : ServerPacket
    {
        public FeatureSystemStatus() : base(ServerOpcodes.FeatureSystemStatus) { }

        public override void Write()
        {
            // Forever layout: TC FeatureSystemStatus::Write
            _worldPacket.WriteUInt8(ComplaintStatus);
            _worldPacket.WriteUInt32(CfgRealmID);
            _worldPacket.WriteInt32(CfgRealmRecID);
            _worldPacket.WriteUInt32(RAFSystem.MaxRecruits);
            _worldPacket.WriteUInt32(RAFSystem.MaxRecruitMonths);
            _worldPacket.WriteUInt32(RAFSystem.MaxRecruitmentUses);
            _worldPacket.WriteUInt32(RAFSystem.DaysInCycle);
            _worldPacket.WriteUInt32(RAFSystem.RewardsVersion);
            _worldPacket.WriteUInt32(CommercePricePollTimeSeconds);
            QuickJoinConfig.Write(_worldPacket);
            _worldPacket.WriteInt64(RedeemForBalanceAmount);
            _worldPacket.WriteUInt32(ClubsPresenceDelay);
            _worldPacket.WriteUInt32(ClubPresenceUnsubscribeDelay);
            Squelch.Write(_worldPacket);
            _worldPacket.WriteInt32(SocialRestriction);
            _worldPacket.WriteInt32(ContentSetID);
            _worldPacket.WriteInt32(DisabledGameModes.Count);
            _worldPacket.WriteInt32(GameRuleValues.Count);
            _worldPacket.WriteInt32(ActiveTimerunningSeasonID);
            _worldPacket.WriteInt32(RemainingTimerunningSeasonSeconds);
            _worldPacket.WriteInt16(MaxPlayerGuidLookupsPerRequest);
            _worldPacket.WriteInt16(NameLookupTelemetryInterval);
            _worldPacket.WriteUInt32(NotFoundCacheTimeSeconds);
            _worldPacket.WriteUInt32(RealmPvpTypeOverride);
            _worldPacket.WriteInt32(AddonChatThrottle.MaxTries);
            _worldPacket.WriteInt32(AddonChatThrottle.TriesRestoredPerSecond);
            _worldPacket.WriteInt32(AddonChatThrottle.UsedTriesPerMessage);
            _worldPacket.WriteInt32(GuildChatThrottle.UsedTriesPerMessage);
            _worldPacket.WriteInt32(GuildChatThrottle.TriesRestoredPerSecond);
            _worldPacket.WriteInt32(GroupChatThrottle.UsedTriesPerMessage);
            _worldPacket.WriteInt32(GroupChatThrottle.TriesRestoredPerSecond);
            _worldPacket.WriteFloat(AddonPerformanceMsgWarning);
            _worldPacket.WriteFloat(AddonPerformanceMsgError);
            _worldPacket.WriteFloat(AddonPerformanceMsgOverall);

            foreach (GameModeData gameMode in DisabledGameModes)
                gameMode.Write(_worldPacket);
            foreach (GameRuleValuePair gameRuleValue in GameRuleValues)
                gameRuleValue.Write(_worldPacket);

            _worldPacket.WriteBit(VoiceEnabled);
            _worldPacket.WriteBit(EuropaTicketSystemStatus.HasValue);
            _worldPacket.WriteBit(BpayStoreAvailable);
            _worldPacket.WriteBit(ItemRestorationButtonEnabled);
            _worldPacket.WriteBit(SessionAlert.HasValue);
            _worldPacket.WriteBit(RAFSystem.Enabled);
            _worldPacket.WriteBit(RAFSystem.RecruitingEnabled);
            _worldPacket.WriteBit(CharUndeleteEnabled);
            _worldPacket.WriteBit(RestrictedAccount);
            _worldPacket.WriteBit(CommerceServerEnabled);
            _worldPacket.WriteBit(TutorialEnabled);
            _worldPacket.WriteBit(VeteranTokenRedeemWillKick);
            _worldPacket.WriteBit(WorldTokenRedeemWillKick);
            _worldPacket.WriteBit(KioskModeEnabled);
            _worldPacket.WriteBit(CompetitiveModeEnabled);
            _worldPacket.WriteBit(RedeemForBalanceAvailable);
            _worldPacket.WriteBit(WarModeEnabled);
            _worldPacket.WriteBit(CommunitiesEnabled);
            _worldPacket.WriteBit(BnetGroupsEnabled);
            _worldPacket.WriteBit(CharacterCommunitiesEnabled);
            _worldPacket.WriteBit(ClubPresenceAllowSubscribeAll);
            _worldPacket.WriteBit(VoiceChatParentalDisabled);
            _worldPacket.WriteBit(VoiceChatParentalMuted);
            _worldPacket.WriteBit(QuestSessionEnabled);
            _worldPacket.WriteBit(IsChatMuted);
            _worldPacket.WriteBit(ClubFinderEnabled);
            _worldPacket.WriteBit(CommunityFinderEnabled);
            _worldPacket.WriteBit(BrowserCrashReporterEnabled);
            _worldPacket.WriteBit(SpeakForMeAllowed);
            _worldPacket.WriteBit(DoesAccountNeedAADCPrompt);
            _worldPacket.WriteBit(IsAccountOptedInToAADC);
            _worldPacket.WriteBit(LfgRequireAuthenticatorEnabled);
            _worldPacket.WriteBit(ScriptsDisallowedForBeta);
            _worldPacket.WriteBit(TimerunningEnabled);
            _worldPacket.WriteBit(PlayerIdentityOptionsEnabled);
            _worldPacket.WriteBit(IsPlayerContentTrackingEnabled);
            _worldPacket.WriteBit(LfdEnabled);
            _worldPacket.WriteBit(LfrEnabled);
            _worldPacket.WriteBit(PetHappinessEnabled);
            _worldPacket.WriteBit(GuildEventsEditsEnabled);
            _worldPacket.WriteBit(GuildTradeSkillsEnabled);
            _worldPacket.WriteBits(ClassicFlagBits10, 10);
            _worldPacket.WriteBit(IsAccountCurrencyTransferEnabled);
            _worldPacket.WriteBit(NetEaseChatTelemetryEnabled);
            _worldPacket.WriteBit(LobbyMatchmakerQueueFromMainlineEnabled);
            _worldPacket.WriteBit(CanSendLobbyMatchmakerPartyCustomizations);
            _worldPacket.WriteBit(AddonProfilingEnabled);
            _worldPacket.WriteBit(GlobalUserGeneratedContentMuteEnabled);
            _worldPacket.WriteBit(AccountUserGeneratedContentIsRisky);
            _worldPacket.WriteBit(FriendsDisabled);
            _worldPacket.FlushBits();

            if (EuropaTicketSystemStatus.HasValue)
                EuropaTicketSystemStatus.Value.Write(_worldPacket);
            if (SessionAlert.HasValue)
                SessionAlert.Value.Write(_worldPacket);
        }
        public bool VoiceEnabled;
        public bool BpayStoreAvailable;
        public SessionAlertConfig? SessionAlert;
        public EuropaTicketConfig? EuropaTicketSystemStatus;
        public uint CfgRealmID;
        public byte ComplaintStatus;
        public int CfgRealmRecID;
        public uint CommercePricePollTimeSeconds = 300;
        public long RedeemForBalanceAmount;
        public uint ClubsPresenceDelay;
        public uint ClubPresenceUnsubscribeDelay;
        public int SocialRestriction;
        public int ContentSetID;
        public int ActiveTimerunningSeasonID;
        public int RemainingTimerunningSeasonSeconds;
        public short MaxPlayerGuidLookupsPerRequest = 50;
        public short NameLookupTelemetryInterval = 600;
        public uint NotFoundCacheTimeSeconds = 10;
        public uint RealmPvpTypeOverride;
        public ChatThrottleParams AddonChatThrottle;
        public ChatThrottleParams GuildChatThrottle;
        public ChatThrottleParams GroupChatThrottle;
        public float AddonPerformanceMsgWarning;
        public float AddonPerformanceMsgError;
        public float AddonPerformanceMsgOverall;
        public List<GameModeData> DisabledGameModes = new();
        public List<GameRuleValuePair> GameRuleValues = new();
        public ushort ClassicFlagBits10 = 2;
        public bool ItemRestorationButtonEnabled;
        public bool CharUndeleteEnabled;
        public bool CommerceServerEnabled;
        public bool VeteranTokenRedeemWillKick;
        public bool WorldTokenRedeemWillKick;
        public bool RestrictedAccount;
        public bool TutorialEnabled;
        public bool KioskModeEnabled;
        public bool CompetitiveModeEnabled;
        public bool RedeemForBalanceAvailable;
        public bool WarModeEnabled = true;
        public bool CommunitiesEnabled;
        public bool BnetGroupsEnabled;
        public bool CharacterCommunitiesEnabled;
        public bool ClubPresenceAllowSubscribeAll;
        public bool VoiceChatParentalDisabled;
        public bool VoiceChatParentalMuted;
        public bool QuestSessionEnabled;
        public bool IsChatMuted;
        public bool ClubFinderEnabled;
        public bool CommunityFinderEnabled;
        public bool BrowserCrashReporterEnabled;
        public bool SpeakForMeAllowed;
        public bool DoesAccountNeedAADCPrompt;
        public bool IsAccountOptedInToAADC;
        public bool LfgRequireAuthenticatorEnabled;
        public bool ScriptsDisallowedForBeta;
        public bool TimerunningEnabled;
        public bool PlayerIdentityOptionsEnabled;
        public bool IsPlayerContentTrackingEnabled;
        public bool LfdEnabled;
        public bool LfrEnabled = true;
        public bool PetHappinessEnabled = true;
        public bool GuildEventsEditsEnabled = true;
        public bool GuildTradeSkillsEnabled = true;
        public bool IsAccountCurrencyTransferEnabled;
        public bool NetEaseChatTelemetryEnabled;
        public bool LobbyMatchmakerQueueFromMainlineEnabled;
        public bool CanSendLobbyMatchmakerPartyCustomizations;
        public bool AddonProfilingEnabled;
        public bool GlobalUserGeneratedContentMuteEnabled;
        public bool AccountUserGeneratedContentIsRisky;
        public bool FriendsDisabled;

        public SocialQueueConfig QuickJoinConfig;
        public SquelchInfo Squelch;
        public RafSystemFeatureInfo RAFSystem;
        public struct SessionAlertConfig
        {
            public void Write(WorldPacket data)
            {
                data.WriteInt32(Delay);
                data.WriteInt32(Period);
                data.WriteInt32(DisplayTime);
            }

            public int Delay;
            public int Period;
            public int DisplayTime;
        }

        public struct SocialQueueConfig
        {
            public void Write(WorldPacket data)
            {
                data.WriteFloat(ToastDuration);
                data.WriteFloat(DelayDuration);
                data.WriteFloat(QueueMultiplier);
                data.WriteFloat(PlayerMultiplier);
                data.WriteFloat(PlayerFriendValue);
                data.WriteFloat(PlayerGuildValue);
                data.WriteFloat(ThrottleInitialThreshold);
                data.WriteFloat(ThrottleDecayTime);
                data.WriteFloat(ThrottlePrioritySpike);
                data.WriteFloat(ThrottleMinThreshold);
                data.WriteFloat(ThrottlePvPPriorityNormal);
                data.WriteFloat(ThrottlePvPPriorityLow);
                data.WriteFloat(ThrottlePvPHonorThreshold);
                data.WriteFloat(ThrottleLfgListPriorityDefault);
                data.WriteFloat(ThrottleLfgListPriorityAbove);
                data.WriteFloat(ThrottleLfgListPriorityBelow);
                data.WriteFloat(ThrottleLfgListIlvlScalingAbove);
                data.WriteFloat(ThrottleLfgListIlvlScalingBelow);
                data.WriteFloat(ThrottleRfPriorityAbove);
                data.WriteFloat(ThrottleRfIlvlScalingAbove);
                data.WriteFloat(ThrottleDfMaxItemLevel);
                data.WriteFloat(ThrottleDfBestPriority);
                data.WriteBit(ToastsDisabled);
                data.FlushBits();
            }

            public bool ToastsDisabled;
            public float ToastDuration;
            public float DelayDuration;
            public float QueueMultiplier;
            public float PlayerMultiplier;
            public float PlayerFriendValue;
            public float PlayerGuildValue;
            public float ThrottleInitialThreshold;
            public float ThrottleDecayTime;
            public float ThrottlePrioritySpike;
            public float ThrottleMinThreshold;
            public float ThrottlePvPPriorityNormal;
            public float ThrottlePvPPriorityLow;
            public float ThrottlePvPHonorThreshold;
            public float ThrottleLfgListPriorityDefault;
            public float ThrottleLfgListPriorityAbove;
            public float ThrottleLfgListPriorityBelow;
            public float ThrottleLfgListIlvlScalingAbove;
            public float ThrottleLfgListIlvlScalingBelow;
            public float ThrottleRfPriorityAbove;
            public float ThrottleRfIlvlScalingAbove;
            public float ThrottleDfMaxItemLevel;
            public float ThrottleDfBestPriority;
        }

        public struct SquelchInfo
        {
            public void Write(WorldPacket data)
            {
                data.WriteBit(IsSquelched);
                data.WritePackedGuid(BnetAccountGuid);
                data.WritePackedGuid(GuildGuid);
            }

            public bool IsSquelched;
            public ObjectGuid BnetAccountGuid;
            public ObjectGuid GuildGuid;
        }

        public struct RafSystemFeatureInfo
        {
            public bool Enabled;
            public bool RecruitingEnabled;
            public uint MaxRecruits;
            public uint MaxRecruitMonths;
            public uint MaxRecruitmentUses;
            public uint DaysInCycle;
            public uint RewardsVersion;
        }
    }

    // SMSG_MIRROR_VARS: client variables the server sets (store, social window, browser).
    public class MirrorVars : ServerPacket
    {
        public MirrorVars() : base(ServerOpcodes.MirrorVars) { }

        public override void Write()
        {
            _worldPacket.WriteUInt32((uint)Variables.Count);
            foreach (var (name, value, updateType) in Variables)
            {
                byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name), valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
                _worldPacket.WriteBits(updateType, 1);
                _worldPacket.WriteBits(nameBytes.Length + 1, 24);   // SizedCString: length includes the terminator
                _worldPacket.WriteBits(valueBytes.Length + 1, 24);
                _worldPacket.FlushBits();
                _worldPacket.WriteBytes(nameBytes); _worldPacket.WriteUInt8(0);
                _worldPacket.WriteBytes(valueBytes); _worldPacket.WriteUInt8(0);
            }
        }

        public List<(string Name, string Value, int UpdateType)> Variables = new();
    }

    public class FeatureSystemStatusGlueScreen : ServerPacket
    {
        public FeatureSystemStatusGlueScreen() : base(ServerOpcodes.FeatureSystemStatusGlueScreen) { }

        public override void Write()
        {
            // Forever layout: TC FeatureSystemStatusGlueScreen::Write (active Classic branch; GameRules bodies appended after the header)
            // Classic 1.60.1 layout, ported from ForeverWorld Packets.FeatureSystemStatusGlueScreen.
            // Bit 0 (MSB of the first byte) is BpayStoreAvailable: the glue handler copies it to C_StoreSecure.IsAvailable(), so without
            // it the Store says "unavailable" at character select. The other flags are not mapped for Classic and stay off (reference).
            _worldPacket.WriteUInt8((byte)(BpayStoreAvailable ? 0x80 : 0x00));
            for (int i = 1; i < 6; i++)
                _worldPacket.WriteUInt8(0);
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteInt64(0);
            _worldPacket.WriteInt32(MaxCharactersPerRealm);
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(Framework.Configuration.ConfigMgr.GetDefaultValue("Realm.CfgContentSetID", 137));
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteUInt32(0); // GameRules: sent in-game with SMSG_FEATURE_SYSTEM_STATUS (reference)
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteInt32(86400);
            _worldPacket.WriteInt32(-1);
            _worldPacket.WriteInt16(50);
            _worldPacket.WriteInt16(600);
            _worldPacket.WriteUInt32(10);
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteInt32(0);
            _worldPacket.WriteUInt32(0);
        }


        public bool BpayStoreAvailable;                 // NYI
        public bool BpayStoreDisabledByParentalControls;// NYI
        public bool CharUndeleteEnabled;
        public bool BpayStoreEnabled;                   // NYI
        public bool CommerceSystemEnabled;              // NYI
        public bool Unk14;                              // NYI
        public bool WillKickFromWorld;                  // NYI
        public bool IsExpansionPreorderInStore;         // NYI
        public bool KioskModeEnabled;                   // NYI
        public bool CompetitiveModeEnabled;             // NYI
        public bool TrialBoostEnabled;                  // NYI
        public bool TokenBalanceEnabled;                // NYI
        public bool LiveRegionCharacterListEnabled;     // NYI
        public bool LiveRegionCharacterCopyEnabled;     // NYI
        public bool LiveRegionAccountCopyEnabled;       // NYI
        public bool LiveRegionKeyBindingsCopyEnabled;
        public bool Unknown901CheckoutRelated;          // NYI
        public bool AddonsDisabled;
        public bool Unused1000;
        public bool AccountSaveDataExportEnabled;
        public bool AccountLockedByExport;
        public EuropaTicketConfig? EuropaTicketSystemStatus;
        public List<int> LiveRegionCharacterCopySourceRegions = new();
        public uint TokenPollTimeSeconds;               // NYI
        public long TokenBalanceAmount;                 // NYI 
        public int MaxCharactersPerRealm;
        public uint BpayStoreProductDeliveryDelay;      // NYI
        public int ActiveCharacterUpgradeBoostType;     // NYI
        public int ActiveClassTrialBoostType;           // NYI
        public Expansion MinimumExpansionLevel;
        public Expansion MaximumExpansionLevel;
        public uint KioskSessionMinutes;
        public int ActiveSeason;                        // Currently active Classic season
        public List<GameRuleValuePair> GameRuleValues = new();
        public short MaxPlayerNameQueriesPerPacket = 50;
        public short PlayerNameQueryTelemetryInterval = 600;
        public TimeSpan PlayerNameQueryInterval = (Seconds)10;
        public int? LaunchETA;
        public List<DebugTimeEventInfo> DebugTimeEvents = new();
        public int Unused1007;
        public string RealmHiddenAlert;
    }    

    public class SetTimeZoneInformation : ServerPacket
    {
        public SetTimeZoneInformation() : base(ServerOpcodes.SetTimeZoneInformation) { }

        public override void Write()
        {
            _worldPacket.WriteBits(ServerTimeTZ.GetByteCount(), 7);
            _worldPacket.WriteBits(GameTimeTZ.GetByteCount(), 7);
            _worldPacket.WriteBits(ServerRegionalTZ.GetByteCount(), 7);
            _worldPacket.FlushBits();

            _worldPacket.WriteString(ServerTimeTZ);
            _worldPacket.WriteString(GameTimeTZ);
            _worldPacket.WriteString(ServerRegionalTZ);
        }

        public string ServerTimeTZ;
        public string GameTimeTZ;
        public string ServerRegionalTZ;
    }

    public class MOTD : ServerPacket
    {
        public MOTD() : base(ServerOpcodes.Motd) { }

        public override void Write()
        {
            _worldPacket.WriteBits(Text.Count, 4);
            _worldPacket.FlushBits();

            foreach (var line in Text)
            {
                _worldPacket.WriteBits(line.GetByteCount(), 7);
                _worldPacket.FlushBits();
                _worldPacket.WriteString(line);
            }
        }

        public List<string> Text;
    }

    //Structs
    public struct SavedThrottleObjectState
    {
        public void Write(WorldPacket data)
        {
            data.WriteUInt32(MaxTries);
            data.WriteUInt32(PerMilliseconds);
            data.WriteUInt32(TryCount);
            data.WriteUInt32(LastResetTimeBeforeNow);
        }
        
        public uint MaxTries;
        public uint PerMilliseconds;
        public uint TryCount;
        public uint LastResetTimeBeforeNow;
    }

    public struct EuropaTicketConfig
    {
        public void Write(WorldPacket data)
        {
            data.WriteBit(TicketsEnabled);
            data.WriteBit(BugsEnabled);
            data.WriteBit(ComplaintsEnabled);
            data.WriteBit(SuggestionsEnabled);
            data.FlushBits();

            ThrottleState.Write(data);
            ExpensiveThrottleState.Write(data);
        }
        
        public bool TicketsEnabled;
        public bool BugsEnabled;
        public bool ComplaintsEnabled;
        public bool SuggestionsEnabled;

        public SavedThrottleObjectState ThrottleState;
        public SavedThrottleObjectState ExpensiveThrottleState;
    }

    public struct ChatThrottleParams
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(MaxTries);
            data.WriteInt32(TriesRestoredPerSecond);
            data.WriteInt32(UsedTriesPerMessage);
        }

        public int MaxTries;
        public int TriesRestoredPerSecond;
        public int UsedTriesPerMessage;
    }

    public struct GameModeData
    {
        public void Write(WorldPacket data)
        {
            data.WriteUInt8(GameMode);
            data.WriteInt32(ContentSetID);
            data.WriteInt32(GameModeRecordID);
        }

        public byte GameMode;
        public int ContentSetID;
        public int GameModeRecordID;
    }

    public struct GameRuleValuePair
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(Rule);
            data.WriteInt32(Value);
            data.WriteFloat(ValueF);
        }
        
        public int Rule;
        public int Value;
        public float ValueF;
    }

    public struct DebugTimeEventInfo
    {
        public void Write(WorldPacket data)
        {
            data.WriteUInt32(TimeEvent);
            data.WriteBits(Text.GetByteCount(), 7);
            data.FlushBits();

            data.WriteString(Text);
        }
        
        public uint TimeEvent;
        public string Text;
    }
}
