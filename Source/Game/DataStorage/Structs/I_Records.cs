// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System;

namespace Game.DataStorage
{
    public sealed class ImportPriceArmorRecord
    {
        public int Id;
        public float ClothModifier;
        public float LeatherModifier;
        public float ChainModifier;
        public float PlateModifier;
    }

    public sealed class ImportPriceQualityRecord
    {
        public int Id;
        public float Data;
    }

    public sealed class ImportPriceShieldRecord
    {
        public int Id;
        public float Data;
    }

    public sealed class ImportPriceWeaponRecord
    {
        public int Id;
        public float Data;
    }

    public sealed class ItemRecord
    {
        public int Id;
        private int _classID = default;
        private byte _subclassID = default;
        public byte Material;
        private sbyte _inventoryType = default;
        public byte SheatheType;
        public int ItemPetFoodID;
        public sbyte SoundOverrideSubclassID;
        public int IconFileDataID;
        public int ItemGroupSoundsID;
        public int ContentTuningID;
        public int ModifiedCraftingReagentItemID;
        public byte Unknown1200;
        public int CraftingQualityID;
        public int ItemSquishEraID;
        public float RecraftReagentCountPercentage;
        public byte OrderSource;

        #region Properties
        public ItemClass ClassID => (ItemClass)_classID;
        public ItemSubClass SubclassID => new(_subclassID);
        public InventoryType InventoryType => (InventoryType)_inventoryType;
        #endregion        
    }

    public sealed class ItemAppearanceRecord
    {
        public int Id;
        public byte DisplayType;
        public int ItemDisplayInfoID;
        public int DefaultIconFileDataID;
        public int UiOrder;
        public int PlayerConditionID;
    }

    public sealed class ItemArmorQualityRecord
    {
        public int Id;
        public float[] QualityMod = new float[7];
    }

    public sealed class ItemArmorShieldRecord
    {
        public int Id;
        public float[] Quality = new float[7];
        public ushort ItemLevel;
    }

    public sealed class ItemArmorTotalRecord
    {
        public int Id;
        public short ItemLevel;
        public float Cloth;
        public float Leather;
        public float Mail;
        public float Plate;
    }

    public sealed class ItemBagFamilyRecord
    {
        public int Id;
        public LocalizedString Name;
    }

    public sealed class ItemBonusRecord
    {
        public int Id;
        public int[] Value = new int[4];
        public ushort ParentItemBonusListID;
        public byte _type;
        public byte OrderIndex;

        #region Properties
        public ItemBonusType Type => (ItemBonusType)_type;
        #endregion
    }

    public sealed class ItemBonusListLevelDeltaRecord
    {
        public short ItemLevelDelta;
        public int Id;
    }

    public sealed class ItemBonusTreeNodeRecord
    {
        public int Id;
        public byte ItemContext;
        public ushort ChildItemBonusTreeID;
        public ushort ChildItemBonusListID;
        public ushort ChildItemLevelSelectorID;
        public int ChildItemBonusListGroupID;
        public int IblGroupPointsModSetID;
        public int MinMythicPlusLevel;
        public int MaxMythicPlusLevel;
        public int ItemCreationContextGroupID;
        public int Flags;
        public int ParentItemBonusTreeID;
    }

    public sealed class ItemChildEquipmentRecord
    {
        public int Id;        
        public int ParentItemID;
        public int ChildItemID;
        public int ChildItemEquipSlot;
    }

    public sealed class ItemClassRecord
    {
        public int Id;
        public LocalizedString ClassName;
        public sbyte ClassID;
        public float PriceModifier;
        public int Flags;
    }

    public sealed class ItemContextPickerEntryRecord
    {
        public int Id;
        public byte ItemCreationContext;
        public byte OrderIndex;
        public int PVal;
        public int LabelID;
        public uint Flags;
        public int PlayerConditionID;
        public int ItemContextPickerID;
    }

    public sealed class ItemCurrencyCostRecord
    {
        public int Id;
        public int ItemID;
    }

