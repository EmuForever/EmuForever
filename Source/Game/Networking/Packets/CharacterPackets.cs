// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Collections;
using Framework.Configuration;
using Framework.Constants;
using Framework.Database;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Networking.Packets
{
    public class EnumCharacters : ClientPacket
    {
        public EnumCharacters(WorldPacket packet) : base(packet) { }

        public override void Read() { }
    }

    // Classic 1.60 C_GameRules.SetForeverExperiencePreset: the glue screen sends both messages, the first one also when the
    // character list disagrees with the preset picked in this client session. Layouts read from the 70532 client serializers.
    public class SetForeverExperiencePreset : ClientPacket
    {
        public SetForeverExperiencePreset(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Unknown = _worldPacket.ReadUInt32();
            Classic = _worldPacket.HasBit();
        }

        public uint Unknown;
        public bool Classic;
    }

    public class SelectForeverExperiencePreset : ClientPacket
    {
        public SelectForeverExperiencePreset(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Classic = _worldPacket.HasBit();
        }

        public bool Classic;
    }

    public class EnumCharactersResult : ServerPacket
    {
        public EnumCharactersResult() : base(ServerOpcodes.EnumCharactersResult) { }

        public override void Write()
        {
            // Forever layout: TC EnumCharactersResult::Write
            _worldPacket.WriteBit(Success);
            _worldPacket.WriteBit(Realmless);
            _worldPacket.WriteBit(IsDeletedCharacters);
            _worldPacket.WriteBit(IsNewPlayerRestrictionSkipped);
            _worldPacket.WriteBit(IsNewPlayerRestricted);
            _worldPacket.WriteBit(false); // IsNewcomerChatCompleted
            _worldPacket.WriteBit(IsTrialAccountRestricted);
            _worldPacket.WriteBit(false); // IsAccountLapsedPlayer
            _worldPacket.WriteBit(DisabledClassesMask.HasValue);
            _worldPacket.WriteBit(ClassicExperiencePreset); // TC ForceCharacterListSort; testing whether the client reads the preset here
            _worldPacket.WriteInt32(Characters.Count);
            _worldPacket.WriteInt32(RegionwideCharacters.Count);
            _worldPacket.WriteInt32(MaxCharacterLevel);
            _worldPacket.WriteInt32(RaceUnlockData.Count);
            _worldPacket.WriteInt32(UnlockedConditionalAppearances.Count);
            _worldPacket.WriteInt32(RaceLimitDisables.Count);
            _worldPacket.WriteInt32(0); // WarbandGroups

            if (DisabledClassesMask.HasValue)
                _worldPacket.WriteInt32(DisabledClassesMask.Value);

            foreach (CharacterInfo charInfo in Characters)
                charInfo.Write(_worldPacket);

            foreach (RegionwideCharacterListEntry charInfo in RegionwideCharacters)
                charInfo.Write(_worldPacket);

            foreach (RaceUnlock raceUnlock in RaceUnlockData)
                raceUnlock.Write(_worldPacket);

            foreach (UnlockedConditionalAppearance unlockedConditionalAppearance in UnlockedConditionalAppearances)
                unlockedConditionalAppearance.Write(_worldPacket);

            foreach (RaceLimitDisableInfo raceLimitDisableInfo in RaceLimitDisables)
                raceLimitDisableInfo.Write(_worldPacket);
        }

        public bool Success;
        public bool Realmless;
        public bool IsDeletedCharacters; // used for character undelete list
        public bool IsNewPlayerRestrictionSkipped; // allows client to skip new player restrictions
        public bool IsNewPlayerRestricted; // forbids using level boost and class trials
        public bool IsNewPlayer; // forbids hero classes and allied races
        public bool IsTrialAccountRestricted;
        public bool ClassicExperiencePreset;

        public int MaxCharacterLevel = 1;
        public int? DisabledClassesMask = new();

        public List<CharacterInfo> Characters = new(); // all characters on the list
        public List<RegionwideCharacterListEntry> RegionwideCharacters = new();
        public List<RaceUnlock> RaceUnlockData = new(); //
        public List<UnlockedConditionalAppearance> UnlockedConditionalAppearances = new();
        public List<RaceLimitDisableInfo> RaceLimitDisables = new();

        public class CharacterInfo
        {
            public CharacterInfo(SQLFields fields)
            {
                Guid = ObjectGuid.Create(HighGuid.Player, fields.Read<long>(0));
                Name = fields.Read<string>(1);
                RaceId = fields.Read<byte>(2);
                ClassId = (Class)fields.Read<byte>(3);
                SexId = fields.Read<byte>(4);
                ExperienceLevel = fields.Read<byte>(5);
                ZoneId = fields.Read<int>(6);
                MapId = fields.Read<int>(7);
                PreloadPos = new Vector3(fields.Read<float>(8), fields.Read<float>(9), fields.Read<float>(10));

                long guildId = fields.Read<long>(11);
                if (guildId != 0)
                    GuildGuid = ObjectGuid.Create(HighGuid.Guild, guildId);

                PlayerFlags playerFlags = (PlayerFlags)fields.Read<uint>(12);
                AtLoginFlags atLoginFlags = (AtLoginFlags)fields.Read<ushort>(13);

                if (playerFlags.HasFlag(PlayerFlags.Resting))
                    Flags |= CharacterFlags.Resting;

                if (atLoginFlags.HasAnyFlag(AtLoginFlags.Resurrect))
                    playerFlags &= ~PlayerFlags.Ghost;

                if (playerFlags.HasAnyFlag(PlayerFlags.Ghost))
                    Flags |= CharacterFlags.Ghost;

                if (atLoginFlags.HasAnyFlag(AtLoginFlags.Rename))
                    Flags |= CharacterFlags.Rename;

                if (fields.Read<ulong>(18) != 0)
                    Flags |= CharacterFlags.LockedByBilling;

                if (WorldConfig.Values[WorldCfg.DeclinedNamesUsed].Bool && !string.IsNullOrEmpty(fields.Read<string>(28)))
                    Flags |= CharacterFlags.Declined;

                if (atLoginFlags.HasAnyFlag(AtLoginFlags.Customize))
                    Flags2 = CharacterCustomizeFlags.Customize;
                else if (atLoginFlags.HasAnyFlag(AtLoginFlags.ChangeFaction))
                    Flags2 = CharacterCustomizeFlags.Faction;
                else if (atLoginFlags.HasAnyFlag(AtLoginFlags.ChangeRace))
                    Flags2 = CharacterCustomizeFlags.Race;

                Flags3 = 0;
                Flags4 = 0;
                FirstLogin = atLoginFlags.HasAnyFlag(AtLoginFlags.FirstLogin);

                // show pet at selection character in character list only for non-ghost character
                if (!playerFlags.HasAnyFlag(PlayerFlags.Ghost) && (ClassId == Class.Warlock || ClassId == Class.Hunter || ClassId == Class.DeathKnight))
                {
                    CreatureTemplate creatureInfo = Global.ObjectMgr.GetCreatureTemplate(fields.Read<int>(14));
                    if (creatureInfo != null)
                    {
                        PetCreatureDisplayId = fields.Read<uint>(15);
                        PetExperienceLevel = fields.Read<ushort>(16);
                        PetCreatureFamilyId = (uint)creatureInfo.Family;
                    }
                }

                BoostInProgress = false;
                ProfessionIds[0] = 0;
                ProfessionIds[1] = 0;

                ListPosition = fields.Read<byte>(19);
                LastPlayedTime = fields.Read<long>(20);

                var spec = Global.DB2Mgr.GetChrSpecializationByIndex(ClassId, fields.Read<byte>(21));
                if (spec != null)
                    SpecID = (short)spec.Id;

                LastLoginVersion = fields.Read<int>(22);
                PersonalTabard = new()
                {
                    EmblemStyle = fields.Read<int>(23),
                    EmblemColor = fields.Read<int>(24),
                    BorderStyle = fields.Read<int>(25),
                    BorderColor = fields.Read<int>(26),
                    BackgroundColor = fields.Read<int>(27)
                };

            }

            public void Write(WorldPacket data)
            {
                WriteBasic(data);
                WriteRestrictionsAndMails(data);
            }

            public void WriteBasic(WorldPacket data)
            {
                // CharacterInfoBasic (1.60.1)
                data.WritePackedGuid(Guid);
                data.WriteUInt32(Global.WorldMgr.GetVirtualRealmAddress());
                data.WriteUInt16(ListPosition);
                data.WriteUInt8(RaceId);
                data.WriteUInt8(SexId);
                data.WriteUInt8((byte)ClassId);
                data.WriteInt16(SpecID);
                data.WriteInt32(Customizations.Count);
                data.WriteUInt8(ExperienceLevel);
                data.WriteInt32(MapId);
                data.WriteInt32(ZoneId);
                data.WriteVector3(PreloadPos);
                data.WriteUInt64(GuildClubMemberID);
                data.WritePackedGuid(GuildGuid);
                data.WriteUInt32((uint)Flags);
                data.WriteUInt32((uint)Flags2);
                data.WriteUInt32(Flags3);
                data.WriteUInt32(Flags4);
                data.WriteUInt8(0); // CantLoginReason

                data.WriteUInt32(PetCreatureDisplayId);
                data.WriteUInt32(PetExperienceLevel);
                data.WriteUInt32(PetCreatureFamilyId);

                for (int i = 0; i < 19; ++i)
                    (i < VisualItems.Count ? VisualItems[i] : default).Write(data);

                data.WriteInt32(49); // SaveVersion: the Classic character-service UI needs 49 (ForeverWorld)
                data.WriteInt64(0); // CreateTime
                data.WriteInt64(LastPlayedTime);
                data.WriteInt32(LastLoginVersion);
                data.WriteInt32(PersonalTabard.EmblemStyle);
                data.WriteInt32(PersonalTabard.EmblemColor);
                data.WriteInt32(PersonalTabard.BorderStyle);
                data.WriteInt32(PersonalTabard.BorderColor);
                data.WriteInt32(PersonalTabard.BackgroundColor);

                data.WriteUInt32(ProfessionIds[0]);
                data.WriteUInt32(ProfessionIds[1]);

                data.WriteInt32(0); // TimerunningSeasonID
                data.WriteUInt32(OverrideSelectScreenFileDataID);
                data.WriteUInt32(0); // RealmQueue
                data.WriteInt32(ConfigMgr.GetDefaultValue("SuperDistrictID", 2)); // must match the realm's SuperDistrictID (BattleNot.conf)

                foreach (ChrCustomizationChoice customization in Customizations)
                    customization.Write(data);

                string surname = Global.CharacterCacheStorage.GetCharacterSurnameByGuid(Guid);
                data.WriteBits(Name.GetByteCount(), 6);
                data.WriteBits(surname.GetByteCount(), 6);
                data.WriteBit(FirstLogin);
                data.WriteBit(true); // RealmInfoFound
                data.WriteBit(false); // IsRealmOffline
                data.FlushBits();
                data.WriteString(Name);
                data.WriteString(surname);
            }

            public void WriteRestrictionsAndMails(WorldPacket data)
            {
                // CharacterRestrictionAndMailData
                data.WriteBit(BoostInProgress);
                data.WriteBit(RpeResetAvailable);
                data.FlushBits();
                data.WriteUInt32(0); // RestrictionFlags
                data.WriteInt32(MailSenders.Count);
                data.WriteInt32(MailSenderTypes.Count);
                data.WriteUInt32(0); // NoRpeReason

                foreach (var mailSenderType in MailSenderTypes)
                    data.WriteUInt32(mailSenderType); // TC appends std::vector<uint32> raw (4 bytes each)

                foreach (string str in MailSenders)
                    data.WriteBits(str.GetByteCount() + 1, 6);

                data.FlushBits();

                foreach (string str in MailSenders)
                    data.WriteCString(str);
            }

            public ObjectGuid Guid;
            public ulong GuildClubMemberID; // same as bgs.protocol.club.v1.MemberId.unique_id, guessed basing on SMSG_QUERY_PLAYER_NAME_RESPONSE (that one is known)
            public string Name;
            public byte ListPosition; // Order of the characters in list
            public byte RaceId;
            public Class ClassId;
            public byte SexId;
            public List<ChrCustomizationChoice> Customizations;
            public byte ExperienceLevel;
            public int ZoneId;
            public int MapId;
            public Vector3 PreloadPos;
            public ObjectGuid GuildGuid;
            public CharacterFlags Flags; // Character flag @see enum CharacterFlags
            public CharacterCustomizeFlags Flags2; // Character customization flags @see enum CharacterCustomizeFlags
            public uint Flags3; // Character flags 3 @todo research
            public uint Flags4;
            public bool FirstLogin;
            public byte unkWod61x;
            public long LastPlayedTime;
            public short SpecID;
            public int Unknown703;
            public int LastLoginVersion;
            public uint OverrideSelectScreenFileDataID;
            public uint PetCreatureDisplayId;
            public uint PetExperienceLevel;
            public uint PetCreatureFamilyId;
            public bool BoostInProgress; // @todo
            public uint[] ProfessionIds = new uint[2];      // @todo
            public Array<VisualItemInfo> VisualItems = new(34); // It was : (InventorySlots.ReagentBagEnd);
            public List<string> MailSenders = new();
            public List<uint> MailSenderTypes = new();
            public bool RpeResetAvailable;
            public bool RpeResetQuestClearAvailable;
            public CustomTabardInfo PersonalTabard;

            public struct VisualItemInfo
            {
                public void Write(WorldPacket data)
                {
                    data.WriteUInt32(ItemId);
                    data.WriteUInt32(TransmogrifiedItemId);
                    data.WriteUInt8(Subclass);
                    data.WriteUInt8(InvType);
                    data.WriteUInt32(DisplayId);
                    data.WriteUInt32(DisplayEnchantId);
                    data.WriteInt32(SecondaryItemModifiedAppearanceID);
                    data.WriteUInt8(0); // SheatheCategory
                }

                public uint ItemId;
                public uint TransmogrifiedItemId;
                public uint DisplayId;
                public uint DisplayEnchantId;
                public int SecondaryItemModifiedAppearanceID; // also -1 is some special value
                public byte InvType;
                public byte Subclass;
            }

            public struct PetInfo
            {
                public uint CreatureDisplayId; // PetCreatureDisplayID
                public uint Level; // PetExperienceLevel
                public uint CreatureFamily; // PetCreatureFamilyID
            }
        }

        // Classic 1.60: the official server sends every character as a regionwide entry (Realmless, no realm entries; TC sniff
        // 70235). Only this entry has money; the character select tooltip shows it with the two primary professions.
        public class RegionwideCharacterListEntry
        {
            public RegionwideCharacterListEntry(CharacterInfo basic)
            {
                Basic = basic;
            }

            public void Write(WorldPacket data)
            {
                Basic.WriteBasic(data);
                data.WriteUInt64(Money);
                data.WriteFloat(AvgEquippedItemLevel);
                data.WriteFloat(CurrentSeasonMythicPlusOverallScore);
                data.WriteInt32(CurrentSeasonBestPvpRating);
                data.WriteInt8(PvpRatingBracket);
                data.WriteInt16(PvpRatingAssociatedSpecID);
            }

            public CharacterInfo Basic;
            public ulong Money;
            public float AvgEquippedItemLevel;
            public float CurrentSeasonMythicPlusOverallScore;
            public int CurrentSeasonBestPvpRating;
            public sbyte PvpRatingBracket = -1;
            public short PvpRatingAssociatedSpecID;
        }

        public struct RaceUnlock
        {
            public void Write(WorldPacket data)
            {
                data.WriteInt8((sbyte)RaceID);
                data.WriteInt32(ClassUnlocks.Count);
                foreach (ClassUnlock classUnlock in ClassUnlocks)
                    classUnlock.Write(data);
                data.WriteBit(HasExpansion); // HasUnlockedLicense
                data.WriteBit(HasAchievement);
                data.WriteBit(HasHeritageArmor);
                data.WriteBit(true); // HasEntitlement (ForeverWorld sends true)
                data.WriteBit(false); // HideRaceOnClient
                data.WriteBit(false); // FactionBalanceDisabled
                data.WriteBit(DoesNotHaveAvailableClasses);
                data.FlushBits();
            }

            public Race RaceID;
            public bool HasExpansion;
            public bool HasAchievement;
            public bool HasHeritageArmor;
            public bool IsLocked;
            public bool DoesNotHaveAvailableClasses;
            public List<ClassUnlock> ClassUnlocks;
        }

        public struct ClassUnlock
        {
            public void Write(WorldPacket data)
            {
                data.WriteInt8((sbyte)ClassID);
                data.WriteUInt32(AchievementID);
                data.WriteBit(HasExpansion);
                data.WriteBit(HasUnlockedAchievement);
                data.WriteBit(HasEntitlement);
                data.FlushBits();
            }

            public Class ClassID;
            public uint AchievementID;
            public bool HasExpansion;
            public bool HasUnlockedAchievement;
            public bool HasEntitlement;
        }

        public struct UnlockedConditionalAppearance
        {
            public void Write(WorldPacket data)
            {
                data.WriteInt32(AchievementID);
                data.WriteInt32(Unused);
            }

            public int AchievementID;
            public int Unused;
        }

        public struct RaceLimitDisableInfo
        {
            public int RaceID;
            public int BlockReason;

            public void Write(WorldPacket data)
            {
                data.WriteInt8((sbyte)RaceID);
                data.WriteInt8((sbyte)BlockReason);
            }
        }
    }

    public class RecentAllyDataResponse : ServerPacket
    {
        public RecentAllyDataResponse() : base(ServerOpcodes.RecentAllyDataResponse) { }

        public override void Write()
        {
            // Our map puts this on wire 0x470365, which TC (working with 70532) numbers SMSG_RECENT_ALLY_LIST, the Allies tab list.
            // Keep it all zero: 0x08ECBD10 here was read as a list size and the client died at character select (2026-10-25).
            // TC's "release" values go to AccountItemCollectionData (wire 0x470363 = TC's RECENT_ALLY_DATA_RESPONSE).
            _worldPacket.WriteUInt32(0);
            _worldPacket.WriteUInt8(0);
            _worldPacket.WriteUInt32(0);
        }
    }

    // Classic 1.60: per character restrictions, sent right after the enum result as on the official server (TC, sniff 70205).
    public class RegionwideCharacterRestrictionsData : ServerPacket
    {
        public RegionwideCharacterRestrictionsData() : base(ServerOpcodes.RegionwideCharacterRestrictionsData) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Characters.Count);
            foreach (ObjectGuid guid in Characters)
            {
                _worldPacket.WriteBit(false);
                _worldPacket.WriteBit(false);
                _worldPacket.FlushBits();
                _worldPacket.WritePackedGuid(guid);
                _worldPacket.WriteUInt32(0);
                _worldPacket.WriteUInt32(10);
            }
        }

        public List<ObjectGuid> Characters = new();
    }

    // Classic 1.60: unread mail of each character; the character list shows a mail icon with the senders (TC, sniff 70205).
    // Per character: 2 bits (own byte), packed guid, sender count, sender type count, types, 6-bit name lengths (with the
    // terminating zero), names. With the 2 bits after the entry the client read past the end and crashed (TC 2026-10-03).
    public class RegionwideCharacterMailData : ServerPacket
    {
        public RegionwideCharacterMailData() : base(ServerOpcodes.RegionwideCharacterMailData) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Characters.Count);
            foreach (var (guid, senders) in Characters)
            {
                _worldPacket.WriteBit(false);
                _worldPacket.WriteBit(false);
                _worldPacket.FlushBits();
                _worldPacket.WritePackedGuid(guid);
                _worldPacket.WriteInt32(senders.Count);
                _worldPacket.WriteInt32(senders.Count);
                foreach (var (type, _) in senders)
                    _worldPacket.WriteUInt32(type);
                foreach (var (_, name) in senders)
                    _worldPacket.WriteBits(name.GetByteCount() + 1, 6);
                _worldPacket.FlushBits();
                foreach (var (_, name) in senders)
                    _worldPacket.WriteCString(name);
            }
        }

        public List<(ObjectGuid Guid, List<(uint Type, string Name)> Senders)> Characters = new();
    }

    public class AccountItemCollectionData : ServerPacket
    {
        public AccountItemCollectionData() : base(ServerOpcodes.AccountItemCollectionData) { }

        public override void Write()
        {
            // Wire 0x470363 is what TC (working with 70532) sends as SMSG_RECENT_ALLY_DATA_RESPONSE: 0x08ECBD10, 7, 0 as in the
            // official sniffs (70235). Character select waits for item collection list type 7 (Connection.log), and with 0, 0, 0 TC
            // saw the Social window keep its old layout (no Allies tabs).
            _worldPacket.WriteUInt32(0x08ECBD10);
            _worldPacket.WriteUInt8(7);     // collectionListType
            _worldPacket.WriteUInt32(0);    // item count
        }
    }

    class CheckCharacterNameAvailability : ClientPacket
    {
        public CheckCharacterNameAvailability(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            SequenceIndex = _worldPacket.ReadUInt32();
            int nameLength = _worldPacket.ReadBits<int>(6);
            _worldPacket.ReadBits<int>(3); // Unknown Classic 1.60.1 name-check bits
            int surnameLength = _worldPacket.ReadBits<int>(6);
            _worldPacket.ResetBitPos();
            Name = _worldPacket.ReadString(nameLength);
            Surname = _worldPacket.ReadString(surnameLength);
        }

        public uint SequenceIndex;
        public string Name;
        public string Surname;
    }

    class CheckCharacterNameAvailabilityResult : ServerPacket
    {
        public CheckCharacterNameAvailabilityResult(uint sequenceIndex, ResponseCodes result) : base(ServerOpcodes.CheckCharacterNameAvailabilityResult)
        {
            SequenceIndex = sequenceIndex;
            Result = result;
        }

        public override void Write()
        {
            _worldPacket.WriteUInt32(SequenceIndex);
            _worldPacket.WriteUInt32((uint)Result);
        }

        public uint SequenceIndex;
        public ResponseCodes Result;
    }

    public class CreateCharacter : ClientPacket
    {
        public CreateCharacter(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            CreateInfo = new CharacterCreateInfo();
            int nameLength = _worldPacket.ReadBits<int>(6);
            bool hasTemplateSet = _worldPacket.HasBit();
            CreateInfo.IsTrialBoost = _worldPacket.HasBit();
            CreateInfo.UseNPE = _worldPacket.HasBit();
            CreateInfo.HardcoreSelfFound = _worldPacket.HasBit();
            _worldPacket.ReadBits<int>(2);
            int surnameLength = _worldPacket.ReadBits<int>(6);
            _worldPacket.ResetBitPos();

            CreateInfo.RaceId = (Race)_worldPacket.ReadUInt8();
            CreateInfo.ClassId = (Class)_worldPacket.ReadUInt8();
            CreateInfo.Sex = (Gender)_worldPacket.ReadInt8();
            var customizationCount = _worldPacket.ReadUInt32();
            _worldPacket.ReadInt32(); // Unused Classic character-creation field
            CreateInfo.TimerunningSeasonID = _worldPacket.ReadInt32();

            CreateInfo.Name = _worldPacket.ReadString(nameLength);
            CreateInfo.Surname = _worldPacket.ReadString(surnameLength);
            if (hasTemplateSet)
                CreateInfo.TemplateSet = _worldPacket.ReadInt32();

            for (var i = 0; i < customizationCount; ++i)
                CreateInfo.Customizations[i] = new(_worldPacket);

            CreateInfo.Customizations.Sort();
        }

        public CharacterCreateInfo CreateInfo;
    }

    public class CreateChar : ServerPacket
    {
        public CreateChar() : base(ServerOpcodes.CreateChar) { }

        public override void Write()
        {
            _worldPacket.WriteUInt32((uint)Code);   // uint32 in Forever/TC
            _worldPacket.WritePackedGuid(Guid);
        }

        public ResponseCodes Code;
        public ObjectGuid Guid;
    }

    public class CharDelete : ClientPacket
    {
        public CharDelete(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Guid = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid Guid; // Guid of the character to delete
    }

    public class DeleteChar : ServerPacket
    {
        public DeleteChar() : base(ServerOpcodes.DeleteChar) { }

        public override void Write()
        {
            _worldPacket.WriteUInt32((uint)Code);   // uint32 in Forever/TC
        }

        public ResponseCodes Code;
    }

    public class CharacterRenameRequest : ClientPacket
    {
        public CharacterRenameRequest(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            RenameInfo = new CharacterRenameInfo();
            RenameInfo.Guid = _worldPacket.ReadPackedGuid();
            RenameInfo.NewName = _worldPacket.ReadString(_worldPacket.ReadBits<int>(6));
        }

        public CharacterRenameInfo RenameInfo;
    }

    public class CharacterRenameResult : ServerPacket
    {
        public CharacterRenameResult() : base(ServerOpcodes.CharacterRenameResult) { }

        public override void Write()
        {
            // Forever layout: TC CharacterRenameResult::Write
            _worldPacket.WriteUInt32((uint)Result);
            _worldPacket.WriteBit(Guid.HasValue);
            _worldPacket.WriteBits(Name.GetByteCount(), 6);
            _worldPacket.FlushBits();

            if (Guid.HasValue)
                _worldPacket.WritePackedGuid(Guid.Value);

            _worldPacket.WriteString(Name);
        }

        public string Name;
        public ResponseCodes Result;
        public ObjectGuid? Guid;
    }

    public class CharCustomize : ClientPacket
    {
        public CharCustomize(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            CustomizeInfo = new CharCustomizeInfo();
            CustomizeInfo.CharGUID = _worldPacket.ReadPackedGuid();
            CustomizeInfo.SexID = (Gender)_worldPacket.ReadInt8();
            var customizationCount = _worldPacket.ReadUInt32();

            for (var i = 0; i < customizationCount; ++i)
                CustomizeInfo.Customizations[i] = new(_worldPacket);

            CustomizeInfo.Customizations.Sort();

            CustomizeInfo.CharName = _worldPacket.ReadString(_worldPacket.ReadBits<int>(6));
        }

        public CharCustomizeInfo CustomizeInfo;
    }

    // @todo: CharCustomizeResult

    public class CharRaceOrFactionChange : ClientPacket
    {
        public CharRaceOrFactionChange(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            RaceOrFactionChangeInfo = new CharRaceOrFactionChangeInfo();

            RaceOrFactionChangeInfo.FactionChange = _worldPacket.HasBit();

            int nameLength = _worldPacket.ReadBits<int>(6);

            RaceOrFactionChangeInfo.Guid = _worldPacket.ReadPackedGuid();
            RaceOrFactionChangeInfo.SexID = (Gender)_worldPacket.ReadUInt8();
            RaceOrFactionChangeInfo.RaceID = (Race)_worldPacket.ReadUInt8();
            RaceOrFactionChangeInfo.InitialRaceID = (Race)_worldPacket.ReadUInt8();
            var customizationCount = _worldPacket.ReadUInt32();
            RaceOrFactionChangeInfo.Name = _worldPacket.ReadString(nameLength);

            for (var i = 0; i < customizationCount; ++i)
                RaceOrFactionChangeInfo.Customizations[i] = new(_worldPacket);

            RaceOrFactionChangeInfo.Customizations.Sort();
        }

        public CharRaceOrFactionChangeInfo RaceOrFactionChangeInfo;
    }

    public class CharFactionChangeResult : ServerPacket
    {
        public CharFactionChangeResult() : base(ServerOpcodes.CharFactionChangeResult) { }

        public override void Write()
        {
            // Forever layout: TC CharFactionChangeResult::Write
            _worldPacket.WriteUInt32((uint)Result);
            _worldPacket.WritePackedGuid(Guid);
            _worldPacket.WriteBit(Display != null);
            _worldPacket.FlushBits();

            if (Display != null)
            {
                _worldPacket.WriteBits(Display.Name.GetByteCount(), 6);
                _worldPacket.WriteInt8((sbyte)Display.SexID);
                _worldPacket.WriteUInt8((byte)Display.RaceID);
                _worldPacket.WriteInt32(Display.Customizations.Count);
                _worldPacket.WriteString(Display.Name);

                foreach (ChrCustomizationChoice customization in Display.Customizations)
                    customization.Write(_worldPacket);
            }
        }

        public ResponseCodes Result = 0;
        public ObjectGuid Guid;
        public CharFactionChangeDisplayInfo Display;

        public class CharFactionChangeDisplayInfo
        {
            public string Name;
            public Gender SexID = Gender.Male;
            public Race RaceID = Race.None;
            public Array<ChrCustomizationChoice> Customizations = new(250);
        }
    }

    public class GenerateRandomCharacterName : ClientPacket
    {
        public GenerateRandomCharacterName(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Race = (Race)_worldPacket.ReadUInt8();
            Sex = (Gender)_worldPacket.ReadInt8();
        }

        public Race Race;
        public Gender Sex;
    }

    public class GenerateRandomCharacterNameResult : ServerPacket
    {
        public GenerateRandomCharacterNameResult() : base(ServerOpcodes.GenerateRandomCharacterNameResult) { }

        public override void Write()
        {
            // Classic 70450 consumes a second six-bit length and surname after the first name.
            _worldPacket.WriteBit(Success);
            _worldPacket.WriteBits(Name.GetByteCount(), 6);
            _worldPacket.WriteBits(Surname.GetByteCount(), 9);
            _worldPacket.FlushBits();

            _worldPacket.WriteString(Name);
            _worldPacket.WriteString(Surname);
            
        }

        public string Name;
        public string Surname = string.Empty;
        public bool Success;
    }

    public class ReorderCharacters : ClientPacket
    {
        public ReorderCharacters(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            uint count = _worldPacket.ReadBits<uint>(9);
            for (var i = 0; i < count; ++i)
                Entries[i] = new(_worldPacket);
        }

        public Array<ReorderInfo> Entries = new(200);

        public class ReorderInfo
        {
            public ReorderInfo(WorldPacket data)
            {
                PlayerGUID = data.ReadPackedGuid();
                NewPosition = data.ReadUInt8();
            }

            public ObjectGuid PlayerGUID;
            public byte NewPosition;
        }
    }

    public class UndeleteCharacter : ClientPacket
    {
        public UndeleteCharacter(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            UndeleteInfo = new CharacterUndeleteInfo();
            // Forever layout: TC UndeleteCharacter::Read
            UndeleteInfo.ClientToken = _worldPacket.ReadInt32();
            UndeleteInfo.CharacterGuid = _worldPacket.ReadPackedGuid();
        }

        public CharacterUndeleteInfo UndeleteInfo;
    }

    public class UndeleteCharacterResponse : ServerPacket
    {
        public UndeleteCharacterResponse() : base(ServerOpcodes.UndeleteCharacterResponse) { }

        public override void Write()
        {
            Cypher.Assert(UndeleteInfo != null);
            _worldPacket.WriteInt32(UndeleteInfo.ClientToken);
            _worldPacket.WriteUInt32((uint)Result);
            _worldPacket.WritePackedGuid(UndeleteInfo.CharacterGuid);
        }

        public CharacterUndeleteInfo UndeleteInfo;
        public CharacterUndeleteResult Result;
    }

    public class GetUndeleteCharacterCooldownStatus : ClientPacket
    {
        public GetUndeleteCharacterCooldownStatus(WorldPacket packet) : base(packet) { }

        public override void Read() { }
    }

    public class UndeleteCooldownStatusResponse : ServerPacket
    {
        public UndeleteCooldownStatusResponse() : base(ServerOpcodes.UndeleteCooldownStatusResponse) { }

        public override void Write()
        {
            // Forever layout: TC UndeleteCooldownStatusResponse::Write
            _worldPacket.WriteUInt32((uint)(int)MaxCooldown);
            _worldPacket.WriteUInt32((uint)(int)CurrentCooldown);
            _worldPacket.WriteBit(OnCooldown);
            _worldPacket.FlushBits();
        }

        public bool OnCooldown; //
        public Seconds MaxCooldown; // Max. cooldown until next free character restoration. Displayed in undelete confirm message. (in sec)
        public Seconds CurrentCooldown; // Current cooldown until next free character restoration. (in sec)
    }

    public class PlayerLogin : ClientPacket
    {
        public PlayerLogin(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            // Forever layout: TC PlayerLogin::Read
            Guid = _worldPacket.ReadPackedGuid();
            FarClip = _worldPacket.ReadFloat();
            RPE = _worldPacket.HasBit();
        }

        public ObjectGuid Guid;      // Guid of the player that is logging in
        public float FarClip; // Visibility distance (for terrain)
        public bool RPE;
    }

    public class LoginVerifyWorld : ServerPacket
    {
        public LoginVerifyWorld() : base(ServerOpcodes.LoginVerifyWorld, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(MapID);
            _worldPacket.WriteVector4(Pos.GetPosition4D());
            _worldPacket.WriteUInt32(Reason);
        }

        public int MapID = -1;
        public Position Pos;
        public uint Reason = 0;
    }

    public class CharacterLoginFailed : ServerPacket
    {
        public CharacterLoginFailed(LoginFailureReason code) : base(ServerOpcodes.CharacterLoginFailed)
        {
            Code = code;
        }

        public override void Write()
        {
            _worldPacket.WriteUInt8((byte)Code);
        }

        LoginFailureReason Code;
    }

    public class LogoutRequest : ClientPacket
    {
        public LogoutRequest(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            // Forever layout: TC LogoutRequest::Read (Classic 1.60.1 sends an empty payload)
            if (_worldPacket.GetSize() > _worldPacket.Position)
                IdleLogout = _worldPacket.HasBit();
        }

        public bool IdleLogout;
    }

    public class LogoutResponse : ServerPacket
    {
        public LogoutResponse() : base(ServerOpcodes.LogoutResponse, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(LogoutResult);
            _worldPacket.WriteBit(Instant);
            _worldPacket.FlushBits();
        }

        public int LogoutResult;
        public bool Instant = false;
    }

    public class LogoutComplete : ServerPacket
    {
        public LogoutComplete() : base(ServerOpcodes.LogoutComplete) { }

        public override void Write()
        {
            // Forever layout: TC LogoutComplete::Write
            _worldPacket.WriteBit(SwitchGameMode != null);
            _worldPacket.FlushBits();

            if (SwitchGameMode != null)
                SwitchGameMode.Write(_worldPacket);
        }

        public SwitchGameModeData SwitchGameMode;
    }

    public class LogoutGameModeData
    {
        public int Unknown_1107_0;
        public ObjectGuid Guid;
        public byte GameMode;
        public int MapID;
        public byte Unknown_1107_1;
        public byte Unknown_1107_2;
        public byte Unknown_1107_3;
        public List<ChrCustomizationChoice> Customizations = new();
        public List<ChrCustomizationChoice> Unknown_1107_4 = new();

        public void Write(WorldPacket data)
        {
            data.WriteInt32(Unknown_1107_0);
            data.WritePackedGuid(Guid);
            data.WriteUInt8(GameMode);
            data.WriteInt32(MapID);
            data.WriteUInt8(Unknown_1107_1);
            data.WriteUInt8(Unknown_1107_2);
            data.WriteUInt8(Unknown_1107_3);
            data.WriteInt32(Customizations.Count);
            data.WriteInt32(Unknown_1107_4.Count);

            foreach (ChrCustomizationChoice customization in Customizations)
                customization.Write(data);

            foreach (ChrCustomizationChoice customization in Unknown_1107_4)
                customization.Write(data);
        }
    }

    public class SwitchGameModeData
    {
        public bool IsFastLogin;
        public LogoutGameModeData Current = new();
        public LogoutGameModeData New = new();

        public void Write(WorldPacket data)
        {
            // TC writes the bit without flushing; GameModeData starts with an int32 so the bit is flushed implicitly
            data.WriteBit(IsFastLogin);
            Current.Write(data);
            New.Write(data);
        }
    }

    public class LogoutCancel : ClientPacket
    {
        public LogoutCancel(WorldPacket packet) : base(packet) { }

        public override void Read() { }
    }

    public class LogoutCancelAck : ServerPacket
    {
        public LogoutCancelAck() : base(ServerOpcodes.LogoutCancelAck, ConnectionType.Instance) { }

        public override void Write() { }
    }

    public class LoadingScreenNotify : ClientPacket
    {
        public LoadingScreenNotify(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            MapID = _worldPacket.ReadInt32();
            Showing = _worldPacket.HasBit();
        }

        public int MapID = -1;
        public bool Showing;
    }

    public class InitialSetup : ServerPacket
    {
        public InitialSetup() : base(ServerOpcodes.InitialSetup, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteUInt8((byte)ServerExpansionLevel);
            _worldPacket.WriteUInt8(ServerExpansionTier);
        }

        public byte ServerExpansionTier;
        public Expansion ServerExpansionLevel;
    }

    public class SetActionBarToggles : ClientPacket
    {
        public SetActionBarToggles(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Mask = _worldPacket.ReadUInt8();
        }

        public byte Mask;
    }

    public class RequestPlayedTime : ClientPacket
    {
        public RequestPlayedTime(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            TriggerScriptEvent = _worldPacket.HasBit();
        }

        public bool TriggerScriptEvent;
    }

    public class PlayedTime : ServerPacket
    {
        public PlayedTime() : base(ServerOpcodes.PlayedTime, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(TotalTime);
            _worldPacket.WriteInt32(LevelTime);
            _worldPacket.WriteBit(TriggerEvent);
            _worldPacket.FlushBits();
        }

        public Seconds TotalTime;
        public Seconds LevelTime;
        public bool TriggerEvent;
    }

    public class SetTitle : ClientPacket
    {
        public SetTitle(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            TitleID = _worldPacket.ReadInt32();
        }

        public int TitleID;
    }

    public class AlterApperance : ClientPacket
    {
        public AlterApperance(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            // Forever layout: TC AlterApperance::Read
            var customizationCount = _worldPacket.ReadUInt32();
            NewSex = _worldPacket.ReadUInt8();
            CustomizedRace = _worldPacket.ReadInt8();
            CustomizedChrModelID = _worldPacket.ReadInt32();
            UnalteredVisualRaceID = _worldPacket.ReadInt8();

            for (var i = 0; i < customizationCount; ++i)
                Customizations[i] = new(_worldPacket);

            Customizations.Sort();
        }

        public byte NewSex;
        public Array<ChrCustomizationChoice> Customizations = new(250);
        public int CustomizedRace;
        public int CustomizedChrModelID;
        public sbyte UnalteredVisualRaceID;
    }

    public class BarberShopResult : ServerPacket
    {
        public BarberShopResult(ResultEnum result) : base(ServerOpcodes.BarberShopResult)
        {
            Result = result;
        }

        public override void Write()
        {
            _worldPacket.WriteInt32((int)Result);
        }

        public ResultEnum Result;

        public enum ResultEnum
        {
            Success = 0,
            NoMoney = 1,
            NotOnChair = 2,
            NoMoney2 = 3
        }
    }

    class LogXPGain : ServerPacket
    {
        public LogXPGain() : base(ServerOpcodes.LogXpGain) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(Victim);
            _worldPacket.WriteInt32(Original);
            _worldPacket.WriteUInt8((byte)Reason);
            _worldPacket.WriteInt32(Amount);
            _worldPacket.WriteFloat(GroupBonus);
        }

        public ObjectGuid Victim;
        public int Original;
        public PlayerLogXPReason Reason;
        public int Amount;
        public float GroupBonus;
    }

    class TitleEarned : ServerPacket
    {
        public TitleEarned(ServerOpcodes opcode) : base(opcode) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Index);
        }

        public int Index;
    }

    class SetFactionAtWar : ClientPacket
    {
        public SetFactionAtWar(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            FactionIndex = _worldPacket.ReadUInt16();
        }

        public ushort FactionIndex;
    }

    class SetFactionNotAtWar : ClientPacket
    {
        public SetFactionNotAtWar(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            FactionIndex = _worldPacket.ReadUInt16();
        }

        public ushort FactionIndex;
    }

    class SetFactionInactive : ClientPacket
    {
        public SetFactionInactive(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Index = _worldPacket.ReadInt32();
            State = _worldPacket.HasBit();
        }

        public int Index;
        public bool State;
    }

    class SetWatchedFaction : ClientPacket
    {
        public SetWatchedFaction(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            FactionIndex = _worldPacket.ReadInt32();
        }

        public int FactionIndex;
    }

    class SetFactionVisible : ServerPacket
    {
        public SetFactionVisible(bool visible) : base(visible ? ServerOpcodes.SetFactionVisible : ServerOpcodes.SetFactionNotVisible, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(FactionIndex);
        }

        public int FactionIndex;
    }

    class CharCustomizeSuccess : ServerPacket
    {
        public CharCustomizeSuccess(CharCustomizeInfo customizeInfo) : base(ServerOpcodes.CharCustomizeSuccess)
        {
            CharGUID = customizeInfo.CharGUID;
            SexID = customizeInfo.SexID;
            CharName = customizeInfo.CharName;
            Customizations = customizeInfo.Customizations;
        }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(CharGUID);
            _worldPacket.WriteInt8((sbyte)SexID);
            _worldPacket.WriteInt32(Customizations.Count);
            foreach (ChrCustomizationChoice customization in Customizations)
                customization.Write(_worldPacket);

            _worldPacket.WriteBits(CharName.GetByteCount(), 6);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(CharName);
        }

        public ObjectGuid CharGUID;
        public string CharName = string.Empty;
        public Gender SexID;
        public Array<ChrCustomizationChoice> Customizations = new(250);
    }

    class CharCustomizeFailure : ServerPacket
    {
        public CharCustomizeFailure() : base(ServerOpcodes.CharCustomizeFailure) { }

        public override void Write()
        {
            // Forever layout: TC CharCustomizeFailure::Write
            _worldPacket.WriteUInt32(Result);
            _worldPacket.WritePackedGuid(CharGUID);
        }

        public uint Result;
        public ObjectGuid CharGUID;
    }

    class SetPlayerDeclinedNames : ClientPacket
    {
        public SetPlayerDeclinedNames(WorldPacket packet) : base(packet)
        {
            DeclinedNames = new DeclinedName();
        }

        public override void Read()
        {
            Player = _worldPacket.ReadPackedGuid();

            byte[] stringLengths = new byte[SharedConst.MaxDeclinedNameCases];

            for (byte i = 0; i < SharedConst.MaxDeclinedNameCases; ++i)
                stringLengths[i] = _worldPacket.ReadBits<byte>(7);

            for (byte i = 0; i < SharedConst.MaxDeclinedNameCases; ++i)
                DeclinedNames.Name[i] = _worldPacket.ReadString(stringLengths[i]);
        }

        public ObjectGuid Player;
        public DeclinedName DeclinedNames;
    }

    class SetPlayerDeclinedNamesResult : ServerPacket
    {
        public SetPlayerDeclinedNamesResult() : base(ServerOpcodes.SetPlayerDeclinedNamesResult) { }

        public override void Write()
        {
            _worldPacket.WriteInt32((int)ResultCode);
            _worldPacket.WritePackedGuid(Player);
        }

        public ObjectGuid Player;
        public DeclinedNameResult ResultCode;
    }

    class SavePersonalEmblem : ClientPacket
    {
        public SavePersonalEmblem(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Vendor = _worldPacket.ReadPackedGuid();
            PersonalTabard = new(_worldPacket);
        }

        public ObjectGuid Vendor;
        public CustomTabardInfo PersonalTabard;
    }

    class PlayerSavePersonalEmblem : ServerPacket
    {
        public PlayerSavePersonalEmblem(GuildEmblemError error) : base(ServerOpcodes.PlayerSavePersonalEmblem)
        {
            Error = error;
        }

        public override void Write()
        {
            _worldPacket.WriteInt32((int)Error);
        }

        public GuildEmblemError Error;
    }


    //Structs

    public class CustomTabardInfo
    {
        public CustomTabardInfo(WorldPacket data)
        {
            EmblemStyle = data.ReadInt32();
            EmblemColor = data.ReadInt32();
            BorderStyle = data.ReadInt32();
            BorderColor = data.ReadInt32();
            BackgroundColor = data.ReadInt32();
        }

        public CustomTabardInfo() { }

        public int EmblemStyle = -1;
        public int EmblemColor = -1;
        public int BorderStyle = -1;
        public int BorderColor = -1;
        public int BackgroundColor = -1;
    }

    public class CharacterCreateInfo
    {
        // User specified variables
        public Race RaceId = Race.None;
        public Class ClassId = Class.None;
        public Gender Sex = Gender.None;
        public Array<ChrCustomizationChoice> Customizations = new(250);
        public int? TemplateSet;
        public bool IsTrialBoost;
        public bool UseNPE;
        public bool HardcoreSelfFound;
        public int TimerunningSeasonID;
        public string Name;
        public string Surname = string.Empty;

        // Server side data
        public byte CharCount = 0;
    }

    public class CharacterRenameInfo
    {
        public string NewName;
        public ObjectGuid Guid;
    }

    public class CharCustomizeInfo
    {
        public ObjectGuid CharGUID;
        public Gender SexID = Gender.None;
        public string CharName;
        public Array<ChrCustomizationChoice> Customizations = new(250);
    }

    public class CharRaceOrFactionChangeInfo
    {
        public Race RaceID = Race.None;
        public Race InitialRaceID = Race.None;
        public Gender SexID = Gender.None;
        public ObjectGuid Guid;
        public bool FactionChange;
        public string Name;
        public Array<ChrCustomizationChoice> Customizations = new(250);
    }

    public class CharacterUndeleteInfo
    {            // User specified variables
        public ObjectGuid CharacterGuid; // Guid of the character to restore
        public int ClientToken = 0; // @todo: research

        // Server side data
        public string Name;
    }
}
