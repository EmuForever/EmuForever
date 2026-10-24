// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;

namespace Game.DataStorage
{
    public sealed class MailTemplateRecord
    {
        public int Id;
        public LocalizedString Body;
    }

    public sealed class MapRecord
    {
        public int Id;
        public string Directory;
        public LocalizedString MapName;
        /// <summary>
        /// Horde
        /// </summary>
        public LocalizedString MapDescription0;
        /// <summary>
        /// Alliance
        /// </summary>
        public LocalizedString MapDescription1;
        public LocalizedString PvpShortDescription;
        public LocalizedString PvpLongDescription;
        public float[] Corpse = new float[2];
        public byte MapType;
        private sbyte _instanceType;
        private byte _expansionID;
        public ushort AreaTableID;
        public short LoadingScreenID;
        public short TimeOfDayOverride;
        public short ParentMapID;
        public short CosmeticParentMapID;
        public byte TimeOffset;
        public float MinimapIconScale;
        /// <summary>
        /// map_id of entrance map in ghost mode (continent always and in most cases = normal entrance)
        /// </summary>
        public short CorpseMapID;
        public byte MaxPlayers;
        public short WindSettingsID;
        public int ZmpFileDataID;
        public int WdtFileDataID;
        public int OceanLiquidTypeID;
        public int NavigationMaxDistance;
        public int PreloadFileDataID;
        private int[] _flags = new int[3];

        #region Properties
        public MapTypes InstanceType => (MapTypes)_instanceType;
        public MapFlags Flags => (MapFlags)_flags[0];
        public MapFlags2 Flags2 => (MapFlags2)_flags[1];
        public Expansion Expansion => (Expansion)_expansionID;
        #endregion

        #region Helpers
        public bool HasFlag(MapFlags flag)
        {
            return _flags[0].HasFlag((int)flag);
        }

        public bool HasAnyFlag(MapFlags flag)
        {
            return _flags[0].HasAnyFlag((int)flag);
        }

        public bool HasFlag(MapFlags2 flag)
        {
            return _flags[1].HasFlag((int)flag);
        }

        public bool HasAnyFlag(MapFlags2 flag)
        {
            return _flags[1].HasAnyFlag((int)flag);
        }

        public bool IsDungeon =>
            (InstanceType == MapTypes.Instance || InstanceType == MapTypes.Raid || InstanceType == MapTypes.Scenario) && !IsGarrison;

        public bool IsNonRaidDungeon => InstanceType == MapTypes.Instance;

        public bool Instanceable =>
            InstanceType == MapTypes.Instance || InstanceType == MapTypes.Raid || InstanceType == MapTypes.Battleground ||
            InstanceType == MapTypes.Arena || InstanceType == MapTypes.Scenario;

        public bool IsRaid => InstanceType == MapTypes.Raid;
        public bool IsBattleground => InstanceType == MapTypes.Battleground;
        public bool IsBattleArena => InstanceType == MapTypes.Arena;
        public bool IsBattlegroundOrArena => InstanceType == MapTypes.Battleground || InstanceType == MapTypes.Arena;
        public bool IsScenario => InstanceType == MapTypes.Scenario;
        public bool IsWorldMap => InstanceType == MapTypes.Common;