    /// <summary>
    /// common struct for:<br/>
    /// ItemDamageAmmo.dbc<br/>
    /// ItemDamageOneHand.dbc<br/>
    /// ItemDamageOneHandCaster.dbc<br/>
    /// ItemDamageRanged.dbc<br/>
    /// ItemDamageThrown.dbc<br/>
    /// ItemDamageTwoHand.dbc<br/>
    /// ItemDamageTwoHandCaster.dbc<br/>
    /// ItemDamageWand.dbc
    /// </summary>
    public sealed class ItemDamageRecord
    {
        public int Id;
        public ushort ItemLevel;
        public float[] Quality = new float[7];
    }

    /// <summary>
    /// 1.60.1: ItemDamageRanged.db2, ItemDamageThrown.db2 and ItemDamageWand.db2 (client meta: int32 ItemLevel, float Quality[7])
    /// </summary>
    public sealed class ItemDamageRangedRecord
    {
        public int Id;
        public int ItemLevel;
        public float[] Quality = new float[7];
    }

    public sealed class ItemDisenchantLootRecord
    {
        public int Id;
        private sbyte _subclass = default;
        private byte _quality = default;
        public ushort MinLevel;
        public ushort MaxLevel;
        public ushort SkillRequired;
        private sbyte _expansionID = default;
        private uint _class = default;

        #region Properties
        public ItemSubClass Subclass => (ItemSubClass)_subclass;
        public ItemQuality Quality => (ItemQuality)_quality;
        public Expansion ExpansionID => (Expansion)_expansionID;
        public ItemClass Class => (ItemClass)_class;
        #endregion
    }

    public sealed class ItemEffectRecord
    {
        public int Id;
        public byte LegacySlotIndex;
        private sbyte _triggerType = default;
        public short Charges;
        private int _coolDownMSec = default;
        private int _categoryCoolDownMSec = default;
        private ushort _spellCategoryID = default;
        public int SpellID;
        public ushort ChrSpecializationID;
        public int PlayerConditionID;   // Forever/TC layout; items link to effects through ItemXItemEffect

        #region Properties
        public Milliseconds CoolDown => (Milliseconds)_coolDownMSec;
        public Milliseconds CategoryCoolDown => (Milliseconds)_categoryCoolDownMSec;
        public ItemSpelltriggerType TriggerType => (ItemSpelltriggerType)_triggerType;
        public SpellCategories SpellCategoryID => (SpellCategories)_spellCategoryID;
        #endregion
    }

    public sealed class ItemExtendedCostRecord
    {
        public int Id;
        public long Money;
        public ushort RequiredArenaRating;
        /// <summary>
        /// arena slot restrictions (min slot value)
        /// </summary>
        public byte ArenaBracket;
        public int Flags;
        public byte MinFactionID;
        private int _minReputation = default;
        /// <summary>
        /// required personal arena rating
        /// </summary>
        public byte RequiredAchievement;
        /// <summary>
        /// required item id
        /// </summary>
        public int[] ItemID = new int[5];
        /// <summary>
        /// required count of 1st item
        /// </summary>
        public ushort[] ItemCount = new ushort[5];
        /// <summary>
        /// required curency id
        /// </summary>
        public ushort[] CurrencyID = new ushort[5];
        /// <summary>
        /// required curency count
        /// </summary>
        public int[] CurrencyCount = new int[5];

        #region Properties
        public ReputationRank MinReputation => (ReputationRank)_minReputation;
        #endregion
    }

    public sealed class ItemLevelSelectorRecord
    {
        public int Id;
        public ushort MinItemLevel;
        public ushort ItemLevelSelectorQualitySetID;
        public ushort AzeriteUnlockMappingSet;
    }

    public sealed class ItemLevelSelectorQualityRecord : IEquatable<ItemLevelSelectorQualityRecord>, IEquatable<ItemQuality>
    {
        public int Id;
        public int QualityItemBonusListID;
        private sbyte _quality = default;
        public int ParentILSQualitySetID;