        public bool IsContinent
        {
            get
            {
                switch (Id)
                {
                    case 0:
                    case 1:
                    case 530:
                    case 571:
                    case 870:
                    case 1116:
                    case 1220:
                    case 1642:
                    case 1643:
                    case 2222:
                    case 2444:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsDynamicDifficultyMap => HasFlag(MapFlags.DynamicDifficulty);
        public bool IsFlexLocking => HasFlag(MapFlags.FlexibleRaidLocking);
        public bool IsGarrison => HasFlag(MapFlags.Garrison);

        public bool IsSplitByFaction
        {
            get
            {
                return Id == 609 || // Acherus (DeathKnight Start)
                Id == 1265 ||   // Assault on the Dark Portal (WoD Intro)
                Id == 1481 ||   // Mardum (DH Start)
                Id == 2175 ||   // Exiles Reach - NPE
                Id == 2570;     // Forbidden Reach (Dracthyr/Evoker Start)
            }
        }
        #endregion
    }

    public sealed class MapChallengeModeRecord
    {
        public LocalizedString Name;
        public int Id;
        public ushort MapID;
        public int Flags;
        public int MaxMedals;
        public uint ExpansionLevel;
        /// <summary>
        /// maybe?
        /// </summary>
        public int RequiredWorldStateID;
        public short[] CriteriaCount = new short[5];
        public int[] FirstRewardQuestID = new int[6];
        public int[] RewardQuestID = new int[6];
    }

    public sealed class MapDifficultyRecord
    {
        /// <summary>
        /// m_message_lang (text showed when transfer to map failed)
        /// </summary>
        public LocalizedString Message;
        public int Id;
        private ushort _difficultyID;
        public int LockID;
        private byte _resetInterval;
        public int MaxPlayers;
        public byte ItemContext;
        public int ItemContextPickerID;
        private int _flags;
        public int ContentTuningID;
        public int WorldStateExpressionID;
        public int MapID;

        #region Properties
        public Difficulty DifficultyID => (Difficulty)_difficultyID;
        public MapDifficultyResetInterval ResetInterval => (MapDifficultyResetInterval)_resetInterval;
        public MapDifficultyFlags Flags => (MapDifficultyFlags)_flags;
        #endregion

        #region Helpers
        public bool HasFlag(MapDifficultyFlags flag)
        {
            return _flags.HasFlag((byte)flag);
        }

        public bool HasAnyFlag(MapDifficultyFlags flag)
        {
            return _flags.HasAnyFlag((byte)flag);
        }

        public bool HasResetSchedule => ResetInterval != MapDifficultyResetInterval.Anytime;
        public bool IsUsingEncounterLocks => HasFlag(MapDifficultyFlags.UseLootBasedLockInsteadOfInstanceLock);
        public bool IsRestoringDungeonState => HasFlag(MapDifficultyFlags.ResumeDungeonProgressBasedOnLockout);
        public bool IsExtendable => !HasFlag(MapDifficultyFlags.DisableLockExtension);

        public Seconds RaidDuration
        {
            get
            {
                if (ResetInterval == MapDifficultyResetInterval.Daily)
                    return (Days)1;
                if (ResetInterval == MapDifficultyResetInterval.Weekly)
                    return (Days)7;
                return Seconds.Zero;
            }
        }
        #endregion
    }

    public sealed class MapDifficultyXConditionRecord
    {
        public int Id;
        public LocalizedString FailureDescription;
        public int PlayerConditionID;
        public int OrderIndex;
        private uint _mapDifficultyID;

        #region Properties
        public Difficulty MapDifficultyID => (Difficulty)_mapDifficultyID;
        #endregion
    }

    public sealed class ModifierTreeRecord
    {
        public int Id;
        public int Parent;
        public sbyte Operator;
        public sbyte Amount;
        private int _type;
        public int Asset;
        public int SecondaryAsset;
        public int TertiaryAsset;

        #region Properties
        public ModifierTreeType Type => (ModifierTreeType)_type;
        #endregion
    }

    public sealed class MountRecord
    {
        public LocalizedString Name;
        public LocalizedString SourceText;
        public LocalizedString Description;
        public int Id;
        public ushort MountTypeID;
        private int _flags;
        public sbyte SourceTypeEnum;
        public int SourceSpellID;
        public int PlayerConditionID;
        public float MountFlyRideHeight;
        public int UiModelSceneID;
        public int MountSpecialRiderAnimKitID;
        public int MountSpecialSpellVisualKitID;

        #region Properties
        public MountFlags Flags => (MountFlags)_flags;
        #endregion

        #region Helpers
        public bool HasFlag(MountFlags flag)
        {
            return _flags.HasFlag((ushort)flag);
        }

        public bool HasAnyFlag(MountFlags flag)
        {
            return _flags.HasAnyFlag((ushort)flag);
        }

        public bool IsSelfMount => HasFlag(MountFlags.SelfMount);
        #endregion
    }

    public sealed class MountCapabilityRecord
    {
        public int Id;
        private int _flags;
        public ushort ReqRidingSkill;
        public ushort ReqAreaID;
        public int ReqSpellAuraID;
        public int ReqSpellKnownID;
        public int ModSpellAuraID;
        public short ReqMapID;
        public int PlayerConditionID;
        public int FlightCapabilityID;
        public int DriveCapabilityID;

        #region Properties
        public MountCapabilityFlags Flags => (MountCapabilityFlags)_flags;
        #endregion

        #region Helpers
        public bool HasFlag(MountCapabilityFlags flag)
        {
            return _flags.HasFlag((byte)flag);
        }

        public bool HasAnyFlag(MountCapabilityFlags flag)
        {
            return _flags.HasAnyFlag((byte)flag);
        }
        #endregion
    }

    public sealed class MountTypeXCapabilityRecord
    {
        public int Id;
        private ushort _mountTypeID;
        public ushort MountCapabilityID;
        public byte OrderIndex;

        #region Properties
        public int MountTypeID => _mountTypeID;
        #endregion
    }

    public sealed class MountXDisplayRecord
    {
        public int Id;
        public int CreatureDisplayInfoID;
        public int PlayerConditionID;
        public ushort Unknown1100;
        public int MountID;
    }

    public sealed class MovieRecord
    {
        public int Id;
        public LocalizedString Summary;
        public byte Volume;
        public byte KeyID;
        public int AudioFileDataID;
        public int SubtitleFileDataID;
        public int SubtitleFileFormat;
    }

    public sealed class MythicPlusSeasonRecord
    {
        public int Id;
        public int MilestoneSeason;
        public int StartTimeEvent;
        public int ExpansionLevel;
        public int HeroicLFGDungeonMinGear;
    }
}