        #region Properties
        public ItemQuality Quality => (ItemQuality)_quality;
        #endregion

        #region Helpers
        public bool Equals(ItemLevelSelectorQualityRecord other) { return Quality < other.Quality; }
        public bool Equals(ItemQuality quality) { return Quality < quality; }
        #endregion       
    }

    public sealed class ItemLevelSelectorQualitySetRecord
    {
        public int Id;
        public short IlvlRare;
        public short IlvlEpic;
    }

    public sealed class ItemLimitCategoryRecord
    {
        public int Id;
        public LocalizedString Name;
        public byte Quantity;
        public int Flags;
    }

    public sealed class ItemLimitCategoryConditionRecord
    {
        public int Id;
        public sbyte AddQuantity;
        public int PlayerConditionID;
        public int ParentItemLimitCategoryID;
    }

    public sealed class ItemModifiedAppearanceRecord
    {
        public int Id;
        public int ItemID;
        public int ItemAppearanceModifierID;
        public int ItemAppearanceID;
        public int OrderIndex;
        public sbyte TransmogSourceTypeEnum;
        public int Flags;
    }

    public sealed class ItemModifiedAppearanceExtraRecord
    {
        public int Id;
        public int IconFileDataID;
        public int UnequippedIconFileDataID;
        public byte SheatheType;
        private sbyte _displayWeaponSubclassID = default;
        public sbyte DisplayInventoryType;

        #region Properties
        public ItemSubClass DisplayWeaponSubclassID => (ItemSubClass)_displayWeaponSubclassID;
        #endregion
    }

    public sealed class ItemNameDescriptionRecord
    {
        public int Id;
        public LocalizedString Description;
        public int Color;
    }

    public sealed class ItemPriceBaseRecord
    {
        public int Id;
        public ushort ItemLevel;
        public float Armor;
        public float Weapon;
    }

    public sealed class ItemRandomPropertiesRecord
    {
        public int Id;
        public LocalizedString Name;
        public ushort[] Enchantment = new ushort[ItemConst.MaxItemRandomProperties];
    };

    public sealed class ItemRandomSuffixRecord
    {
        public int Id;
        public LocalizedString Name;
        public ushort[] Enchantment = new ushort[ItemConst.MaxItemRandomProperties];
        public ushort[] AllocationPct = new ushort[ItemConst.MaxItemRandomProperties];
    };

    public sealed class ItemSearchNameRecord
    {
        public int Id;
        public LocalizedString Display;
        public byte OverallQualityID;
        public int ExpansionID;
        public ushort MinFactionID;
        public int MinReputation;
        public int AllowableClass;
        public sbyte RequiredLevel;
        public ushort RequiredSkill;
        public ushort RequiredSkillRank;
        public uint RequiredAbility;
        public ushort ItemLevel;
        public int[] Flags = new int[5];
        private int[] _raceMask = new int[2];

        #region Properties
        public RaceMask RaceMask => _raceMask.ToRaceMask();
        #endregion
    };

    public sealed class ItemSetRecord
    {
        public int Id;
        public LocalizedString Name;
        private uint _setFlags = default;
        public uint RequiredSkill;
        public ushort RequiredSkillRank;
        public uint[] ItemID = new uint[17];

        #region Properties
        public ItemSetFlags SetFlags => (ItemSetFlags)_setFlags;
        #endregion

        #region Helpers
        public bool HasFlag(ItemSetFlags flag)
        {
            return _setFlags.HasFlag((uint)flag);
        }

        public bool HasAnyFlag(ItemSetFlags flag)
        {
            return _setFlags.HasAnyFlag((uint)flag);
        }
        #endregion
    }

    public sealed class ItemSetSpellRecord
    {
        public int Id;
        public ushort ChrSpecID;
        public int SpellID;
        public ushort TraitSubTreeID;
        public byte Threshold;
        public int ItemSetID;
    }

    public sealed class ItemSparseRecord
    {
        public int Id;
        public LocalizedString Description;
        public LocalizedString Display3;
        public LocalizedString Display2;
        public LocalizedString Display1;
        public LocalizedString Display;
        private int _expansionID = default;
        public float DmgVariance;
        public int LimitCategory;
        private int _durationInInventory = default;
        public float QualityModifier;
        public uint BagFamily;
        public int StartQuestID;
        public int LanguageID;
        public float ItemRange;
        public float[] StatPercentageOfSocket = new float[10];
        public int[] StatPercentEditor = new int[10];
        private int[] _statModifierBonusStat = new int[10];
        public int Stackable;
        public int MaxCount;
        private int _minReputation = default;
        public int RequiredAbility;
        private int[] _allowableRace = new int[2];
        public uint SellPrice;
        public uint BuyPrice;
        public int VendorStackCount;
        public float PriceVariance;
        public float PriceRandomValue;
        public int[] Flags = new int[5];
        public int FactionRelated;
        public int ModifiedCraftingReagentItemID;
        public int ContentTuningID;
        public int PlayerLevelToItemLevelCurveID;
        public int ItemLevelOffsetCurveID;
        public int ItemLevelOffsetItemLevel;
        public int ItemSquishEraID;
        public ushort ItemNameDescriptionID;
        public ushort RequiredTransmogHoliday;
        public ushort RequiredHoliday;
        public ushort GemProperties;
        public ushort SocketMatchEnchantmentId;
        public ushort TotemCategoryID;
        public ushort InstanceBound;
        public ushort[] ZoneBound = new ushort[2];
        public ushort ItemSet;
        public ushort LockID;
        public ushort PageID;
        private ushort _itemDelay = default;
        public ushort MinFactionID;
        public ushort RequiredSkillRank;
        public ushort RequiredSkill;
        public ushort ItemLevel;
        private short _allowableClass = default;
        public byte ArtifactID;
        public byte SpellWeight;
        public byte SpellWeightCategory;
        public byte[] SocketType = new byte[3];
        public byte SheatheType;
        public byte Material;
        public byte PageMaterialID;
        public byte Bonding;
        private byte _damageType = default;
        public byte ContainerSlots;
        public byte RequiredPVPMedal;
        public byte RequiredPVPRank;
        public sbyte RequiredLevel;
        private sbyte _inventoryType = default;
        private sbyte _overallQualityID = default;
        public byte AmmunitionType;

        #region Properties
        public RaceMask AllowableRace => _allowableRace.ToRaceMask();
        public ClassMask AllowableClass => (ClassMask)_allowableClass;
        public SpellSchools DamageType => (SpellSchools)_damageType;
        public ItemModType StatModifierBonusStat(int itemStatSlot) => (ItemModType)_statModifierBonusStat[itemStatSlot];
        public InventoryType InventoryType => (InventoryType)_inventoryType;
        public ItemQuality OverallQualityID => (ItemQuality)_overallQualityID;
        public Expansion ExpansionID => (Expansion)_expansionID;
        public ReputationRank MinReputation => (ReputationRank)_minReputation;
        public Milliseconds ItemDelay => (Milliseconds)_itemDelay;
        public Seconds DurationInInventory => (Seconds)_durationInInventory;
        #endregion
    }

    public sealed class ItemSpecRecord
    {
        public int Id;
        public byte MinLevel;
        public byte MaxLevel;
        public byte ItemType;
        private byte _primaryStat = default;
        private byte _secondaryStat = default;
        public ushort SpecializationID;

        #region Properties
        public ItemSpecStat PrimaryStat => (ItemSpecStat)_primaryStat;
        public ItemSpecStat SecondaryStat => (ItemSpecStat)_secondaryStat;
        #endregion
    }

    public sealed class ItemSpecOverrideRecord
    {
        public int Id;
        public ushort SpecID;
        public int ItemID;
    }

    public sealed class ItemXItemEffectRecord
    {
        public int Id;
        public int ItemEffectID;
        public int ItemID;
    }

    public sealed class ItemXBonusTreeRecord
    {
        public int Id;
        public ushort ItemBonusTreeID;
        public int ItemID;
    }
}
