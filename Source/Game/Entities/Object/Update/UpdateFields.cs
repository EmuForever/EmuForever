// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Networking;
using Game.Networking.Packets;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Entities
{
    public class ObjectFieldData : HasChangesMask
    {
        public UpdateField<int> EntryId = new(0, 1);
        public UpdateField<uint> DynamicFlags = new(0, 2);
        public UpdateField<float> Scale = new(0, 3);
        static int ChangeMaskLength = 4;

        public ObjectFieldData() : base(0, TypeId.Object, ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, WorldObject owner, Player receiver)
        {
            data.WriteInt32(GetViewerDependentEntryId(this, owner, receiver));
            data.WriteUInt32(GetViewerDependentDynamicFlags(this, owner, receiver));
            data.WriteFloat(Scale);
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, WorldObject owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, WorldObject owner, Player receiver)
        {
            data.WriteBits(changesMask.GetBlock(0), ChangeMaskLength);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(GetViewerDependentEntryId(this, owner, receiver));
                }
                if (changesMask[2])
                {
                    data.WriteUInt32(GetViewerDependentDynamicFlags(this, owner, receiver));
                }
                if (changesMask[3])
                {
                    data.WriteFloat(Scale);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(EntryId);
            ClearChangesMask(DynamicFlags);
            ClearChangesMask(Scale);
            _changesMask.ResetAll();
        }

        int GetViewerDependentEntryId(ObjectFieldData objectData, WorldObject obj, Player receiver)
        {
            int entryId = objectData.EntryId;

            if (obj.ToUnit() is Unit unit)
            {
                if (unit.ToTempSummon() is TempSummon summon)
                    if (summon.GetSummonerGUID() == receiver.GetGUID() && summon.GetCreatureIdVisibleToSummoner().HasValue)
                        entryId = summon.GetCreatureIdVisibleToSummoner().Value;
            }

            return entryId;
        }

        uint GetViewerDependentDynamicFlags(ObjectFieldData objectData, WorldObject obj, Player receiver)
        {
            uint unitDynFlags = objectData.DynamicFlags;

            if (obj.ToUnit() is Unit unit)
            {
                if (obj.ToCreature() is Creature creature)
                {
                    if ((unitDynFlags & (uint)UnitDynFlags.Tapped) != 0 && !creature.IsTappedBy(receiver))
                        unitDynFlags &= ~(uint)UnitDynFlags.Tapped;

                    if ((unitDynFlags & (uint)UnitDynFlags.Lootable) != 0 && !receiver.IsAllowedToLoot(creature))
                        unitDynFlags &= ~(uint)UnitDynFlags.Lootable;

                    if ((unitDynFlags & (uint)UnitDynFlags.CanSkin) != 0 && creature.IsSkinnedBy(receiver))
                        unitDynFlags &= ~(uint)UnitDynFlags.CanSkin;
                }

                // unit UNIT_DYNFLAG_TRACK_UNIT should only be sent to caster of SPELL_AURA_MOD_STALKED auras
                if (unitDynFlags.HasAnyFlag((uint)UnitDynFlags.TrackUnit))
                    if (!unit.HasAuraTypeWithCaster(AuraType.ModStalked, receiver.GetGUID()))
                        unitDynFlags &= ~(uint)UnitDynFlags.TrackUnit;
            }
            else
            {
                if (obj.ToGameObject() is GameObject gameObject)
                {
                    GameObjectDynamicLowFlags dynFlags = 0;
                    ushort pathProgress = 0xFFFF;
                    switch (gameObject.GetGoType())
                    {
                        case GameObjectTypes.QuestGiver:
                            if (gameObject.ActivateToQuest(receiver))
                                dynFlags |= GameObjectDynamicLowFlags.Activate;
                            break;
                        case GameObjectTypes.Chest:
                            if (gameObject.ActivateToQuest(receiver))
                                dynFlags |= GameObjectDynamicLowFlags.Activate | GameObjectDynamicLowFlags.Sparkle | GameObjectDynamicLowFlags.Highlight;
                            else if (receiver.IsGameMaster())
                                dynFlags |= GameObjectDynamicLowFlags.Activate;
                            break;
                        case GameObjectTypes.Goober:
                            if (gameObject.ActivateToQuest(receiver))
                            {
                                dynFlags |= GameObjectDynamicLowFlags.Highlight;
                                if (gameObject.GetGoStateFor(receiver.GetGUID()) != GameObjectState.Active)
                                    dynFlags |= GameObjectDynamicLowFlags.Activate;
                            }
                            else if (receiver.IsGameMaster())
                                dynFlags |= GameObjectDynamicLowFlags.Activate;
                            break;
                        case GameObjectTypes.Generic:
                            if (gameObject.ActivateToQuest(receiver))
                                dynFlags |= GameObjectDynamicLowFlags.Sparkle | GameObjectDynamicLowFlags.Highlight;
                            break;
                        case GameObjectTypes.Transport:
                        case GameObjectTypes.MapObjTransport:
                        {
                            dynFlags = (GameObjectDynamicLowFlags)((int)unitDynFlags & 0xFFFF);
                            pathProgress = (ushort)((int)unitDynFlags >> 16);
                            break;
                        }
                        case GameObjectTypes.CapturePoint:
                            if (!gameObject.CanInteractWithCapturePoint(receiver))
                                dynFlags |= GameObjectDynamicLowFlags.NoInterract;
                            else
                                dynFlags &= ~GameObjectDynamicLowFlags.NoInterract;
                            break;
                        case GameObjectTypes.GatheringNode:
                            if (gameObject.ActivateToQuest(receiver))
                                dynFlags |= GameObjectDynamicLowFlags.Activate | GameObjectDynamicLowFlags.Sparkle | GameObjectDynamicLowFlags.Highlight;
                            if (gameObject.GetGoStateFor(receiver.GetGUID()) == GameObjectState.Active)
                                dynFlags |= GameObjectDynamicLowFlags.Depleted;
                            break;
                        default:
                            break;
                    }

                    if (!gameObject.MeetsInteractCondition(receiver))
                        dynFlags |= GameObjectDynamicLowFlags.NoInterract;

                    unitDynFlags = ((uint)pathProgress << 16) | (uint)dynFlags;
                }
            }

            return unitDynFlags;
        }
    }

    // Classic 1.60.1 ItemEnchantment layout (TrinityCore-forever UF::ItemEnchantment).
    public class ItemEnchantment : HasChangesMask
    {
        public UpdateField<int> ID = new(0, 1);
        public UpdateField<Milliseconds> Duration = new(0, 2);
        public UpdateField<short> Charges = new(0, 3);
        public UpdateField<ushort> Inactive = new(0, 4);
        static int ChangeMaskLength = 5;

        public ItemEnchantment() : base(ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, Item owner, Player receiver)
        {
            data.WriteInt32(ID);
            data.WriteUInt32((uint)Duration.GetValue());
            data.WriteInt16(Charges);
            data.WriteUInt16(Inactive);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Item owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), ChangeMaskLength);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(ID);
                }
                if (changesMask[2])
                {
                    data.WriteUInt32((uint)Duration.GetValue());
                }
                if (changesMask[3])
                {
                    data.WriteInt16(Charges);
                }
                if (changesMask[4])
                {
                    data.WriteUInt16(Inactive);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ID);
            ClearChangesMask(Duration);
            ClearChangesMask(Charges);
            ClearChangesMask(Inactive);
            _changesMask.ResetAll();
        }
    }

    public class ItemMod
    {
        public int Value;
        public byte Type;

        public void WriteCreate(WorldPacket data, Item owner, Player receiver)
        {
            data.WriteUInt8(Type);
            data.WriteInt32(Value);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Item owner, Player receiver)
        {
            data.WriteUInt8(Type);
            data.WriteInt32(Value);
        }
    }

    public class ItemModList : HasChangesMask
    {
        public DynamicUpdateField<ItemMod> Values = new(-1, 0);

        public ItemModList() : base(1) { }

        public void WriteCreate(WorldPacket data, Item owner, Player receiver)
        {
            data.WriteBits(Values.Size(), 7);
            for (int i = 0; i < Values.Size(); ++i)
            {
                Values[i].WriteCreate(data, owner, receiver);
            }
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Item owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), 1);

            if (changesMask[0])
            {
                if (!ignoreChangesMask)
                    Values.WriteUpdateMask(data, 7);
                else
                    WriteCompleteDynamicFieldUpdateMask(Values.Size(), data, 7);
            }

            data.FlushBits();

            if (changesMask[0])
            {
                for (int i = 0; i < Values.Size(); ++i)
                {
                    if (Values.HasChanged(i) || ignoreChangesMask)
                    {
                        Values[i].WriteUpdate(data, ignoreChangesMask, owner, receiver);
                    }
                }
            }

            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Values);
            _changesMask.ResetAll();
        }
    }

    public class ArtifactPower
    {
        public short ArtifactPowerId;
        public byte PurchasedRank;
        public byte CurrentRankWithBonus;

        public void WriteCreate(WorldPacket data, Item owner, Player receiver)
        {
            data.WriteInt16(ArtifactPowerId);
            data.WriteUInt8(PurchasedRank);
            data.WriteUInt8(CurrentRankWithBonus);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Item owner, Player receiver)
        {
            data.WriteInt16(ArtifactPowerId);
            data.WriteUInt8(PurchasedRank);
            data.WriteUInt8(CurrentRankWithBonus);
        }
    }

    public class SocketedGem : HasChangesMask
    {
        public UpdateField<int> ItemId = new(0, 1);
        public UpdateField<byte> Context = new(0, 2);
        public UpdateFieldArray<ushort> BonusListIDs = new(16, 3, 4);
        static int ChangeMaskLength = 20;

        public SocketedGem() : base(ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, Item owner, Player receiver)
        {
            data.WriteInt32(ItemId);
            for (int i = 0; i < 16; ++i)
                data.WriteUInt16(BonusListIDs[i]);

            data.WriteUInt8(Context);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Item owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlocksMask(0), 1);
            if (changesMask.GetBlock(0) != 0)
                data.WriteBits(changesMask.GetBlock(0), 32);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(ItemId);
                }
                if (changesMask[2])
                {
                    data.WriteUInt8(Context);
                }
            }
            if (changesMask[3])
            {
                for (int i = 0; i < 16; ++i)
                {
                    if (changesMask[4 + i])
                    {
                        data.WriteUInt16(BonusListIDs[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ItemId);
            ClearChangesMask(Context);
            ClearChangesMask(BonusListIDs);
            _changesMask.ResetAll();
        }
    }

    public class ItemData : HasChangesMask
    {
        public DynamicUpdateField<ArtifactPower> ArtifactPowers = new(0, 1);
        public DynamicUpdateField<SocketedGem> Gems = new(0, 2);
        public UpdateField<ObjectGuid> Owner = new(0, 3);
        public UpdateField<ObjectGuid> ContainedIn = new(0, 4);
        public UpdateField<ObjectGuid> Creator = new(0, 5);
        public UpdateField<ObjectGuid> GiftCreator = new(0, 6);
        public UpdateField<int> StackCount = new(0, 7);
        public UpdateField<Seconds> Expiration = new(0, 8);
        public UpdateField<uint> DynamicFlags = new(0, 9);
        public UpdateField<int> PropertySeed = new(0, 10);
        public UpdateField<int> RandomPropertiesID = new(0, 11);
        public UpdateField<int> Durability = new(0, 12);
        public UpdateField<int> MaxDurability = new(0, 13);
        public UpdateField<Seconds> CreatePlayedTime = new(0, 14);
        public UpdateField<int> Context = new(0, 15);
        public UpdateField<long> CreateTime = new(0, 16);
        public UpdateField<long> ArtifactXP = new(0, 17);
        public UpdateField<byte> ItemAppearanceModID = new(0, 18);
        public UpdateField<ItemModList> Modifiers = new(0, 19);
        public UpdateField<uint> DynamicFlags2 = new(0, 20);
        public UpdateField<ItemBonusKey> ItemBonusKey = new(0, 21);
        public UpdateField<ushort> DEBUGItemLevel = new(0, 22);
        public UpdateFieldArray<int> SpellCharges = new(5, 23, 24);
        public UpdateFieldArray<ItemEnchantment> Enchantment = new(13, 29, 30);
        static int ChangeMaskLength = 43;

        // Internal field bits use the WotLK model, including its visibility masks.
        // ToRefMask translates them to Forever wire bits; keep these number spaces separate.
        public ItemData() : base(0, TypeId.Item, ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Item owner, Player receiver)
        {
            data.WritePackedGuid(Owner);
            data.WritePackedGuid(ContainedIn);
            data.WritePackedGuid(Creator);
            data.WritePackedGuid(GiftCreator);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteInt32(StackCount);
                data.WriteInt32(Expiration.GetValue());
                for (int i = 0; i < 5; ++i)
                {
                    data.WriteInt32(SpellCharges[i]);
                }
            }
            data.WriteUInt32(DynamicFlags);
            for (int i = 0; i < 13; ++i)
            {
                Enchantment[i].WriteCreate(data, owner, receiver);
            }
            // 1.60.1 reads fourteen enchantment records; the core only models
            // the thirteen retail slots, so preserve the additional wire slot.
            data.WriteInt32(0);
            data.WriteUInt32(0);
            data.WriteInt16(0);
            data.WriteUInt16(0);
            // Classic 1.60.1 has no PropertySeed/RandomPropertiesID (WotLK random enchant fields) and a one-byte Context.
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteInt32(Durability);
                data.WriteInt32(MaxDurability);
            }
            data.WriteInt32(CreatePlayedTime.GetValue());
            data.WriteUInt8((byte)Context.GetValue());
            data.WriteInt64(CreateTime);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteInt64(ArtifactXP);
                data.WriteUInt8(ItemAppearanceModID);
            }
            Modifiers.GetValue().WriteCreate(data, owner, receiver);
            data.WriteInt32(ArtifactPowers.Size());
            data.WriteInt32(Gems.Size());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                uint flags = DynamicFlags2 | 0x4;
                if ((DynamicFlags2 & 0x1) != 0)
                    flags |= 0x2;
                data.WriteUInt32(flags);
            }
            ItemBonusKey.GetValue().Write(data);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteUInt16(DEBUGItemLevel);
            }
            for (int i = 0; i < ArtifactPowers.Size(); ++i)
            {
                ArtifactPowers[i].WriteCreate(data, owner, receiver);
            }
            for (int i = 0; i < Gems.Size(); ++i)
            {
                Gems[i].WriteCreate(data, owner, receiver);
            }
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Item owner, Player receiver)
        {
            UpdateMask allowedMaskForTarget = new(ChangeMaskLength, [0xE029CE7Fu, 0x000007FFu]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            WriteUpdate(data, _changesMask & allowedMaskForTarget, false, owner, receiver);
        }

        public void AppendAllowedFieldsMaskForFlag(UpdateMask allowedMaskForTarget, UpdateFieldFlag fieldVisibilityFlags)
        {
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
                allowedMaskForTarget.OR(new UpdateMask(ChangeMaskLength, [0x1FD63180u, 0x00000000u]));
        }

        public void FilterDisallowedFieldsMaskForFlag(UpdateMask changesMask, UpdateFieldFlag fieldVisibilityFlags)
        {
            UpdateMask allowedMaskForTarget = new(ChangeMaskLength, [0xE029CE7Fu, 0x000007FFu]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            changesMask.AND(allowedMaskForTarget);
        }

        // Generated from TrinityCore-forever UF::ItemData::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 41;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[1]) { r.Set(0); r.Set(1); }
            if (ours[2]) { r.Set(0); r.Set(2); }
            if (ours[3]) { r.Set(0); r.Set(3); }
            if (ours[4]) { r.Set(0); r.Set(4); }
            if (ours[5]) { r.Set(0); r.Set(5); }
            if (ours[6]) { r.Set(0); r.Set(6); }
            if (ours[7]) { r.Set(0); r.Set(7); }
            if (ours[8]) { r.Set(0); r.Set(8); }
            if (ours[9]) { r.Set(0); r.Set(9); }
            if (ours[12]) { r.Set(0); r.Set(10); }
            if (ours[13]) { r.Set(0); r.Set(11); }
            if (ours[14]) { r.Set(0); r.Set(12); }
            if (ours[15]) { r.Set(0); r.Set(13); }
            if (ours[16]) { r.Set(0); r.Set(14); }
            if (ours[17]) { r.Set(0); r.Set(15); }
            if (ours[18]) { r.Set(0); r.Set(16); }
            if (ours[19]) { r.Set(0); r.Set(17); }
            if (ours[20]) { r.Set(0); r.Set(18); }
            if (ours[21]) { r.Set(0); r.Set(19); }
            if (ours[22]) { r.Set(0); r.Set(20); }
            for (int i = 0; i < 5; ++i)
                if (ours[24 + i]) { r.Set(21); r.Set(22 + i); }
            for (int i = 0; i < 13; ++i)
                if (ours[30 + i]) { r.Set(27); r.Set(28 + i); }
            return r;
        }

        public void WriteUpdate(WorldPacket data, UpdateMask ourChangesMask, bool ignoreNestedChangesMask, Item owner, Player receiver)
        {
            UpdateMask changesMask = ToRefMask(ourChangesMask);

            data.WriteBits(changesMask.GetBlocksMask((uint)0), 2);
            for (int i = 0; i < 2; ++i)
            {
                if (changesMask.GetBlock((uint)i) != 0)
                {
                    data.WriteBits(changesMask.GetBlock((uint)i), 32);
                }
            }
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        ArtifactPowers.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(ArtifactPowers.Size(), data);
                    }
                }
                if (changesMask[2])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        Gems.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(Gems.Size(), data);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    for (int i = 0; i < ArtifactPowers.Size(); ++i)
                    {
                        if (ArtifactPowers.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            ArtifactPowers[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[2])
                {
                    for (int i = 0; i < Gems.Size(); ++i)
                    {
                        if (Gems.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            Gems[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[3])
                {
                    data.WritePackedGuid(Owner);
                }
                if (changesMask[4])
                {
                    data.WritePackedGuid(ContainedIn);
                }
                if (changesMask[5])
                {
                    data.WritePackedGuid(Creator);
                }
                if (changesMask[6])
                {
                    data.WritePackedGuid(GiftCreator);
                }
                if (changesMask[7])
                {
                    data.WriteUInt32((uint)StackCount.GetValue());
                }
                if (changesMask[8])
                {
                    data.WriteUInt32((uint)(int)Expiration.GetValue());
                }
                if (changesMask[9])
                {
                    data.WriteUInt32((uint)DynamicFlags.GetValue());
                }
                if (changesMask[10])
                {
                    data.WriteUInt32((uint)Durability.GetValue());
                }
                if (changesMask[11])
                {
                    data.WriteUInt32((uint)MaxDurability.GetValue());
                }
                if (changesMask[12])
                {
                    data.WriteUInt32((uint)(int)CreatePlayedTime.GetValue());
                }
                if (changesMask[13])
                {
                    data.WriteUInt8((byte)Context.GetValue());
                }
                if (changesMask[14])
                {
                    data.WriteInt64((long)CreateTime.GetValue());
                }
                if (changesMask[15])
                {
                    data.WriteUInt64((ulong)ArtifactXP.GetValue());
                }
                if (changesMask[16])
                {
                    data.WriteUInt8((byte)ItemAppearanceModID.GetValue());
                }
                if (changesMask[17])
                {
                    Modifiers.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                }
                if (changesMask[18])
                {
                    data.WriteUInt32(DynamicFlags2 | 0x4u | ((DynamicFlags2 & 0x1u) != 0 ? 0x2u : 0u));
                }
                if (changesMask[19])
                {
                    ItemBonusKey.GetValue().Write(data);
                }
                if (changesMask[20])
                {
                    data.WriteUInt16((ushort)DEBUGItemLevel.GetValue());
                }
            }
            if (changesMask[21])
            {
                for (int i = 0; i < 5; ++i)
                {
                    if (changesMask[22 + i])
                    {
                        data.WriteInt32((int)SpellCharges[i]);
                    }
                }
            }
            if (changesMask[27])
            {
                for (int i = 0; i < 13; ++i)
                {
                    if (changesMask[28 + i])
                    {
                        Enchantment[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ArtifactPowers);
            ClearChangesMask(Gems);
            ClearChangesMask(Owner);
            ClearChangesMask(ContainedIn);
            ClearChangesMask(Creator);
            ClearChangesMask(GiftCreator);
            ClearChangesMask(StackCount);
            ClearChangesMask(Expiration);
            ClearChangesMask(DynamicFlags);
            ClearChangesMask(PropertySeed);
            ClearChangesMask(RandomPropertiesID);
            ClearChangesMask(Durability);
            ClearChangesMask(MaxDurability);
            ClearChangesMask(CreatePlayedTime);
            ClearChangesMask(Context);
            ClearChangesMask(CreateTime);
            ClearChangesMask(ArtifactXP);
            ClearChangesMask(ItemAppearanceModID);
            ClearChangesMask(Modifiers);
            ClearChangesMask(DynamicFlags2);
            ClearChangesMask(ItemBonusKey);
            ClearChangesMask(DEBUGItemLevel);
            ClearChangesMask(SpellCharges);
            ClearChangesMask(Enchantment);
            _changesMask.ResetAll();
        }
    }

    // Classic 1.60.1 ContainerData layout (TrinityCore-forever UF::ContainerData): 98 slots.
    public class ContainerData : HasChangesMask
    {
        public UpdateField<int> NumSlots = new(0, 1);
        public UpdateFieldArray<ObjectGuid> Slots = new(98, 2, 3);
        static int ChangeMaskLength = 101;

        public ContainerData() : base(0, TypeId.Container, ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Bag owner, Player receiver)
        {
            for (int i = 0; i < 98; ++i)
            {
                data.WritePackedGuid(Slots[i]);
            }
            data.WriteUInt32((uint)NumSlots.GetValue());
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Bag owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, Bag owner, Player receiver)
        {
            data.WriteBits(changesMask.GetBlocksMask(0), 4);
            for (uint i = 0; i < 4; ++i)
                if (changesMask.GetBlock(i) != 0)
                    data.WriteBits(changesMask.GetBlock(i), 32);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteUInt32((uint)NumSlots.GetValue());
                }
            }
            if (changesMask[2])
            {
                for (int i = 0; i < 98; ++i)
                {
                    if (changesMask[3 + i])
                    {
                        data.WritePackedGuid(Slots[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(NumSlots);
            ClearChangesMask(Slots);
            _changesMask.ResetAll();
        }
    }

    // Classic 1.60.1 UnitChannel: SpellID, SpellCastVisual (SpellXSpellVisualID, ScriptVisualID), StartTimeMs, Duration.
    public class UnitChannel
    {
        public int SpellID;
        public int SpellXSpellVisualID;
        public int ScriptVisualID;
        public uint StartTimeMs;
        public uint Duration;

        public void WriteCreate(WorldPacket data, Unit owner, Player receiver)
        {
            data.WriteInt32(SpellID);
            data.WriteInt32(SpellXSpellVisualID);
            data.WriteInt32(ScriptVisualID);
            data.WriteUInt32(StartTimeMs);
            data.WriteUInt32(Duration);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Unit owner, Player receiver)
        {
            WriteCreate(data, owner, receiver);
        }
    }

    public class VisibleItem : HasChangesMask
    {
        public UpdateField<int> ItemID = new(0, 3);
        public UpdateField<ushort> ItemAppearanceModID = new(0, 6);
        public UpdateField<ushort> ItemVisual = new(0, 7);
        public UpdateField<int> SecondaryItemModifiedAppearanceID = new(0, 4);
        public UpdateField<uint> ItemModifiedAppearanceID = new(0, 8);
        static int ChangeMaskLength = 11; // Reference field mask; Classic inserts wire bit 6.

        public VisibleItem() : base(ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, Unit owner, Player receiver)
        {
            data.WriteInt32(ItemID);
            data.WriteInt32(SecondaryItemModifiedAppearanceID);
            data.WriteInt32(0); // ConditionalItemAppearanceID
            data.WriteInt32(0); // Classic unknown field after ConditionalItemAppearanceID
            data.WriteUInt16(ItemAppearanceModID);
            data.WriteUInt16(ItemVisual);
            data.WriteUInt32(ItemModifiedAppearanceID);
            data.WriteUInt8(0); // TransmogSlotOption
            data.WriteUInt8(0); // SheatheCategory
            data.WriteBit(false); // HasTransmog
            data.WriteBit(false); // HasIllusion
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Unit owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            // The Classic reader reserves bit 6 for the inserted int32 after
            // ConditionalItemAppearanceID. Shift reference bits 6..10 above it.
            uint block = changesMask.GetBlock(0);
            uint classicBlock = (block & 0x3Fu) | ((block & ~0x3Fu) << 1);
            if (ignoreChangesMask)
                classicBlock |= 1u << 6;
            data.WriteBits(classicBlock, 12);
            if (changesMask[0])
            {
                if (changesMask[1])
                    data.WriteBit(false); // HasTransmog
                if (changesMask[2])
                    data.WriteBit(false); // HasIllusion
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[3]) data.WriteInt32(ItemID);
                if (changesMask[4]) data.WriteInt32(SecondaryItemModifiedAppearanceID);
                if (changesMask[5]) data.WriteInt32(0); // ConditionalItemAppearanceID
                if (ignoreChangesMask) data.WriteInt32(0); // Classic inserted unknown field
                if (changesMask[6]) data.WriteUInt16(ItemAppearanceModID);
                if (changesMask[7]) data.WriteUInt16(ItemVisual);
                if (changesMask[8]) data.WriteUInt32(ItemModifiedAppearanceID);
                if (changesMask[9]) data.WriteUInt8(0); // TransmogSlotOption
                if (changesMask[10]) data.WriteUInt8(0); // SheatheCategory
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ItemID);
            ClearChangesMask(ItemAppearanceModID);
            ClearChangesMask(ItemVisual);
            ClearChangesMask(SecondaryItemModifiedAppearanceID);
            ClearChangesMask(ItemModifiedAppearanceID);
            _changesMask.ResetAll();
        }
    }

    public class PassiveSpellHistory
    {
        public int SpellID;
        public int AuraSpellID;

        public void WriteCreate(WorldPacket data, Unit owner, Player receiver)
        {
            data.WriteInt32(SpellID);
            data.WriteInt32(AuraSpellID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Unit owner, Player receiver)
        {
            data.WriteInt32(SpellID);
            data.WriteInt32(AuraSpellID);
        }
    }

    public class UnitData : HasChangesMask
    {
        public UpdateField<bool> Field_314 = new(0, 1);
        public UpdateField<List<int>> StateWorldEffectIDs = new(0, 2);
        public DynamicUpdateField<PassiveSpellHistory> PassiveSpells = new(0, 3);
        public DynamicUpdateField<int> WorldEffects = new(0, 4);
        public DynamicUpdateField<ObjectGuid> ChannelObjects = new(0, 5);
        public UpdateField<int> DisplayID = new(0, 6);
        public UpdateFieldArray<uint> NpcFlags = new(2, 0, 7);
        public UpdateField<int> StateSpellVisualID = new(0, 9);
        public UpdateField<int> StateAnimID = new(0, 10);
        public UpdateField<int> StateAnimKitID = new(0, 11);
        public UpdateField<uint> StateWorldEffectsQuestObjectiveID = new(0, 12);
        public UpdateField<int> SpellOverrideNameID = new(0, 13);
        public UpdateField<ObjectGuid> Charm = new(0, 14);
        public UpdateField<ObjectGuid> Summon = new(0, 15);
        public UpdateField<ObjectGuid> Critter = new(0, 16);
        public UpdateField<ObjectGuid> CharmedBy = new(0, 17);
        public UpdateField<ObjectGuid> SummonedBy = new(0, 18);
        public UpdateField<ObjectGuid> CreatedBy = new(0, 19);
        public UpdateField<ObjectGuid> DemonCreator = new(0, 20);
        public UpdateField<ObjectGuid> LookAtControllerTarget = new(0, 21);
        public UpdateField<ObjectGuid> Target = new(0, 22);
        public UpdateField<ObjectGuid> BattlePetCompanionGUID = new(0, 23);
        public UpdateField<ulong> BattlePetDBID = new(0, 24);
        public UpdateField<ObjectGuid> BattlePetAttachedToDecorGUID = new(0, 25);
        public UpdateField<ObjectGuid> BattlePetDecorHouseGUID = new(0, 26);
        public UpdateField<UnitChannel> ChannelData = new(0, 27);
        public UpdateField<sbyte> SpellEmpowerStage = new(0, 28);
        public UpdateField<uint> SummonedByHomeRealm = new(0, 29);
        public UpdateField<byte> Race = new(0, 30);
        public UpdateField<byte> ClassId = new(0, 31);
        public UpdateField<byte> PlayerClassId = new(32, 33);
        public UpdateField<byte> Sex = new(32, 34);
        public UpdateField<byte> CreatureType = new(32, 35);
        public UpdateField<byte> DisplayPower = new(32, 36);
        public UpdateField<int> OverrideDisplayPowerID = new(32, 37);
        public UpdateField<long> Health = new(32, 38);
        public UpdateField<long> MaxHealth = new(32, 39);
        public UpdateField<int> Level = new(32, 40);
        public UpdateField<int> EffectiveLevel = new(32, 41);
        public UpdateField<int> ContentTuningID = new(32, 42);
        public UpdateField<int> ScalingLevelMin = new(32, 43);
        public UpdateField<int> ScalingLevelMax = new(32, 44);
        public UpdateField<int> ScalingLevelDelta = new(32, 45);
        public UpdateField<int> ScalingFactionGroup = new(32, 46);
        public UpdateField<int> FactionTemplate = new(32, 47);
        public UpdateField<uint> Flags = new(32, 48);
        public UpdateField<uint> Flags2 = new(32, 49);
        public UpdateField<uint> Flags3 = new(32, 50);
        public UpdateField<uint> Flags4 = new(32, 51);
        public UpdateField<uint> AuraState = new(32, 52);
        public UpdateField<uint> RangedAttackRoundBaseTime = new(32, 53);
        public UpdateField<float> BoundingRadius = new(32, 54);
        public UpdateField<float> CombatReach = new(32, 55);
        public UpdateField<float> DisplayScale = new(32, 56);
        public UpdateField<int> CreatureFamily = new(32, 57);
        public UpdateField<byte> OverrideCreatureType = new(32, 58);
        public UpdateField<int> NativeDisplayID = new(32, 59);
        public UpdateField<float> NativeXDisplayScale = new(32, 60);
        public UpdateField<int> MountDisplayID = new(32, 61);
        public UpdateField<int> CosmeticMountDisplayID = new(32, 62);
        public UpdateField<float> MinDamage = new(32, 63);
        public UpdateField<float> MaxDamage = new(64, 65);
        public UpdateField<float> MinOffHandDamage = new(64, 66);
        public UpdateField<float> MaxOffHandDamage = new(64, 67);
        public UpdateField<byte> StandState = new(64, 68);
        public UpdateField<byte> PetTalentPoints = new(64, 69);
        public UpdateField<byte> VisFlags = new(64, 70);
        public UpdateField<byte> AnimTier = new(64, 71);
        public UpdateField<int> PetNumber = new(64, 72);
        public UpdateField<UnixTime> PetNameTimestamp = new(64, 73);
        public UpdateField<int> PetExperience = new(64, 74);
        public UpdateField<int> PetNextLevelExperience = new(64, 75);
        public UpdateField<float> ModCastingSpeed = new(64, 76);
        public UpdateField<float> ModCastingSpeedNeg = new(64, 77);
        public UpdateField<float> ModSpellHaste = new(64, 78);
        public UpdateField<float> ModHaste = new(64, 79);
        public UpdateField<float> ModRangedHaste = new(64, 80);
        public UpdateField<float> ModHasteRegen = new(64, 81);
        public UpdateField<float> ModTimeRate = new(64, 82);
        public UpdateField<int> CreatedBySpell = new(64, 83);
        public UpdateField<int> EmoteState = new(64, 84);
        public UpdateField<int> BaseMana = new(64, 85);
        public UpdateField<int> BaseHealth = new(64, 86);
        public UpdateField<byte> SheatheState = new(64, 87);
        public UpdateField<byte> PvpFlags = new(64, 88);
        public UpdateField<byte> PetFlags = new(64, 89);
        public UpdateField<byte> ShapeshiftForm = new(64, 90);
        public UpdateField<int> AttackPower = new(64, 91);
        public UpdateField<int> AttackPowerModPos = new(64, 92);
        public UpdateField<int> AttackPowerModNeg = new(64, 93);
        public UpdateField<float> AttackPowerMultiplier = new(64, 94);
        public UpdateField<int> AttackPowerModSupport = new(64, 95);
        public UpdateField<int> RangedAttackPower = new(96, 97);
        public UpdateField<int> RangedAttackPowerModPos = new(96, 98);
        public UpdateField<int> RangedAttackPowerModNeg = new(96, 99);
        public UpdateField<float> RangedAttackPowerMultiplier = new(96, 100);
        public UpdateField<int> RangedAttackPowerModSupport = new(96, 101);
        public UpdateField<int> MainHandWeaponAttackPower = new(96, 102);
        public UpdateField<int> OffHandWeaponAttackPower = new(96, 103);
        public UpdateField<int> RangedWeaponAttackPower = new(96, 104);
        public UpdateField<int> SetAttackSpeedAura = new(96, 105);
        public UpdateField<float> Lifesteal = new(96, 106);
        public UpdateField<float> MinRangedDamage = new(96, 107);
        public UpdateField<float> MaxRangedDamage = new(96, 108);
        public UpdateField<float> ManaCostMultiplier = new(96, 109);
        public UpdateField<float> MaxHealthModifier = new(96, 110);
        public UpdateField<float> HoverHeight = new(96, 111);
        public UpdateField<int> MinItemLevelCutoff = new(96, 112);
        public UpdateField<int> MinItemLevel = new(96, 113);
        public UpdateField<int> MaxItemLevel = new(96, 114);
        public UpdateField<int> AzeriteItemLevel = new(96, 115);
        public UpdateField<int> WildBattlePetLevel = new(96, 116);
        public UpdateField<int> BattlePetCompanionExperience = new(96, 117);
        public UpdateField<UnixTime> BattlePetCompanionNameTimestamp = new(96, 118);
        public UpdateField<int> InteractSpellID = new(96, 119);
        public UpdateField<int> ScaleDuration = new(96, 120);
        public UpdateField<int> LooksLikeMountID = new(96, 121);
        public UpdateField<int> LooksLikeCreatureID = new(96, 122);
        public UpdateField<int> LookAtControllerID = new(96, 123);
        public UpdateField<int> PerksVendorItemID = new(96, 124);
        public UpdateField<int> TaxiNodesID = new(96, 125);
        public UpdateField<ObjectGuid> GuildGUID = new(96, 126);
        public UpdateField<int> FlightCapabilityID = new(96, 127);
        public UpdateField<float> GlideEventSpeedDivisor = new(128, 129);                         // Movement speed gets divided by this value when evaluating what GlideEvents to use        
        public UpdateField<int> DriveCapabilityID = new(128, 130);
        public UpdateField<int> MaxHealthModifierFlatNeg = new(128, 131);
        public UpdateField<int> MaxHealthModifierFlatPos = new(128, 132);
        public UpdateField<uint> SilencedSchoolMask = new(128, 133);
        public UpdateField<uint> CurrentAreaID = new(128, 134);
        public UpdateField<float> NameplateDistanceMod = new(128, 135);
        public UpdateField<float> AutoAttackRangeMod = new(128, 136);
        public UpdateField<ObjectGuid> NameplateAttachToGUID = new(128, 137);
        public OptionalUpdateField<UnitAssistActionData> AssistActionData = new(128, 138);
        public UpdateFieldArray<int> Power = new(10, 139, 140);
        public UpdateFieldArray<int> MaxPower = new(10, 139, 150);
        public UpdateFieldArray<float> PowerRegenFlatModifier = new(10, 139, 160);
        public UpdateFieldArray<float> PowerRegenInterruptedFlatModifier = new(10, 139, 170);
        public UpdateFieldArray<VisibleItem> VirtualItems = new(3, 180, 181);
        public UpdateFieldArray<uint> AttackRoundBaseTime = new(2, 184, 185);
        public UpdateFieldArray<int> Stats = new(5, 187, 188);
        public UpdateFieldArray<int> StatPosBuff = new(5, 187, 193);
        public UpdateFieldArray<int> StatNegBuff = new(5, 187, 198);
        public UpdateFieldArray<int> StatSupportBuff = new(5, 187, 203);
        public UpdateFieldArray<int> Resistances = new(7, 208, 209);
        public UpdateFieldArray<int> BonusResistanceMods = new(7, 208, 216);
        public UpdateFieldArray<int> ManaCostModifier = new(7, 208, 223);

        // Classic-only reference fields retain the gameplay-facing names used by this core.
        public UpdateField<short> TrainingPointsUsed = new(64, 231);
        public UpdateField<short> TrainingPointsTotal = new(64, 232);
        public UpdateField<ObjectGuid> ComboTarget = new(128, 230);

        // WotLK-only gameplay state has no field in UF::UnitData. Keep its
        // local change bits outside the reference schema; never put them on the wire.
        public UpdateField<int> ScalingHealthItemLevelCurveID = new(256, 257);
        public UpdateField<int> ScalingDamageItemLevelCurveID = new(256, 258);
        public UpdateField<ObjectGuid> SkinningOwnerGUID = new(256, 261);
        public UpdateFieldArray<float> ModPowerRegen = new(10, 256, 263);                        // Applies to power regen only if expansion < 2, hidden from lua
        public UpdateFieldArray<int> PowerCostModifier = new(7, 256, 273);
        public UpdateFieldArray<float> PowerCostMultiplier = new(7, 256, 280);
        public UpdateFieldArray<int> ResistanceBuffModsPositive = new(7, 256, 287);
        public UpdateFieldArray<int> ResistanceBuffModsNegative = new(7, 256, 294);
        static int ChangeMaskLength = 301;

        public UnitData() : base(0, TypeId.Unit, ChangeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Unit owner, Player receiver)
        {
            List<int> stateWorldEffectIDs = null;
            data.WriteInt32(GetViewerDependentDisplayId(this, owner, receiver));
            data.WriteUInt32(GetViewerDependentNpcFlags(this, 0, owner, receiver));
            data.WriteUInt32(GetViewerDependentNpcFlags(this, 1, owner, receiver));
            data.WriteUInt32((uint)StateSpellVisualID.GetValue());
            data.WriteUInt32((uint)StateAnimID.GetValue());
            data.WriteUInt32((uint)StateAnimKitID.GetValue());
            stateWorldEffectIDs = StateWorldEffectIDs.GetValue();
            data.WriteUInt32((uint)stateWorldEffectIDs.Count);
            data.WriteUInt32(StateWorldEffectsQuestObjectiveID.GetValue());
            data.WriteInt32((int)SpellOverrideNameID.GetValue());
            for (int i = 0; i < stateWorldEffectIDs.Count; ++i)
            {
                data.WriteUInt32((uint)stateWorldEffectIDs[i]);
            }
            data.WritePackedGuid(Charm);
            data.WritePackedGuid(Summon);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WritePackedGuid(Critter);
            }
            data.WritePackedGuid(CharmedBy);
            data.WritePackedGuid(SummonedBy);
            data.WritePackedGuid(CreatedBy);
            data.WritePackedGuid(DemonCreator);
            data.WritePackedGuid(LookAtControllerTarget);
            data.WritePackedGuid(Target);
            data.WritePackedGuid(BattlePetCompanionGUID);
            data.WriteUInt64((ulong)BattlePetDBID.GetValue());
            data.WritePackedGuid(BattlePetAttachedToDecorGUID);
            data.WritePackedGuid(BattlePetDecorHouseGUID);
            ChannelData.GetValue().WriteCreate(data, owner, receiver);
            data.WriteInt8((sbyte)SpellEmpowerStage.GetValue());
            data.WriteUInt32((uint)SummonedByHomeRealm.GetValue());
            data.WriteUInt8((byte)Race.GetValue());
            data.WriteUInt8((byte)ClassId.GetValue());
            data.WriteUInt8((byte)PlayerClassId.GetValue());
            data.WriteUInt8((byte)Sex.GetValue());
            data.WriteUInt8((byte)CreatureType.GetValue());
            data.WriteUInt8((byte)DisplayPower.GetValue());
            data.WriteUInt32((uint)OverrideDisplayPowerID.GetValue());
            data.WriteInt64((long)Health.GetValue());
            for (int i = 0; i < 10; ++i)
            {
                data.WriteInt32((int)Power[i]);
                data.WriteInt32((int)MaxPower[i]);
            }
            if (fieldVisibilityFlags.HasAnyFlag(UpdateFieldFlag.Owner | UpdateFieldFlag.UnitAll))
            {
                for (int i = 0; i < 10; ++i)
                {
                    data.WriteFloat((float)PowerRegenFlatModifier[i]);
                    data.WriteFloat((float)PowerRegenInterruptedFlatModifier[i]);
                }
            }
            data.WriteInt64((long)MaxHealth.GetValue());
            data.WriteInt32((int)Level.GetValue());
            data.WriteInt32((int)EffectiveLevel.GetValue());
            data.WriteInt32((int)ContentTuningID.GetValue());
            data.WriteInt32((int)ScalingLevelMin.GetValue());
            data.WriteInt32((int)ScalingLevelMax.GetValue());
            data.WriteInt32((int)ScalingLevelDelta.GetValue());
            data.WriteUInt8((byte)ScalingFactionGroup.GetValue());
            data.WriteInt32(GetViewerDependentFactionTemplate(this, owner, receiver));
            for (int i = 0; i < 3; ++i)
            {
                VirtualItems[i].WriteCreate(data, owner, receiver);
            }
            data.WriteUInt32(GetViewerDependentFlags(this, owner, receiver));
            data.WriteUInt32(Flags2.GetValue());
            data.WriteUInt32(Flags3.GetValue());
            data.WriteUInt32(Flags4.GetValue());
            data.WriteUInt32(GetViewerDependentAuraState(this, owner, receiver));
            for (int i = 0; i < 2; ++i)
            {
                data.WriteUInt32((uint)AttackRoundBaseTime[i]);
            }
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteUInt32((uint)RangedAttackRoundBaseTime.GetValue());
            }
            data.WriteFloat((float)BoundingRadius.GetValue());
            data.WriteFloat((float)CombatReach.GetValue());
            data.WriteFloat((float)DisplayScale.GetValue());
            data.WriteInt32((int)CreatureFamily.GetValue());
            data.WriteUInt8((byte)OverrideCreatureType.GetValue());
            data.WriteInt32((int)NativeDisplayID.GetValue());
            data.WriteFloat((float)NativeXDisplayScale.GetValue());
            data.WriteInt32((int)MountDisplayID.GetValue());
            data.WriteInt32((int)CosmeticMountDisplayID.GetValue());
            if (fieldVisibilityFlags.HasAnyFlag(UpdateFieldFlag.Owner | UpdateFieldFlag.Empath))
            {
                data.WriteFloat((float)MinDamage.GetValue());
                data.WriteFloat((float)MaxDamage.GetValue());
                data.WriteFloat((float)MinOffHandDamage.GetValue());
                data.WriteFloat((float)MaxOffHandDamage.GetValue());
            }
            data.WriteUInt8((byte)StandState.GetValue());
            data.WriteUInt8((byte)PetTalentPoints.GetValue());
            data.WriteUInt8((byte)VisFlags.GetValue());
            data.WriteUInt8((byte)AnimTier.GetValue());            
            data.WriteInt8(0);
            data.WriteUInt32((uint)PetNumber.GetValue());
            data.WriteUInt32((uint)(int)PetNameTimestamp.GetValue());
            data.WriteUInt32((uint)PetExperience.GetValue());
            data.WriteUInt32((uint)PetNextLevelExperience.GetValue());
            data.WriteFloat((float)ModCastingSpeed.GetValue());
            data.WriteFloat(ModCastingSpeedNeg.GetValue() != 0.0f ? ModCastingSpeedNeg.GetValue() : 1.0f);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteFloat((float)ModSpellHaste.GetValue());
            }
            data.WriteFloat((float)ModHaste.GetValue());
            data.WriteFloat((float)ModRangedHaste.GetValue());
            data.WriteFloat((float)ModHasteRegen.GetValue());
            data.WriteFloat((float)ModTimeRate.GetValue());
            data.WriteInt32((int)CreatedBySpell.GetValue());
            data.WriteInt32((int)EmoteState.GetValue());
            data.WriteInt16(0); //data.WriteInt16((short)TrainingPointsUsed.GetValue());
            data.WriteInt16(0); //data.WriteInt16((short)TrainingPointsTotal.GetValue());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                for (int i = 0; i < 5; ++i)
                {
                    data.WriteInt32((int)Stats[i]);
                    data.WriteInt32((int)StatPosBuff[i]);
                    data.WriteInt32((int)StatNegBuff[i]);
                    data.WriteInt32((int)StatSupportBuff[i]);
                }
            }
            if (fieldVisibilityFlags.HasAnyFlag(UpdateFieldFlag.Owner | UpdateFieldFlag.Empath))
            {
                for (int i = 0; i < 7; ++i)
                {
                    data.WriteInt32((int)Resistances[i]);
                }
            }
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                for (int i = 0; i < 7; ++i)
                {
                    data.WriteInt32((int)BonusResistanceMods[i]);
                    data.WriteInt32((int)ManaCostModifier[i]);
                }
            }
            data.WriteInt32((int)BaseMana.GetValue());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteInt32((int)BaseHealth.GetValue());
            }
            data.WriteUInt8((byte)SheatheState.GetValue());
            data.WriteUInt8(GetViewerDependentPvpFlags(this, owner, receiver));
            data.WriteUInt8((byte)PetFlags.GetValue());
            data.WriteUInt8((byte)ShapeshiftForm.GetValue());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteInt32((int)AttackPower.GetValue());
                data.WriteInt32((int)AttackPowerModPos.GetValue());
                data.WriteInt32((int)AttackPowerModNeg.GetValue());
                data.WriteFloat((float)AttackPowerMultiplier.GetValue());
                data.WriteInt32((int)AttackPowerModSupport.GetValue());
                data.WriteInt32((int)RangedAttackPower.GetValue());
                data.WriteInt32((int)RangedAttackPowerModPos.GetValue());
                data.WriteInt32((int)RangedAttackPowerModNeg.GetValue());
                data.WriteFloat((float)RangedAttackPowerMultiplier.GetValue());
                data.WriteInt32((int)RangedAttackPowerModSupport.GetValue());
                data.WriteInt32(0);
                data.WriteInt32((int)MainHandWeaponAttackPower.GetValue());
                data.WriteInt32((int)OffHandWeaponAttackPower.GetValue());
                data.WriteInt32((int)RangedWeaponAttackPower.GetValue());
                data.WriteInt32((int)SetAttackSpeedAura.GetValue());
                data.WriteInt32(0);
                data.WriteFloat((float)Lifesteal.GetValue());
                data.WriteFloat((float)MinRangedDamage.GetValue());
                data.WriteFloat((float)MaxRangedDamage.GetValue());
                data.WriteFloat((float)ManaCostMultiplier.GetValue());
                // client 70425: three owner-only fields close the attack block (create reader rva 0x37CC2C6..0x37CC6C6 skips
                // UnitData +0x240/+0x244/+0x248 for other viewers, update bits 115-117). Sent after TaxiNodesID to every
                // viewer, they shifted the rest of another player's UnitData/PlayerData by 12 bytes (naked, wrong face, and
                // ChannelObjects.size read from GuildGUID bytes -> out-of-memory crash).
                data.WriteInt32(0);
                data.WriteInt32(0);
                data.WriteInt32(0);
            }
            data.WriteFloat((float)MaxHealthModifier.GetValue());
            data.WriteFloat((float)HoverHeight.GetValue());
            data.WriteInt32((int)MinItemLevelCutoff.GetValue());
            data.WriteInt32((int)MinItemLevel.GetValue());
            data.WriteInt32((int)MaxItemLevel.GetValue());
            data.WriteInt32((int)AzeriteItemLevel.GetValue());
            data.WriteInt32((int)WildBattlePetLevel.GetValue());
            data.WriteInt32((int)BattlePetCompanionExperience.GetValue());
            data.WriteUInt32((uint)(int)BattlePetCompanionNameTimestamp.GetValue());
            data.WriteInt32(GetViewerDependentInteractSpellId(this, owner, receiver));
            data.WriteInt32((int)ScaleDuration.GetValue());
            data.WriteInt32((int)LooksLikeMountID.GetValue());
            data.WriteInt32((int)LooksLikeCreatureID.GetValue());
            data.WriteInt32((int)LookAtControllerID.GetValue());
            data.WriteInt32((int)PerksVendorItemID.GetValue());
            data.WriteInt32((int)TaxiNodesID.GetValue());
            data.WritePackedGuid(GuildGUID);
            data.WriteUInt32((uint)PassiveSpells.Size());
            data.WriteUInt32((uint)WorldEffects.Size());
            data.WriteUInt32((uint)ChannelObjects.Size());
            data.WriteInt32((int)FlightCapabilityID.GetValue());
            data.WriteFloat((float)GlideEventSpeedDivisor.GetValue());
            data.WriteInt32((int)DriveCapabilityID.GetValue());
            data.WriteInt32((int)MaxHealthModifierFlatNeg.GetValue());
            data.WriteInt32((int)MaxHealthModifierFlatPos.GetValue());
            data.WriteUInt32((uint)SilencedSchoolMask.GetValue());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WriteFloat(0.0f);
                // client 70291+: owner-only list right after it (uint32 count, then count x (int32, int32))
                data.WriteUInt32(0);
            }
            data.WriteUInt32((uint)CurrentAreaID.GetValue());
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
            {
                data.WritePackedGuid(ComboTarget);
                data.WriteInt32(0);
                data.WriteFloat(0.0f);
            }
            data.WriteFloat((float)NameplateDistanceMod.GetValue());
            data.WriteFloat((float)AutoAttackRangeMod.GetValue());
            data.WritePackedGuid(NameplateAttachToGUID);
            for (int i = 0; i < PassiveSpells.Size(); ++i)
            {
                PassiveSpells[i].WriteCreate(data, owner, receiver);
            }
            for (int i = 0; i < WorldEffects.Size(); ++i)
            {
                data.WriteInt32((int)WorldEffects[i]);
            }
            for (int i = 0; i < ChannelObjects.Size(); ++i)
            {
                data.WritePackedGuid(ChannelObjects[i]);
            }
            data.WriteBit(Field_314.GetValue());
            data.WriteBit(AssistActionData.HasValue());
            data.FlushBits();
            if (AssistActionData.HasValue())
            {
                AssistActionData.GetValue().WriteCreate(data, owner, receiver);
            }
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Unit owner, Player receiver)
        {
            UpdateMask allowedMaskForTarget = new(ChangeMaskLength, [0xFFFEFFFFu, 0x7FDFFFFFu, 0x07BFBFF1u, 0xFFFFC001u, 0xFFFFFFFFu, 0x07F00000u, 0x00000000u, 0x00000180u]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            WriteUpdate(data, _changesMask & allowedMaskForTarget, false, owner, receiver);
        }

        public void AppendAllowedFieldsMaskForFlag(UpdateMask allowedMaskForTarget, UpdateFieldFlag fieldVisibilityFlags)
        {
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
                allowedMaskForTarget.OR(new UpdateMask(ChangeMaskLength, [0x00010000u, 0x80200000u, 0xF840400Eu, 0x00003FFEu, 0x00000800u, 0xF80FFFFFu, 0xFFFFFFFFu, 0x0000007Fu]));
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.UnitAll))
                allowedMaskForTarget.OR(new UpdateMask(ChangeMaskLength, [0x00000000u, 0x00000000u, 0x00000000u, 0x00000000u, 0x00000800u, 0x000FFFFFu, 0x00000000u, 0x00000000u]));
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Empath))
                allowedMaskForTarget.OR(new UpdateMask(ChangeMaskLength, [0x00000000u, 0x80000000u, 0x0000000Eu, 0x00000000u, 0x00000000u, 0x00000000u, 0x00FF0000u, 0x00000000u]));
        }

        public void FilterDisallowedFieldsMaskForFlag(UpdateMask changesMask, UpdateFieldFlag fieldVisibilityFlags)
        {
            UpdateMask allowedMaskForTarget = new(ChangeMaskLength, [0xFFFEFFFFu, 0x7FDFFFFFu, 0x07BFBFF1u, 0xFFFFC001u, 0xFFFFFFFFu, 0x07F00000u, 0x00000000u, 0x00000180u]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            changesMask.AND(allowedMaskForTarget);
        }

        // Generated from TrinityCore-forever UF::UnitData::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 233;

        public void WriteUpdate(WorldPacket data, UpdateMask ourChangesMask, bool ignoreNestedChangesMask, Unit owner, Player receiver)
        {
            UpdateMask changesMask = ourChangesMask;

            uint[] classicBlocks = ClassicUpdateMask.WriteHeader(data, changesMask, RefChangeMaskLength, 8, ClassicUpdateMask.UnitDataBit, ClassicUpdateMask.UnitDataGroups);
            bool classicGroup128 = (classicBlocks[4] & 1u) != 0;
            if (true)
            {
                data.WriteBit(false);
            }
            List<int> stateWorldEffectIDs = null;
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteBit(Field_314.GetValue());
                }
                if (changesMask[2])
                {
                    stateWorldEffectIDs = StateWorldEffectIDs.GetValue();
                    data.WriteBits(stateWorldEffectIDs.Count, 32);
                    for (int i = 0; i < stateWorldEffectIDs.Count; ++i)
                    {
                        data.WriteUInt32((uint)stateWorldEffectIDs[i]);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[3])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        PassiveSpells.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(PassiveSpells.Size(), data);
                    }
                }
                if (changesMask[4])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        WorldEffects.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(WorldEffects.Size(), data);
                    }
                }
                if (changesMask[5])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        ChannelObjects.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(ChannelObjects.Size(), data);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[3])
                {
                    for (int i = 0; i < PassiveSpells.Size(); ++i)
                    {
                        if (PassiveSpells.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            PassiveSpells[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[4])
                {
                    for (int i = 0; i < WorldEffects.Size(); ++i)
                    {
                        if (WorldEffects.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)WorldEffects[i]);
                        }
                    }
                }
                if (changesMask[5])
                {
                    for (int i = 0; i < ChannelObjects.Size(); ++i)
                    {
                        if (ChannelObjects.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WritePackedGuid(ChannelObjects[i]);
                        }
                    }
                }
                if (changesMask[6])
                {
                    data.WriteInt32(GetViewerDependentDisplayId(this, owner, receiver));
                }
                if (changesMask[7])
                {
                    data.WriteUInt32(GetViewerDependentNpcFlags(this, 0, owner, receiver));
                }
                if (changesMask[8])
                {
                    data.WriteUInt32(GetViewerDependentNpcFlags(this, 1, owner, receiver));
                }
                if (changesMask[9])
                {
                    data.WriteUInt32((uint)StateSpellVisualID.GetValue());
                }
                if (changesMask[10])
                {
                    data.WriteUInt32((uint)StateAnimID.GetValue());
                }
                if (changesMask[11])
                {
                    data.WriteUInt32((uint)StateAnimKitID.GetValue());
                }
                if (changesMask[12])
                {
                    data.WriteUInt32(StateWorldEffectsQuestObjectiveID.GetValue());
                }
                if (changesMask[13])
                {
                    data.WriteInt32((int)SpellOverrideNameID.GetValue());
                }
                if (changesMask[14])
                {
                    data.WritePackedGuid(Charm);
                }
                if (changesMask[15])
                {
                    data.WritePackedGuid(Summon);
                }
                if (changesMask[16])
                {
                    data.WritePackedGuid(Critter);
                }
                if (changesMask[17])
                {
                    data.WritePackedGuid(CharmedBy);
                }
                if (changesMask[18])
                {
                    data.WritePackedGuid(SummonedBy);
                }
                if (changesMask[19])
                {
                    data.WritePackedGuid(CreatedBy);
                }
                if (changesMask[20])
                {
                    data.WritePackedGuid(DemonCreator);
                }
                if (changesMask[21])
                {
                    data.WritePackedGuid(LookAtControllerTarget);
                }
                if (changesMask[22])
                {
                    data.WritePackedGuid(Target);
                }
                if (changesMask[23])
                {
                    data.WritePackedGuid(BattlePetCompanionGUID);
                }
                if (changesMask[24])
                {
                    data.WriteUInt64((ulong)BattlePetDBID.GetValue());
                }
                if (changesMask[25])
                {
                    data.WritePackedGuid(BattlePetAttachedToDecorGUID);
                }
                if (changesMask[26])
                {
                    data.WritePackedGuid(BattlePetDecorHouseGUID);
                }
                if (changesMask[27])
                {
                    ChannelData.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                }
                if (changesMask[28])
                {
                    data.WriteInt8((sbyte)SpellEmpowerStage.GetValue());
                }
                if (changesMask[29])
                {
                    data.WriteUInt32((uint)SummonedByHomeRealm.GetValue());
                }
                if (changesMask[30])
                {
                    data.WriteUInt8((byte)Race.GetValue());
                }
                if (changesMask[31])
                {
                    data.WriteUInt8((byte)ClassId.GetValue());
                }
            }
            if (changesMask[32])
            {
                if (changesMask[33])
                {
                    data.WriteUInt8((byte)PlayerClassId.GetValue());
                }
                if (changesMask[34])
                {
                    data.WriteUInt8((byte)Sex.GetValue());
                }
                if (changesMask[35])
                {
                    data.WriteUInt8((byte)CreatureType.GetValue());
                }
                if (changesMask[36])
                {
                    data.WriteUInt8((byte)DisplayPower.GetValue());
                }
                if (changesMask[37])
                {
                    data.WriteUInt32((uint)OverrideDisplayPowerID.GetValue());
                }
                if (changesMask[38])
                {
                    data.WriteInt64((long)Health.GetValue());
                }
                if (changesMask[39])
                {
                    data.WriteInt64((long)MaxHealth.GetValue());
                }
                if (changesMask[40])
                {
                    data.WriteInt32((int)Level.GetValue());
                }
                if (changesMask[41])
                {
                    data.WriteInt32((int)EffectiveLevel.GetValue());
                }
                if (changesMask[42])
                {
                    data.WriteInt32((int)ContentTuningID.GetValue());
                }
                if (changesMask[43])
                {
                    data.WriteInt32((int)ScalingLevelMin.GetValue());
                }
                if (changesMask[44])
                {
                    data.WriteInt32((int)ScalingLevelMax.GetValue());
                }
                if (changesMask[45])
                {
                    data.WriteInt32((int)ScalingLevelDelta.GetValue());
                }
                if (changesMask[46])
                {
                    data.WriteUInt8((byte)ScalingFactionGroup.GetValue());
                }
                if (changesMask[47])
                {
                    data.WriteInt32(GetViewerDependentFactionTemplate(this, owner, receiver));
                }
                if (changesMask[48])
                {
                    data.WriteUInt32(GetViewerDependentFlags(this, owner, receiver));
                }
                if (changesMask[49])
                {
                    data.WriteUInt32(Flags2.GetValue());
                }
                if (changesMask[50])
                {
                    data.WriteUInt32(Flags3.GetValue());
                }
                if (changesMask[51])
                {
                    data.WriteUInt32(Flags4.GetValue());
                }
                if (changesMask[52])
                {
                    data.WriteUInt32(GetViewerDependentAuraState(this, owner, receiver));
                }
                if (changesMask[53])
                {
                    data.WriteUInt32((uint)RangedAttackRoundBaseTime.GetValue());
                }
                if (changesMask[54])
                {
                    data.WriteFloat((float)BoundingRadius.GetValue());
                }
                if (changesMask[55])
                {
                    data.WriteFloat((float)CombatReach.GetValue());
                }
                if (changesMask[56])
                {
                    data.WriteFloat((float)DisplayScale.GetValue());
                }
                if (changesMask[57])
                {
                    data.WriteInt32((int)CreatureFamily.GetValue());
                }
                if (changesMask[58])
                {
                    data.WriteUInt8((byte)OverrideCreatureType.GetValue());
                }
                if (changesMask[59])
                {
                    data.WriteInt32((int)NativeDisplayID.GetValue());
                }
                if (changesMask[60])
                {
                    data.WriteFloat((float)NativeXDisplayScale.GetValue());
                }
                if (changesMask[61])
                {
                    data.WriteInt32((int)MountDisplayID.GetValue());
                }
                if (changesMask[62])
                {
                    data.WriteInt32((int)CosmeticMountDisplayID.GetValue());
                }
                if (changesMask[63])
                {
                    data.WriteFloat((float)MinDamage.GetValue());
                }
            }
            if (changesMask[64])
            {
                if (changesMask[65])
                {
                    data.WriteFloat((float)MaxDamage.GetValue());
                }
                if (changesMask[66])
                {
                    data.WriteFloat((float)MinOffHandDamage.GetValue());
                }
                if (changesMask[67])
                {
                    data.WriteFloat((float)MaxOffHandDamage.GetValue());
                }
                if (changesMask[68])
                {
                    data.WriteUInt8((byte)StandState.GetValue());
                }
                if (changesMask[69])
                {
                    data.WriteUInt8((byte)PetTalentPoints.GetValue());
                }
                if (changesMask[71])
                {
                    data.WriteUInt8((byte)AnimTier.GetValue());
                }
                if (changesMask[70])
                {
                    data.WriteUInt8((byte)VisFlags.GetValue());
                }
                if (changesMask[72])
                {
                    data.WriteUInt32((uint)PetNumber.GetValue());
                }
                if (changesMask[73])
                {
                    data.WriteUInt32((uint)(int)PetNameTimestamp.GetValue());
                }
                if (changesMask[74])
                {
                    data.WriteUInt32((uint)PetExperience.GetValue());
                }
                if (changesMask[75])
                {
                    data.WriteUInt32((uint)PetNextLevelExperience.GetValue());
                }
                if (changesMask[76])
                {
                    data.WriteFloat((float)ModCastingSpeed.GetValue());
                }
                if (changesMask[77])
                {
                    data.WriteFloat(ModCastingSpeedNeg.GetValue() != 0.0f ? ModCastingSpeedNeg.GetValue() : 1.0f);
                }
                if (changesMask[78])
                {
                    data.WriteFloat((float)ModSpellHaste.GetValue());
                }
                if (changesMask[79])
                {
                    data.WriteFloat((float)ModHaste.GetValue());
                }
                if (changesMask[80])
                {
                    data.WriteFloat((float)ModRangedHaste.GetValue());
                }
                if (changesMask[81])
                {
                    data.WriteFloat((float)ModHasteRegen.GetValue());
                }
                if (changesMask[82])
                {
                    data.WriteFloat((float)ModTimeRate.GetValue());
                }
                if (changesMask[83])
                {
                    data.WriteInt32((int)CreatedBySpell.GetValue());
                }
                if (changesMask[84])
                {
                    data.WriteInt32((int)EmoteState.GetValue());
                }
                if (changesMask[231])
                {
                    data.WriteInt16((short)TrainingPointsUsed.GetValue());
                }
                if (changesMask[232])
                {
                    data.WriteInt16((short)TrainingPointsTotal.GetValue());
                }
                if (changesMask[85])
                {
                    data.WriteInt32((int)BaseMana.GetValue());
                }
                if (changesMask[86])
                {
                    data.WriteInt32((int)BaseHealth.GetValue());
                }
                if (changesMask[87])
                {
                    data.WriteUInt8((byte)SheatheState.GetValue());
                }
                if (changesMask[88])
                {
                    data.WriteUInt8(GetViewerDependentPvpFlags(this, owner, receiver));
                }
                if (changesMask[89])
                {
                    data.WriteUInt8((byte)PetFlags.GetValue());
                }
                if (changesMask[90])
                {
                    data.WriteUInt8((byte)ShapeshiftForm.GetValue());
                }
                if (changesMask[91])
                {
                    data.WriteInt32((int)AttackPower.GetValue());
                }
                if (changesMask[92])
                {
                    data.WriteInt32((int)AttackPowerModPos.GetValue());
                }
                if (changesMask[93])
                {
                    data.WriteInt32((int)AttackPowerModNeg.GetValue());
                }
                if (changesMask[94])
                {
                    data.WriteFloat((float)AttackPowerMultiplier.GetValue());
                }
                if (changesMask[95])
                {
                    data.WriteInt32((int)AttackPowerModSupport.GetValue());
                }
            }
            if (changesMask[96])
            {
                if (changesMask[97])
                {
                    data.WriteInt32((int)RangedAttackPower.GetValue());
                }
                if (changesMask[98])
                {
                    data.WriteInt32((int)RangedAttackPowerModPos.GetValue());
                }
                if (changesMask[99])
                {
                    data.WriteInt32((int)RangedAttackPowerModNeg.GetValue());
                }
                if (changesMask[100])
                {
                    data.WriteFloat((float)RangedAttackPowerMultiplier.GetValue());
                }
                if (changesMask[101])
                {
                    data.WriteInt32((int)RangedAttackPowerModSupport.GetValue());
                }
                if (changesMask[102])
                {
                    data.WriteInt32((int)MainHandWeaponAttackPower.GetValue());
                }
                if (changesMask[103])
                {
                    data.WriteInt32((int)OffHandWeaponAttackPower.GetValue());
                }
                if (changesMask[104])
                {
                    data.WriteInt32((int)RangedWeaponAttackPower.GetValue());
                }
                if (changesMask[105])
                {
                    data.WriteInt32((int)SetAttackSpeedAura.GetValue());
                }
                if (changesMask[106])
                {
                    data.WriteFloat((float)Lifesteal.GetValue());
                }
                if (changesMask[107])
                {
                    data.WriteFloat((float)MinRangedDamage.GetValue());
                }
                if (changesMask[108])
                {
                    data.WriteFloat((float)MaxRangedDamage.GetValue());
                }
                if (changesMask[109])
                {
                    data.WriteFloat((float)ManaCostMultiplier.GetValue());
                }
                if (changesMask[110])
                {
                    data.WriteFloat((float)MaxHealthModifier.GetValue());
                }
                if (changesMask[111])
                {
                    data.WriteFloat((float)HoverHeight.GetValue());
                }
                if (changesMask[112])
                {
                    data.WriteInt32((int)MinItemLevelCutoff.GetValue());
                }
                if (changesMask[113])
                {
                    data.WriteInt32((int)MinItemLevel.GetValue());
                }
                if (changesMask[114])
                {
                    data.WriteInt32((int)MaxItemLevel.GetValue());
                }
                if (changesMask[115])
                {
                    data.WriteInt32((int)AzeriteItemLevel.GetValue());
                }
                if (changesMask[116])
                {
                    data.WriteInt32((int)WildBattlePetLevel.GetValue());
                }
                if (changesMask[117])
                {
                    data.WriteInt32((int)BattlePetCompanionExperience.GetValue());
                }
                if (changesMask[118])
                {
                    data.WriteUInt32((uint)(int)BattlePetCompanionNameTimestamp.GetValue());
                }
                if (changesMask[119])
                {
                    data.WriteInt32(GetViewerDependentInteractSpellId(this, owner, receiver));
                }
                if (changesMask[120])
                {
                    data.WriteInt32((int)ScaleDuration.GetValue());
                }
                if (changesMask[121])
                {
                    data.WriteInt32((int)LooksLikeMountID.GetValue());
                }
                if (changesMask[122])
                {
                    data.WriteInt32((int)LooksLikeCreatureID.GetValue());
                }
                if (changesMask[123])
                {
                    data.WriteInt32((int)LookAtControllerID.GetValue());
                }
                if (changesMask[124])
                {
                    data.WriteInt32((int)PerksVendorItemID.GetValue());
                }
                if (changesMask[125])
                {
                    data.WriteInt32((int)TaxiNodesID.GetValue());
                }
                if (changesMask[126])
                {
                    data.WritePackedGuid(GuildGUID);
                }
                if (changesMask[127])
                {
                    data.WriteInt32((int)FlightCapabilityID.GetValue());
                }
            }
            if (changesMask[128])
            {
                if (changesMask[129])
                {
                    data.WriteFloat((float)GlideEventSpeedDivisor.GetValue());
                }
                if (changesMask[130])
                {
                    data.WriteInt32((int)DriveCapabilityID.GetValue());
                }
                if (changesMask[131])
                {
                    data.WriteInt32((int)MaxHealthModifierFlatNeg.GetValue());
                }
                if (changesMask[132])
                {
                    data.WriteInt32((int)MaxHealthModifierFlatPos.GetValue());
                }
                if (changesMask[133])
                {
                    data.WriteUInt32((uint)SilencedSchoolMask.GetValue());
                }
                if (changesMask[134])
                {
                    data.WriteUInt32((uint)CurrentAreaID.GetValue());
                }
                if (changesMask[230])
                {
                    data.WritePackedGuid(ComboTarget);
                }
                if (changesMask[135])
                {
                    data.WriteFloat((float)NameplateDistanceMod.GetValue());
                }
                if (changesMask[136])
                {
                    data.WriteFloat((float)AutoAttackRangeMod.GetValue());
                }
                if (changesMask[137])
                {
                    data.WritePackedGuid(NameplateAttachToGUID);
                }
            }
            if (classicGroup128)
            {
                data.WriteBit(AssistActionData.HasValue());
            }
            data.FlushBits();
            if (changesMask[128])
            {
                if (changesMask[138])
                {
                    if (AssistActionData.HasValue())
                    {
                        AssistActionData.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            if (changesMask[139])
            {
                for (int i = 0; i < 10; ++i)
                {
                    if (changesMask[140 + i])
                    {
                        data.WriteInt32((int)Power[i]);
                    }
                    if (changesMask[150 + i])
                    {
                        data.WriteInt32((int)MaxPower[i]);
                    }
                    if (changesMask[160 + i])
                    {
                        data.WriteFloat((float)PowerRegenFlatModifier[i]);
                    }
                    if (changesMask[170 + i])
                    {
                        data.WriteFloat((float)PowerRegenInterruptedFlatModifier[i]);
                    }
                }
            }
            if (changesMask[180])
            {
                for (int i = 0; i < 3; ++i)
                {
                    if (changesMask[181 + i])
                    {
                        VirtualItems[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            if (changesMask[184])
            {
                for (int i = 0; i < 2; ++i)
                {
                    if (changesMask[185 + i])
                    {
                        data.WriteUInt32((uint)AttackRoundBaseTime[i]);
                    }
                }
            }
            if (changesMask[187])
            {
                for (int i = 0; i < 5; ++i)
                {
                    if (changesMask[188 + i])
                    {
                        data.WriteInt32((int)Stats[i]);
                    }
                    if (changesMask[193 + i])
                    {
                        data.WriteInt32((int)StatPosBuff[i]);
                    }
                    if (changesMask[198 + i])
                    {
                        data.WriteInt32((int)StatNegBuff[i]);
                    }
                    if (changesMask[203 + i])
                    {
                        data.WriteInt32((int)StatSupportBuff[i]);
                    }
                }
            }
            if (changesMask[208])
            {
                for (int i = 0; i < 7; ++i)
                {
                    if (changesMask[209 + i])
                    {
                        data.WriteInt32((int)Resistances[i]);
                    }
                    if (changesMask[216 + i])
                    {
                        data.WriteInt32((int)BonusResistanceMods[i]);
                    }
                    if (changesMask[223 + i])
                    {
                        data.WriteInt32((int)ManaCostModifier[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Field_314);
            ClearChangesMask(StateWorldEffectIDs);
            ClearChangesMask(PassiveSpells);
            ClearChangesMask(WorldEffects);
            ClearChangesMask(ChannelObjects);
            ClearChangesMask(DisplayID);
            ClearChangesMask(NpcFlags);
            ClearChangesMask(StateSpellVisualID);
            ClearChangesMask(StateAnimID);
            ClearChangesMask(StateAnimKitID);
            ClearChangesMask(StateWorldEffectsQuestObjectiveID);
            ClearChangesMask(SpellOverrideNameID);
            ClearChangesMask(Charm);
            ClearChangesMask(Summon);
            ClearChangesMask(Critter);
            ClearChangesMask(CharmedBy);
            ClearChangesMask(SummonedBy);
            ClearChangesMask(CreatedBy);
            ClearChangesMask(DemonCreator);
            ClearChangesMask(LookAtControllerTarget);
            ClearChangesMask(Target);
            ClearChangesMask(BattlePetCompanionGUID);
            ClearChangesMask(BattlePetDBID);
            ClearChangesMask(BattlePetAttachedToDecorGUID);
            ClearChangesMask(BattlePetDecorHouseGUID);
            ClearChangesMask(ChannelData);
            ClearChangesMask(SpellEmpowerStage);
            ClearChangesMask(SummonedByHomeRealm);
            ClearChangesMask(Race);
            ClearChangesMask(ClassId);
            ClearChangesMask(PlayerClassId);
            ClearChangesMask(Sex);
            ClearChangesMask(CreatureType);
            ClearChangesMask(DisplayPower);
            ClearChangesMask(OverrideDisplayPowerID);
            ClearChangesMask(Health);
            ClearChangesMask(MaxHealth);
            ClearChangesMask(Level);
            ClearChangesMask(EffectiveLevel);
            ClearChangesMask(ContentTuningID);
            ClearChangesMask(ScalingLevelMin);
            ClearChangesMask(ScalingLevelMax);
            ClearChangesMask(ScalingLevelDelta);
            ClearChangesMask(ScalingFactionGroup);
            ClearChangesMask(FactionTemplate);
            ClearChangesMask(Flags);
            ClearChangesMask(Flags2);
            ClearChangesMask(Flags3);
            ClearChangesMask(Flags4);
            ClearChangesMask(AuraState);
            ClearChangesMask(RangedAttackRoundBaseTime);
            ClearChangesMask(BoundingRadius);
            ClearChangesMask(CombatReach);
            ClearChangesMask(DisplayScale);
            ClearChangesMask(CreatureFamily);
            ClearChangesMask(OverrideCreatureType);
            ClearChangesMask(NativeDisplayID);
            ClearChangesMask(NativeXDisplayScale);
            ClearChangesMask(MountDisplayID);
            ClearChangesMask(CosmeticMountDisplayID);
            ClearChangesMask(MinDamage);
            ClearChangesMask(MaxDamage);
            ClearChangesMask(MinOffHandDamage);
            ClearChangesMask(MaxOffHandDamage);
            ClearChangesMask(StandState);
            ClearChangesMask(PetTalentPoints);
            ClearChangesMask(VisFlags);
            ClearChangesMask(AnimTier);
            ClearChangesMask(PetNumber);
            ClearChangesMask(PetNameTimestamp);
            ClearChangesMask(PetExperience);
            ClearChangesMask(PetNextLevelExperience);
            ClearChangesMask(ModCastingSpeed);
            ClearChangesMask(ModCastingSpeedNeg);
            ClearChangesMask(ModSpellHaste);
            ClearChangesMask(ModHaste);
            ClearChangesMask(ModRangedHaste);
            ClearChangesMask(ModHasteRegen);
            ClearChangesMask(ModTimeRate);
            ClearChangesMask(CreatedBySpell);
            ClearChangesMask(EmoteState);
            ClearChangesMask(BaseMana);
            ClearChangesMask(BaseHealth);
            ClearChangesMask(SheatheState);
            ClearChangesMask(PvpFlags);
            ClearChangesMask(PetFlags);
            ClearChangesMask(ShapeshiftForm);
            ClearChangesMask(AttackPower);
            ClearChangesMask(AttackPowerModPos);
            ClearChangesMask(AttackPowerModNeg);
            ClearChangesMask(AttackPowerMultiplier);
            ClearChangesMask(AttackPowerModSupport);
            ClearChangesMask(RangedAttackPower);
            ClearChangesMask(RangedAttackPowerModPos);
            ClearChangesMask(RangedAttackPowerModNeg);
            ClearChangesMask(RangedAttackPowerMultiplier);
            ClearChangesMask(RangedAttackPowerModSupport);
            ClearChangesMask(MainHandWeaponAttackPower);
            ClearChangesMask(OffHandWeaponAttackPower);
            ClearChangesMask(RangedWeaponAttackPower);
            ClearChangesMask(SetAttackSpeedAura);
            ClearChangesMask(Lifesteal);
            ClearChangesMask(MinRangedDamage);
            ClearChangesMask(MaxRangedDamage);
            ClearChangesMask(ManaCostMultiplier);
            ClearChangesMask(MaxHealthModifier);
            ClearChangesMask(HoverHeight);
            ClearChangesMask(MinItemLevelCutoff);
            ClearChangesMask(MinItemLevel);
            ClearChangesMask(MaxItemLevel);
            ClearChangesMask(AzeriteItemLevel);
            ClearChangesMask(WildBattlePetLevel);
            ClearChangesMask(BattlePetCompanionExperience);
            ClearChangesMask(BattlePetCompanionNameTimestamp);
            ClearChangesMask(InteractSpellID);
            ClearChangesMask(ScaleDuration);
            ClearChangesMask(LooksLikeMountID);
            ClearChangesMask(LooksLikeCreatureID);
            ClearChangesMask(LookAtControllerID);
            ClearChangesMask(PerksVendorItemID);
            ClearChangesMask(TaxiNodesID);
            ClearChangesMask(GuildGUID);
            ClearChangesMask(FlightCapabilityID);
            ClearChangesMask(GlideEventSpeedDivisor);
            ClearChangesMask(DriveCapabilityID);
            ClearChangesMask(MaxHealthModifierFlatNeg);
            ClearChangesMask(MaxHealthModifierFlatPos);
            ClearChangesMask(SilencedSchoolMask);
            ClearChangesMask(CurrentAreaID);
            ClearChangesMask(NameplateDistanceMod);
            ClearChangesMask(AutoAttackRangeMod);
            ClearChangesMask(NameplateAttachToGUID);
            ClearChangesMask(AssistActionData);
            ClearChangesMask(Power);
            ClearChangesMask(MaxPower);
            ClearChangesMask(PowerRegenFlatModifier);
            ClearChangesMask(PowerRegenInterruptedFlatModifier);
            ClearChangesMask(VirtualItems);
            ClearChangesMask(AttackRoundBaseTime);
            ClearChangesMask(Stats);
            ClearChangesMask(StatPosBuff);
            ClearChangesMask(StatNegBuff);
            ClearChangesMask(StatSupportBuff);
            ClearChangesMask(Resistances);
            ClearChangesMask(BonusResistanceMods);
            ClearChangesMask(ManaCostModifier);
            ClearChangesMask(ScalingHealthItemLevelCurveID);
            ClearChangesMask(ScalingDamageItemLevelCurveID);
            ClearChangesMask(TrainingPointsUsed);
            ClearChangesMask(TrainingPointsTotal);
            ClearChangesMask(SkinningOwnerGUID);
            ClearChangesMask(ComboTarget);
            ClearChangesMask(ModPowerRegen);
            ClearChangesMask(PowerCostModifier);
            ClearChangesMask(PowerCostMultiplier);
            ClearChangesMask(ResistanceBuffModsPositive);
            ClearChangesMask(ResistanceBuffModsNegative);
            _changesMask.ResetAll();
        }

        protected virtual int GetViewerDependentDisplayId(UnitData unitData, Unit unit, Player receiver)
        {
            int displayId = unitData.DisplayID;
            if (unit.IsCreature())
            {
                CreatureTemplate cinfo = unit.ToCreature().GetCreatureTemplate();

                if (unit.ToTempSummon() is TempSummon summon)
                {
                    if (summon.GetSummonerGUID() == receiver.GetGUID())
                    {
                        if (summon.GetCreatureIdVisibleToSummoner().HasValue)
                            cinfo = Global.ObjectMgr.GetCreatureTemplate(summon.GetCreatureIdVisibleToSummoner().Value);

                        if (summon.GetDisplayIdVisibleToSummoner().HasValue)
                            displayId = summon.GetDisplayIdVisibleToSummoner().Value;
                    }
                }

                // this also applies for transform auras
                SpellInfo transform = Global.SpellMgr.GetSpellInfo(unit.GetTransformSpell(), unit.GetMap().GetDifficultyID());
                if (transform != null)
                {
                    foreach (var spellEffectInfo in transform.GetEffects())
                    {
                        if (spellEffectInfo.IsAura(AuraType.Transform))
                        {
                            CreatureTemplate transformInfo = Global.ObjectMgr.GetCreatureTemplate(spellEffectInfo.MiscValue);
                            if (transformInfo != null)
                            {
                                cinfo = transformInfo;
                                break;
                            }
                        }
                    }
                }

                if (cinfo.FlagsExtra.HasAnyFlag(CreatureFlagsExtra.Trigger))
                    if (receiver.IsGameMaster())
                        displayId = cinfo.GetFirstVisibleModel().CreatureDisplayID;
            }

            return displayId;
        }

        protected virtual uint GetViewerDependentNpcFlags(UnitData unitData, int i, Unit unit, Player receiver)
        {
            uint npcFlag = unitData.NpcFlags[i];
            if (npcFlag != 0)
            {
                if ((!unit.IsInteractionAllowedInCombat() && unit.IsInCombat())
                    || (!unit.IsInteractionAllowedWhileHostile() && unit.IsHostileTo(receiver)))
                    npcFlag = 0;
                else
                {
                    Creature creature = unit.ToCreature();
                    if (creature != null)
                    {
                        if (i == 0)
                        {
                            if (!receiver.CanSeeGossipOn(creature))
                                npcFlag &= ~(uint)(NPCFlags1.Gossip | NPCFlags1.QuestGiver);

                            if (!receiver.CanSeeSpellClickOn(creature))
                                npcFlag &= ~(uint)NPCFlags1.SpellClick;
                        }
                    }
                }
            }

            return npcFlag;
        }

        protected virtual int GetViewerDependentFactionTemplate(UnitData unitData, Unit unit, Player receiver)
        {
            int factionTemplate = unitData.FactionTemplate;
            if (unit.IsControlledByPlayer() && receiver != unit 
                && WorldConfig.Values[WorldCfg.AllowTwoSideInteractionGroup].Bool 
                && unit.IsInRaidWith(receiver))
            {
                FactionTemplateRecord ft1 = unit.GetFactionTemplateEntry();
                FactionTemplateRecord ft2 = receiver.GetFactionTemplateEntry();
                if (ft1 != null && ft2 != null && !ft1.IsFriendlyTo(ft2))
                    // pretend that all other HOSTILE players have own faction, to allow follow, heal, rezz (trade wont work)
                    factionTemplate = receiver.GetFaction();
            }

            return factionTemplate;
        }

        protected virtual uint GetViewerDependentFlags(UnitData unitData, Unit unit, Player receiver)
        {
            uint flags = unitData.Flags;
            // Update fields of triggers, transformed units or uninteractible units (values dependent on GM state)
            if (receiver.IsGameMaster())
                flags &= ~(uint)UnitFlags.Uninteractible;

            return flags;
        }

        protected virtual uint GetViewerDependentFlags3(UnitData unitData, Unit unit, Player receiver)
        {
            uint flags = unitData.Flags3;
            if ((flags & (uint)UnitFlags3.AlreadySkinned) != 0 && unit.IsCreature() && !unit.ToCreature().IsSkinnedBy(receiver))
                flags &= ~(uint)UnitFlags3.AlreadySkinned;

            return flags;
        }

        protected virtual uint GetViewerDependentAuraState(UnitData unitData, Unit unit, Player receiver)
        {
            // Check per caster aura states to not enable using a spell in client if specified aura is not by target
            return unit.BuildAuraStateUpdateForTarget(receiver);
        }
        protected virtual byte GetViewerDependentPvpFlags(UnitData unitData, Unit unit, Player receiver)
        {
            byte pvpFlags = unitData.PvpFlags;
            if (unit.IsControlledByPlayer() && receiver != unit 
                && WorldConfig.Values[WorldCfg.AllowTwoSideInteractionGroup].Bool 
                && unit.IsInRaidWith(receiver))
            {
                FactionTemplateRecord ft1 = unit.GetFactionTemplateEntry();
                FactionTemplateRecord ft2 = receiver.GetFactionTemplateEntry();
                if (ft1 != null && ft2 != null && !ft1.IsFriendlyTo(ft2))
                    // Allow targeting opposite faction in party when enabled in config
                    pvpFlags &= (byte)UnitPVPStateFlags.Sanctuary;
            }

            return pvpFlags;
        }

        protected virtual int GetViewerDependentInteractSpellId(UnitData unitData, Unit unit, Player receiver)
        {
            int interactSpellId = unitData.InteractSpellID;
            if (unitData.NpcFlags[0].HasFlag((uint)NPCFlags1.SpellClick) && interactSpellId == 0)
            {
                // this field is not set if there are multiple available spellclick spells
                var clickBounds = Global.ObjectMgr.GetSpellClickInfoMapBounds(unit.GetEntry());
                foreach (var spellClickInfo in clickBounds)
                {
                    if (!spellClickInfo.IsFitToRequirements(receiver, unit))
                        continue;

                    if (!Global.ConditionMgr.IsObjectMeetingSpellClickConditions(unit.GetEntry(), spellClickInfo.spellId, receiver, unit))
                        continue;

                    interactSpellId = spellClickInfo.spellId;
                    break;
                }

            }
            return interactSpellId;
        }
    }

    public class ChrCustomizationChoice : IComparable<ChrCustomizationChoice>
    {
        public ChrCustomizationChoice() { }

        public ChrCustomizationChoice(WorldPacket data)
        {
            ChrCustomizationOptionID = data.ReadInt32();
            ChrCustomizationChoiceID = data.ReadInt32();
        }

        public void WriteCreate(WorldPacket data, WorldObject owner, Player receiver)
        {
            data.WriteInt32(ChrCustomizationOptionID);
            data.WriteInt32(ChrCustomizationChoiceID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, WorldObject owner, Player receiver)
        {
            data.WriteInt32(ChrCustomizationOptionID);
            data.WriteInt32(ChrCustomizationChoiceID);
        }        

        public int CompareTo(ChrCustomizationChoice other)
        {
            return ChrCustomizationOptionID.CompareTo(other.ChrCustomizationOptionID);
        }

        public int ChrCustomizationOptionID;
        public int ChrCustomizationChoiceID;
    }

    // Classic 1.60.1 QuestLog layout (TrinityCore-forever UF::QuestLog).
    public class QuestLog : HasChangesMask
    {
        public UpdateField<int> QuestID = new(0, 1);
        public UpdateField<uint> StateFlags = new(0, 2);
        public UpdateField<UnixTime64> EndTime = new(0, 3);
        public UpdateField<uint> ObjectiveFlags = new(0, 4);
        public UpdateField<uint> EnabledObjectivesMask = new(0, 5);
        public UpdateFieldArray<ushort> ObjectiveProgress = new(24, 6, 7);
        static int changeMaskLength = 31;

        public QuestLog() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(QuestID);
            data.WriteUInt16((ushort)StateFlags.GetValue());
            for (int i = 0; i < 24; ++i)
            {
                data.WriteUInt16(ObjectiveProgress[i]);
            }
            data.WriteInt64(EndTime.GetValue());
            data.WriteUInt32(ObjectiveFlags);
            data.WriteUInt32(EnabledObjectivesMask);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlocksMask(0), 1);
            if (changesMask.GetBlock(0) != 0)
                data.WriteBits(changesMask.GetBlock(0), 32);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(QuestID);
                }
                if (changesMask[2])
                {
                    data.WriteUInt16((ushort)StateFlags.GetValue());
                }
                if (changesMask[3])
                {
                    data.WriteInt64(EndTime.GetValue());
                }
                if (changesMask[4])
                {
                    data.WriteUInt32(ObjectiveFlags);
                }
                if (changesMask[5])
                {
                    data.WriteUInt32(EnabledObjectivesMask);
                }
            }
            if (changesMask[6])
            {
                for (int i = 0; i < 24; ++i)
                {
                    if (changesMask[7 + i])
                    {
                        data.WriteUInt16(ObjectiveProgress[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(QuestID);
            ClearChangesMask(StateFlags);
            ClearChangesMask(EndTime);
            ClearChangesMask(ObjectiveFlags);
            ClearChangesMask(EnabledObjectivesMask);
            ClearChangesMask(ObjectiveProgress);
            _changesMask.ResetAll();
        }
    }

    public class ArenaCooldown : HasChangesMask
    {
        public UpdateField<int> SpellID = new(0, 1);
        public UpdateField<int> Charges = new(0, 2);
        public UpdateField<uint> Flags = new(0, 3);
        public UpdateField<uint> StartTime = new(0, 4);
        public UpdateField<uint> EndTime = new(0, 5);
        public UpdateField<uint> NextChargeTime = new(0, 6);
        public UpdateField<byte> MaxCharges = new(0, 7);
        static int changeMaskLength = 8;

        public ArenaCooldown() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(SpellID);
            data.WriteInt32(Charges);
            data.WriteUInt32(Flags);
            data.WriteUInt32(StartTime);
            data.WriteUInt32(EndTime);
            data.WriteUInt32(NextChargeTime);
            data.WriteUInt8(MaxCharges);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(SpellID);
                }
                if (changesMask[2])
                {
                    data.WriteInt32(Charges);
                }
                if (changesMask[3])
                {
                    data.WriteUInt32(Flags);
                }
                if (changesMask[4])
                {
                    data.WriteUInt32(StartTime);
                }
                if (changesMask[5])
                {
                    data.WriteUInt32(EndTime);
                }
                if (changesMask[6])
                {
                    data.WriteUInt32(NextChargeTime);
                }
                if (changesMask[7])
                {
                    data.WriteUInt8(MaxCharges);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(SpellID);
            ClearChangesMask(Charges);
            ClearChangesMask(Flags);
            ClearChangesMask(StartTime);
            ClearChangesMask(EndTime);
            ClearChangesMask(NextChargeTime);
            ClearChangesMask(MaxCharges);
            _changesMask.ResetAll();
        }
    }

    public class PlayerData : HasChangesMask
    {
        public DynamicUpdateField<ChrCustomizationChoice> Customizations = new(0, 1);
        public DynamicUpdateField<ArenaCooldown> ArenaCooldowns = new(0, 2);
        public DynamicUpdateField<int> VisualItemReplacements = new(0, 3);
        public UpdateField<ObjectGuid> DuelArbiter = new(0, 4);
        public UpdateField<ObjectGuid> WowAccount = new(0, 5);
        public UpdateField<ObjectGuid> LootTargetGUID = new(0, 6);
        public UpdateField<PlayerFlags> PlayerFlags = new(0, 7);
        public UpdateField<PlayerFlagsEx> PlayerFlagsEx = new(0, 8);
        public UpdateField<int> GuildRankID = new(0, 9);
        public UpdateField<uint> GuildDeleteDate = new(0, 10);
        public UpdateField<int> GuildLevel = new(0, 11);
        public UpdateField<byte> NumBankSlots = new(0, 12);
        public UpdateField<byte> NativeSex = new(0, 13);
        public UpdateField<byte> Inebriation = new(0, 14);
        public UpdateField<byte> PvpTitle = new(0, 15);
        public UpdateField<byte> ArenaFaction = new(0, 16);
        public UpdateField<byte> PvpRank = new(0, 17);
        public UpdateField<int> Field_88 = new(0, 18);
        public UpdateField<uint> DuelTeam = new(0, 19);
        public UpdateField<int> GuildTimeStamp = new(0, 20);
        public UpdateField<int> PlayerTitle = new(0, 21);
        public UpdateField<int> FakeInebriation = new(0, 22);
        public UpdateField<uint> VirtualPlayerRealm = new(0, 23);
        public UpdateField<int> CurrentSpecID = new(0, 24);
        public UpdateField<int> TaxiMountAnimKitID = new(0, 25);
        public UpdateField<byte> CurrentBattlePetBreedQuality = new(0, 26);
        public UpdateField<int> HonorLevel = new(0, 27);
        public UpdateField<UnixTime64> LogoutTime = new(0, 28);
        public UpdateField<int> CurrentBattlePetSpeciesID = new(0, 29);
        public UpdateField<ObjectGuid> BnetAccount = new(0, 30);                    // For telemetry
        public UpdateField<DungeonScoreSummary> DungeonScore = new(0, 31);
        public UpdateFieldArray<byte> PartyType = new(2, 32, 33);
        public UpdateFieldArray<QuestLog> QuestLog = new(25, 35, 36);
        public UpdateFieldArray<VisibleItem> VisibleItems = new(19, 61, 62);
        public UpdateFieldArray<float> AvgItemLevel = new(6, 81, 82);
        public UpdateFieldArray<uint> Field_3120 = new(19, 88, 89);
        public UpdateField<ulong> GuildClubMemberID = new(0, 108);
        static int changeMaskLength = 109;

        public PlayerData() : base(0, TypeId.Player, changeMaskLength) { }

        const int ClassicQuestLogSize = 175;

        // Classic 1.60.1: 17-byte struct (4x uint32 + int8) whose first uint32 is the character's super district (ruleset).
        static void WriteClassicSuperDistrict(WorldPacket data, uint superDistrictId)
        {
            data.WriteUInt32(superDistrictId);
            data.WriteUInt32(0);
            data.WriteUInt32(0);
            data.WriteUInt32(0);
            data.WriteInt8(0);
        }

        // QuestLogQuestIdToIndex (quest id -> quest log slot), computed from the quest log.
        void WriteQuestLogQuestIdToIndex(WorldPacket data)
        {
            var questSlots = new SortedDictionary<int, int>();
            for (int i = 0; i < QuestLog.GetSize(); ++i)
                if (QuestLog[i].QuestID != 0)
                    questSlots[QuestLog[i].QuestID] = i;
            data.WriteUInt32((uint)questSlots.Count);
            foreach (var (questId, slot) in questSlots)
            {
                data.WriteInt32(questId);
                data.WriteInt32(slot);
            }
        }

        // Map field update sent as a full replacement (ignoreChangesMask = 1, then the whole map).
        void WriteQuestLogQuestIdToIndexUpdate(WorldPacket data)
        {
            data.WriteUInt8(1);
            WriteQuestLogQuestIdToIndex(data);
        }

        // ItemInstance with no item: ItemID, empty ItemModList (7 bits), no ItemBonus (1 bit).
        static void WriteEmptyItemInstance(WorldPacket data)
        {
            data.WriteInt32(0);
            data.WriteBits(0, 7);
            data.FlushBits();
            data.WriteBit(false);
            data.FlushBits();
        }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Player owner, Player receiver)
        {
            // Classic 1.60.1 PlayerData create layout (TrinityCore-forever UF::PlayerData::WriteCreate).
            // Fields this core does not model are written with the reference server's defaults.
            uint superDistrictId = (uint)Framework.Configuration.ConfigMgr.GetDefaultValue("SuperDistrictID", 2);

            data.WritePackedGuid(DuelArbiter);
            data.WritePackedGuid(WowAccount);
            data.WritePackedGuid(BnetAccount);
            data.WriteUInt64(GuildClubMemberID);
            data.WritePackedGuid(LootTargetGUID);
            data.WriteUInt32((uint)PlayerFlags.GetValue());
            data.WriteUInt32((uint)PlayerFlagsEx.GetValue());
            data.WriteUInt32((uint)GuildRankID.GetValue());
            data.WriteUInt32(GuildDeleteDate);
            data.WriteInt32(GuildLevel);
            WriteClassicSuperDistrict(data, superDistrictId);
            data.WriteUInt32((uint)Customizations.Size());
            data.WriteUInt32(0); // RandomCustomizations
            for (int i = 0; i < 2; ++i)
                data.WriteUInt8(PartyType[i]);
            data.WriteUInt8(NativeSex);
            data.WriteUInt8(Inebriation);
            data.WriteUInt8(PvpTitle);
            data.WriteUInt8(ArenaFaction);
            data.WriteUInt32(DuelTeam);
            data.WriteInt32(GuildTimeStamp);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.PartyMember))
            {
                // the client has 175 slots, the core uses the first 25
                var emptyQuest = new QuestLog();
                for (int i = 0; i < ClassicQuestLogSize; ++i)
                    (i < QuestLog.GetSize() ? QuestLog[i] : emptyQuest).WriteCreate(data, owner, receiver);

                WriteQuestLogQuestIdToIndex(data);
                data.WriteUInt32(0); // QuestSessionQuestLog
            }
            for (int i = 0; i < 19; ++i)
                VisibleItems[i].WriteCreate(data, owner, receiver);
            data.WriteInt32(PlayerTitle);
            data.WriteInt32(FakeInebriation);
            data.WriteUInt32(VirtualPlayerRealm);
            data.WriteUInt32((uint)CurrentSpecID.GetValue());
            data.WriteInt32(0); // CurrentCombatTraitConfigSubTreeID
            data.WriteInt32(TaxiMountAnimKitID);
            for (int i = 0; i < 6; ++i)
                data.WriteFloat(AvgItemLevel[i]);
            data.WriteUInt8(CurrentBattlePetBreedQuality);
            data.WriteInt32(HonorLevel);
            data.WriteInt64(LogoutTime.GetValue());
            data.WriteUInt32((uint)ArenaCooldowns.Size());
            for (int i = 0; i < 32; ++i)
            {
                data.WriteInt32(0); // ForcedReactions[i].FactionID
                data.WriteInt32(0); // ForcedReactions[i].Reaction
            }
            data.WriteInt32(0); // OfferedAdvJournalQuestID
            data.WriteInt32(0); // OfferedScriptQuestID
            data.WriteInt32(CurrentBattlePetSpeciesID);
            data.WriteUInt32(0); // PetNames
            // CtrOptions
            data.WriteUInt32(0); // ConditionalFlags
            data.WriteUInt8(0);  // FactionGroup
            data.WriteUInt32(0); // ChromieTimeExpansionMask
            data.WriteInt32(0); // CovenantID
            data.WriteInt32(0); // SoulbindID
            DungeonScore._value.Write(data);
            // LeaverInfo
            data.WritePackedGuid(ObjectGuid.Empty);
            data.WriteFloat(0.0f);
            data.WriteUInt32(0);
            data.WriteUInt32(0);
            data.WriteUInt32(0);
            data.WriteInt32(0);
            data.WriteInt64(0);
            data.WriteInt64(0);
            data.WriteInt32(0);
            data.WriteBit(false);
            data.FlushBits();
            data.WritePackedGuid(ObjectGuid.Empty); // SpectateTarget
            data.WriteInt32(0); // WorldLootObjectInventorySwapSlot
            for (int i = 0; i < 16; ++i)
                WriteEmptyItemInstance(data); // VisibleEquipableSpells
            data.WriteUInt32((uint)VisualItemReplacements.Size());
            for (int i = 0; i < 19; ++i)
                data.WriteUInt32(Field_3120[i]); // PlunderstormItemDisplayID
            for (int i = 0; i < 5; ++i)
                data.WriteInt32(0); // PersonalTabard
            // NpcAsPlayerInfo
            data.WriteInt32(0);
            data.WriteInt32(0);
            data.WriteInt32(0);
            data.WriteFloat(0.0f);
            data.WriteFloat(0.0f);
            data.WriteFloat(0.0f);
            data.WriteFloat(0.0f);
            data.WritePackedGuid(ObjectGuid.Empty);
            WriteClassicSuperDistrict(data, superDistrictId);
            for (int i = 0; i < Customizations.Size(); ++i)
                Customizations[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < ArenaCooldowns.Size(); ++i)
                ArenaCooldowns[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < VisualItemReplacements.Size(); ++i)
                data.WriteInt32(VisualItemReplacements[i]);

            string name = owner.GetName() ?? "";
            string surname = Global.CharacterCacheStorage.GetCharacterSurnameByGuid(owner.GetGUID()) ?? "";
            data.WriteBits(name.GetByteCount(), 6);
            data.WriteBits(surname.GetByteCount(), 9);
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.PartyMember))
                data.WriteBit(false); // HasQuestSession
            data.WriteBit(false); // HasLevelLink
            data.WriteBit(false); // DeclinedNames
            data.FlushBits();
            data.WriteString(name);
            data.WriteString(surname);
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Player owner, Player receiver)
        {
            UpdateMask allowedMaskForTarget = new(changeMaskLength, [0xFFFFFFFFu, 0xE0000007u, 0xFFFFFFFFu, 0x00001FFFu]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            WriteUpdate(data, _changesMask & allowedMaskForTarget, false, owner, receiver);
        }

        public void AppendAllowedFieldsMaskForFlag(UpdateMask allowedMaskForTarget, UpdateFieldFlag fieldVisibilityFlags)
        {
            if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.PartyMember))
                allowedMaskForTarget.OR(new UpdateMask(changeMaskLength, [0x00000000u, 0x1FFFFFF8u, 0x00000000u, 0x00000000u]));
        }

        public void FilterDisallowedFieldsMaskForFlag(UpdateMask changesMask, UpdateFieldFlag fieldVisibilityFlags)
        {
            UpdateMask allowedMaskForTarget = new(changeMaskLength, [0xFFFFFFFFu, 0xE0000007u, 0xFFFFFFFFu, 0x00001FFFu]);
            AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
            changesMask.AND(allowedMaskForTarget);
        }

        // Generated from TrinityCore-forever UF::PlayerData::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 326;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[108]) { r.Set(0); r.Set(12); }
            if (ours[1]) { r.Set(0); r.Set(3); }
            if (ours[2]) { r.Set(0); r.Set(6); }
            if (ours[3]) { r.Set(0); r.Set(8); }
            if (ours[4]) { r.Set(0); r.Set(9); }
            if (ours[5]) { r.Set(0); r.Set(10); }
            if (ours[30]) { r.Set(0); r.Set(11); }
            if (ours[6]) { r.Set(0); r.Set(13); }
            if (ours[7]) { r.Set(0); r.Set(14); }
            if (ours[8]) { r.Set(0); r.Set(15); }
            if (ours[9]) { r.Set(0); r.Set(16); }
            if (ours[10]) { r.Set(0); r.Set(17); }
            if (ours[11]) { r.Set(0); r.Set(18); }
            if (ours[13]) { r.Set(0); r.Set(19); }
            if (ours[14]) { r.Set(0); r.Set(20); }
            if (ours[15]) { r.Set(0); r.Set(21); }
            if (ours[16]) { r.Set(0); r.Set(22); }
            if (ours[19]) { r.Set(0); r.Set(23); }
            if (ours[20]) { r.Set(0); r.Set(24); }
            if (ours[21]) { r.Set(0); r.Set(26); }
            if (ours[22]) { r.Set(0); r.Set(27); }
            if (ours[23]) { r.Set(0); r.Set(28); }
            if (ours[24]) { r.Set(0); r.Set(29); }
            if (ours[25]) { r.Set(0); r.Set(31); }
            if (ours[26]) { r.Set(32); r.Set(33); }
            if (ours[27]) { r.Set(32); r.Set(34); }
            if (ours[28]) { r.Set(32); r.Set(35); }
            if (ours[29]) { r.Set(32); r.Set(39); }
            if (ours[31]) { r.Set(32); r.Set(43); }
            for (int i = 0; i < 2; ++i)
                if (ours[33 + i]) { r.Set(50); r.Set(51 + i); }
            for (int i = 0; i < 25; ++i)
                if (ours[36 + i]) { r.Set(53); r.Set(54 + i); }
            for (int i = 0; i < 19; ++i)
                if (ours[62 + i]) { r.Set(229); r.Set(230 + i); }
            for (int i = 0; i < 6; ++i)
                if (ours[82 + i]) { r.Set(249); r.Set(250 + i); }
            for (int i = 0; i < 19; ++i)
                if (ours[89 + i]) { r.Set(306); r.Set(307 + i); }
            for (int i = 0; i < 25; ++i) if (ours[36 + i]) { r.Set(0); r.Set(25); } // a quest log change resends QuestLogQuestIdToIndex
            return r;
        }

        public void WriteUpdate(WorldPacket data, UpdateMask ourChangesMask, bool ignoreNestedChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = ToRefMask(ourChangesMask);

            uint[] classicBlocks = ClassicUpdateMask.WriteHeader(data, changesMask, RefChangeMaskLength, 11, ClassicUpdateMask.PlayerDataBit, ClassicUpdateMask.PlayerDataGroups);
            bool classicGroup32 = (classicBlocks[1] & 1u) != 0;
            bool noQuestLogChangesMask = data.WriteBit(IsQuestLogChangesMaskSkipped());
            if (changesMask[0])
            {
                if (changesMask[3])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        Customizations.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(Customizations.Size(), data);
                    }
                }
                if (changesMask[6])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        ArenaCooldowns.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(ArenaCooldowns.Size(), data);
                    }
                }
                if (changesMask[8])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        VisualItemReplacements.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(VisualItemReplacements.Size(), data);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[3])
                {
                    for (int i = 0; i < Customizations.Size(); ++i)
                    {
                        if (Customizations.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            Customizations[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[6])
                {
                    for (int i = 0; i < ArenaCooldowns.Size(); ++i)
                    {
                        if (ArenaCooldowns.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            ArenaCooldowns[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[8])
                {
                    for (int i = 0; i < VisualItemReplacements.Size(); ++i)
                    {
                        if (VisualItemReplacements.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)VisualItemReplacements[i]);
                        }
                    }
                }
                if (changesMask[9])
                {
                    data.WritePackedGuid(DuelArbiter);
                }
                if (changesMask[10])
                {
                    data.WritePackedGuid(WowAccount);
                }
                if (changesMask[11])
                {
                    data.WritePackedGuid(BnetAccount);
                }
                if (changesMask[12])
                    data.WriteUInt64(GuildClubMemberID);
                if (changesMask[13])
                {
                    data.WritePackedGuid(LootTargetGUID);
                }
                if (changesMask[14])
                {
                    data.WriteUInt32((uint)PlayerFlags.GetValue());
                }
                if (changesMask[15])
                {
                    data.WriteUInt32((uint)PlayerFlagsEx.GetValue());
                }
                if (changesMask[16])
                {
                    data.WriteUInt32((uint)GuildRankID.GetValue());
                }
                if (changesMask[17])
                {
                    data.WriteUInt32((uint)GuildDeleteDate.GetValue());
                }
                if (changesMask[18])
                {
                    data.WriteInt32((int)GuildLevel.GetValue());
                }
                if (changesMask[19])
                {
                    data.WriteUInt8((byte)NativeSex.GetValue());
                }
                if (changesMask[20])
                {
                    data.WriteUInt8((byte)Inebriation.GetValue());
                }
                if (changesMask[21])
                {
                    data.WriteUInt8((byte)PvpTitle.GetValue());
                }
                if (changesMask[22])
                {
                    data.WriteUInt8((byte)ArenaFaction.GetValue());
                }
                if (changesMask[23])
                {
                    data.WriteUInt32((uint)DuelTeam.GetValue());
                }
                if (changesMask[24])
                {
                    data.WriteInt32((int)GuildTimeStamp.GetValue());
                }
                if (changesMask[25])
                {
                    WriteQuestLogQuestIdToIndexUpdate(data);
                }
                if (changesMask[26])
                {
                    data.WriteInt32((int)PlayerTitle.GetValue());
                }
                if (changesMask[27])
                {
                    data.WriteInt32((int)FakeInebriation.GetValue());
                }
                if (changesMask[28])
                {
                    data.WriteUInt32((uint)VirtualPlayerRealm.GetValue());
                }
                if (changesMask[29])
                {
                    data.WriteUInt32((uint)CurrentSpecID.GetValue());
                }
                if (changesMask[31])
                {
                    data.WriteInt32((int)TaxiMountAnimKitID.GetValue());
                }
            }
            if (changesMask[32])
            {
                if (changesMask[33])
                {
                    data.WriteUInt8((byte)CurrentBattlePetBreedQuality.GetValue());
                }
                if (changesMask[34])
                {
                    data.WriteInt32((int)HonorLevel.GetValue());
                }
                if (changesMask[35])
                {
                    data.WriteInt64((long)(long)LogoutTime.GetValue());
                }
                if (changesMask[39])
                {
                    data.WriteInt32((int)CurrentBattlePetSpeciesID.GetValue());
                }
                if (changesMask[43])
                {
                    DungeonScore.GetValue().Write(data);
                }
            }
            if (classicGroup32)
            {
                data.WriteBit(false);
            }
            data.FlushBits();
            if (changesMask[50])
            {
                for (int i = 0; i < 2; ++i)
                {
                    if (changesMask[51 + i])
                    {
                        data.WriteUInt8((byte)PartyType[i]);
                    }
                }
            }
            if (changesMask[53])
            {
                for (int i = 0; i < 175; ++i)
                {
                    if (changesMask[54 + i])
                    {
                        if (noQuestLogChangesMask)
                        {
                            QuestLog[i].WriteCreate(data, owner, receiver);
                        }
                        else
                        {
                            QuestLog[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
            }
            if (changesMask[229])
            {
                for (int i = 0; i < 19; ++i)
                {
                    if (changesMask[230 + i])
                    {
                        VisibleItems[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            if (changesMask[249])
            {
                for (int i = 0; i < 6; ++i)
                {
                    if (changesMask[250 + i])
                    {
                        data.WriteFloat((float)AvgItemLevel[i]);
                    }
                }
            }
            if (changesMask[306])
            {
                for (int i = 0; i < 19; ++i)
                {
                    if (changesMask[307 + i])
                    {
                        data.WriteUInt32((uint)Field_3120[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Customizations);
            ClearChangesMask(ArenaCooldowns);
            ClearChangesMask(VisualItemReplacements);
            ClearChangesMask(DuelArbiter);
            ClearChangesMask(WowAccount);
            ClearChangesMask(LootTargetGUID);
            ClearChangesMask(PlayerFlags);
            ClearChangesMask(PlayerFlagsEx);

            ClearChangesMask(GuildRankID);
            ClearChangesMask(GuildClubMemberID);
            ClearChangesMask(GuildDeleteDate);
            ClearChangesMask(GuildLevel);
            ClearChangesMask(NumBankSlots);
            ClearChangesMask(NativeSex);
            ClearChangesMask(Inebriation);
            ClearChangesMask(PvpTitle);

            ClearChangesMask(ArenaFaction);
            ClearChangesMask(PvpRank);
            ClearChangesMask(Field_88);
            ClearChangesMask(DuelTeam);
            ClearChangesMask(GuildTimeStamp);
            ClearChangesMask(PlayerTitle);
            ClearChangesMask(FakeInebriation);

            ClearChangesMask(VirtualPlayerRealm);
            ClearChangesMask(CurrentSpecID);
            ClearChangesMask(TaxiMountAnimKitID);
            ClearChangesMask(CurrentBattlePetBreedQuality);
            ClearChangesMask(HonorLevel);
            ClearChangesMask(LogoutTime);
            ClearChangesMask(CurrentBattlePetSpeciesID);

            ClearChangesMask(BnetAccount);
            ClearChangesMask(DungeonScore);
            ClearChangesMask(PartyType);
            ClearChangesMask(QuestLog);
            ClearChangesMask(VisibleItems);
            ClearChangesMask(AvgItemLevel);
            ClearChangesMask(Field_3120);
            _changesMask.ResetAll();
        }

        bool IsQuestLogChangesMaskSkipped() { return false; } // bandwidth savings aren't worth the cpu time
    }

    public class SkillInfo : HasChangesMask
    {
        public UpdateFieldArray<ushort> SkillLineID = new(256, 0, 1);
        public UpdateFieldArray<ushort> SkillStep = new(256, 0, 257);
        public UpdateFieldArray<ushort> SkillRank = new(256, 0, 513);
        public UpdateFieldArray<ushort> SkillStartingRank = new(256, 0, 769);
        public UpdateFieldArray<ushort> SkillMaxRank = new(256, 0, 1025);
        public UpdateFieldArray<short> SkillTempBonus = new(256, 0, 1281);
        public UpdateFieldArray<ushort> SkillPermBonus = new(256, 0, 1537);
        static int changeMaskLength = 1793;

        public SkillInfo() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            for (int i = 0; i < 256; ++i)
            {
                data.WriteUInt16(SkillLineID[i]);
                data.WriteUInt16(SkillStep[i]);
                data.WriteUInt16(SkillRank[i]);
                data.WriteUInt16(SkillStartingRank[i]);
                data.WriteUInt16(SkillMaxRank[i]);
                data.WriteInt16(SkillTempBonus[i]);
                data.WriteUInt16(SkillPermBonus[i]);
            }
        }

        // Generated from TrinityCore-forever UF::SkillInfo::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 2101;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            for (int i = 0; i < 256; ++i)
                if (ours[1 + i]) { r.Set(0); r.Set(1 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[257 + i]) { r.Set(0); r.Set(301 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[513 + i]) { r.Set(0); r.Set(601 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[769 + i]) { r.Set(0); r.Set(901 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[1025 + i]) { r.Set(0); r.Set(1201 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[1281 + i]) { r.Set(0); r.Set(1501 + i); }
            for (int i = 0; i < 256; ++i)
                if (ours[1537 + i]) { r.Set(0); r.Set(1801 + i); }
            return r;
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask source = new(changeMaskLength);
            if (ignoreChangesMask)
                source.SetAll();
            else
                source.OR(_changesMask);
            UpdateMask changesMask = ToRefMask(source);

            for (int i = 0; i < 2; ++i)
            {
                data.WriteUInt32(changesMask.GetBlocksMask((uint)i));
            }
            data.WriteBits(changesMask.GetBlocksMask((uint)2), 2);
            for (int i = 0; i < 66; ++i)
            {
                if (changesMask.GetBlock((uint)i) != 0)
                {
                    data.WriteBits(changesMask.GetBlock((uint)i), 32);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                for (int i = 0; i < 300; ++i)
                {
                    if (changesMask[1 + i])
                    {
                        data.WriteUInt16((ushort)SkillLineID[i]);
                    }
                    if (changesMask[301 + i])
                    {
                        data.WriteUInt16((ushort)SkillStep[i]);
                    }
                    if (changesMask[601 + i])
                    {
                        data.WriteUInt16((ushort)SkillRank[i]);
                    }
                    if (changesMask[901 + i])
                    {
                        data.WriteUInt16((ushort)SkillStartingRank[i]);
                    }
                    if (changesMask[1201 + i])
                    {
                        data.WriteUInt16((ushort)SkillMaxRank[i]);
                    }
                    if (changesMask[1501 + i])
                    {
                        data.WriteInt16((short)SkillTempBonus[i]);
                    }
                    if (changesMask[1801 + i])
                    {
                        data.WriteUInt16((ushort)SkillPermBonus[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(SkillLineID);
            ClearChangesMask(SkillStep);
            ClearChangesMask(SkillRank);
            ClearChangesMask(SkillStartingRank);
            ClearChangesMask(SkillMaxRank);
            ClearChangesMask(SkillTempBonus);
            ClearChangesMask(SkillPermBonus);
            _changesMask.ResetAll();
        }
    }

    public class RestInfo : HasChangesMask
    {
        public UpdateField<int> Threshold = new(0, 1);
        public UpdateField<byte> StateID = new(0, 2);
        static int changeMaskLength = 3;

        public RestInfo() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(Threshold);
            data.WriteUInt8(StateID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt32(Threshold);
                }
                if (changesMask[2])
                {
                    data.WriteUInt8(StateID);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Threshold);
            ClearChangesMask(StateID);
            _changesMask.ResetAll();
        }
    }

    public class PVPInfo : HasChangesMask
    {
        public UpdateField<bool> Disqualified = new(0, 1);
        public UpdateField<sbyte> Bracket = new(0, 2);
        public UpdateField<int> PvpRatingID = new(0, 3);
        public UpdateField<int> WeeklyPlayed = new(0, 4);
        public UpdateField<int> WeeklyWon = new(0, 5);
        public UpdateField<int> SeasonPlayed = new(0, 6);
        public UpdateField<int> SeasonWon = new(0, 7);
        public UpdateField<int> Rating = new(0, 8);
        public UpdateField<int> WeeklyBestRating = new(0, 9);
        public UpdateField<int> SeasonBestRating = new(0, 10);
        public UpdateField<int> PvpTierID = new(0, 11);
        public UpdateField<int> WeeklyBestWinPvpTierID = new(0, 12);
        public UpdateField<uint> Field_28 = new(0, 13);
        public UpdateField<uint> Field_2C = new(0, 14);
        public UpdateField<int> WeeklyRoundsPlayed = new(0, 15);
        public UpdateField<int> WeeklyRoundsWon = new(0, 16);
        public UpdateField<int> SeasonRoundsPlayed = new(0, 17);
        public UpdateField<int> SeasonRoundsWon = new(0, 18);
        static int changeMaskLength = 19;

        public PVPInfo() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt8(Bracket);
            data.WriteInt32(PvpRatingID);
            data.WriteInt32(WeeklyPlayed);
            data.WriteInt32(WeeklyWon);
            data.WriteInt32(SeasonPlayed);
            data.WriteInt32(SeasonWon);
            data.WriteInt32(Rating);
            data.WriteInt32(WeeklyBestRating);
            data.WriteInt32(SeasonBestRating);
            data.WriteInt32(PvpTierID);
            data.WriteInt32(WeeklyBestWinPvpTierID);
            data.WriteUInt32(Field_28);
            data.WriteUInt32(Field_2C);
            data.WriteInt32(WeeklyRoundsPlayed);
            data.WriteInt32(WeeklyRoundsWon);
            data.WriteInt32(SeasonRoundsPlayed);
            data.WriteInt32(SeasonRoundsWon);
            data.WriteBit(Disqualified);
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteBit(Disqualified);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[2])
                {
                    data.WriteInt8(Bracket);
                }
                if (changesMask[3])
                {
                    data.WriteInt32(PvpRatingID);
                }
                if (changesMask[4])
                {
                    data.WriteInt32(WeeklyPlayed);
                }
                if (changesMask[5])
                {
                    data.WriteInt32(WeeklyWon);
                }
                if (changesMask[6])
                {
                    data.WriteInt32(SeasonPlayed);
                }
                if (changesMask[7])
                {
                    data.WriteInt32(SeasonWon);
                }
                if (changesMask[8])
                {
                    data.WriteInt32(Rating);
                }
                if (changesMask[9])
                {
                    data.WriteInt32(WeeklyBestRating);
                }
                if (changesMask[10])
                {
                    data.WriteInt32(SeasonBestRating);
                }
                if (changesMask[11])
                {
                    data.WriteInt32(PvpTierID);
                }
                if (changesMask[12])
                {
                    data.WriteInt32(WeeklyBestWinPvpTierID);
                }
                if (changesMask[13])
                {
                    data.WriteUInt32(Field_28);
                }
                if (changesMask[14])
                {
                    data.WriteUInt32(Field_2C);
                }
                if (changesMask[15])
                {
                    data.WriteInt32(WeeklyRoundsPlayed);
                }
                if (changesMask[16])
                {
                    data.WriteInt32(WeeklyRoundsWon);
                }
                if (changesMask[17])
                {
                    data.WriteInt32(SeasonRoundsPlayed);
                }
                if (changesMask[18])
                {
                    data.WriteInt32(SeasonRoundsWon);
                }
            }
            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Disqualified);
            ClearChangesMask(Bracket);
            ClearChangesMask(PvpRatingID);
            ClearChangesMask(WeeklyPlayed);
            ClearChangesMask(WeeklyWon);
            ClearChangesMask(SeasonPlayed);
            ClearChangesMask(SeasonWon);
            ClearChangesMask(Rating);
            ClearChangesMask(WeeklyBestRating);
            ClearChangesMask(SeasonBestRating);
            ClearChangesMask(PvpTierID);
            ClearChangesMask(WeeklyBestWinPvpTierID);
            ClearChangesMask(Field_28);
            ClearChangesMask(Field_2C);
            ClearChangesMask(WeeklyRoundsPlayed);
            ClearChangesMask(WeeklyRoundsWon);
            ClearChangesMask(SeasonRoundsPlayed);
            ClearChangesMask(SeasonRoundsWon);
            _changesMask.ResetAll();
        }
    }

    public class CharacterRestriction
    {
        public int Field_0;
        public int Field_4;
        public int Field_8;
        public uint Type;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(Field_0);
            data.WriteInt32(Field_4);
            data.WriteInt32(Field_8);
            data.WriteBits(Type, 5);
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(Field_0);
            data.WriteInt32(Field_4);
            data.WriteInt32(Field_8);
            data.WriteBits(Type, 5);
            data.FlushBits();
        }
    }

    public class SpellPctModByLabel
    {
        public int ModIndex;
        public float ModifierValue;
        public int LabelID;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(ModIndex);
            data.WriteFloat(ModifierValue);
            data.WriteInt32(LabelID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(ModIndex);
            data.WriteFloat(ModifierValue);
            data.WriteInt32(LabelID);
        }

    }

    public class SpellFlatModByLabel
    {
        public int ModIndex;
        public int ModifierValue;
        public int LabelID;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(ModIndex);
            data.WriteInt32(ModifierValue);
            data.WriteInt32(LabelID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(ModIndex);
            data.WriteInt32(ModifierValue);
            data.WriteInt32(LabelID);
        }
    }

    public class CompletedProject : HasChangesMask
    {
        public UpdateField<uint> ProjectID = new(0, 1);
        public UpdateField<long> FirstCompleted = new(0, 2);
        public UpdateField<uint> CompletionCount = new(0, 3);
        static int changeMaskLength = 4;

        public CompletedProject() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteUInt32(ProjectID);
            data.WriteInt64(FirstCompleted);
            data.WriteUInt32(CompletionCount);
        }

        // Generated from TrinityCore-forever UF::CompletedProject::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 4;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[2]) { r.Set(0); r.Set(1); }
            if (ours[1]) { r.Set(0); r.Set(2); }
            if (ours[3]) { r.Set(0); r.Set(3); }
            return r;
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask source = new(changeMaskLength);
            if (ignoreChangesMask)
                source.SetAll();
            else
                source.OR(_changesMask);
            UpdateMask changesMask = ToRefMask(source);

            data.WriteBits(changesMask.GetBlock(0), 4);
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteInt64((long)FirstCompleted.GetValue());
                }
                if (changesMask[2])
                {
                    data.WriteUInt32((uint)ProjectID.GetValue());
                }
                if (changesMask[3])
                {
                    data.WriteUInt32((uint)CompletionCount.GetValue());
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ProjectID);
            ClearChangesMask(FirstCompleted);
            ClearChangesMask(CompletionCount);
            _changesMask.ResetAll();
        }
    }

    public class ResearchHistory : HasChangesMask
    {
        public DynamicUpdateField<CompletedProject> CompletedProjects = new(0, 1);
        static int changeMaskLength = 2;

        public ResearchHistory() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(CompletedProjects.Size());
            for (int i = 0; i < CompletedProjects.Size(); ++i)
            {
                CompletedProjects[i].WriteCreate(data, owner, receiver);
            }
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    if (!ignoreChangesMask)
                        CompletedProjects.WriteUpdateMask(data);
                    else
                        WriteCompleteDynamicFieldUpdateMask(CompletedProjects.Size(), data);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    for (int i = 0; i < CompletedProjects.Size(); ++i)
                    {
                        if (CompletedProjects.HasChanged(i) || ignoreChangesMask)
                        {
                            CompletedProjects[i].WriteUpdate(data, ignoreChangesMask, owner, receiver);
                        }
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(CompletedProjects);
            _changesMask.ResetAll();
        }
    }

    public class TraitEntry : IEquatable<TraitEntry>
    {
        public int TraitNodeID;
        public int TraitNodeEntryID;
        public int Rank;
        public int GrantedRanks;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(TraitNodeID);
            data.WriteInt32(TraitNodeEntryID);
            data.WriteInt32(Rank);
            data.WriteInt32(GrantedRanks);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(TraitNodeID);
            data.WriteInt32(TraitNodeEntryID);
            data.WriteInt32(Rank);
            data.WriteInt32(GrantedRanks);
        }

        public bool Equals(TraitEntry right)
        {
            return TraitNodeID == right.TraitNodeID
                && TraitNodeEntryID == right.TraitNodeEntryID
                && Rank == right.Rank
                && GrantedRanks == right.GrantedRanks;
        }
    }

    public class TraitConfig : HasChangesMask
    {
        public DynamicUpdateField<TraitEntry> Entries = new(0, 1);
        public UpdateField<int> ID = new(0, 2);
        public UpdateFieldString Name = new(0, 3);
        public UpdateField<int> Type = new(4, 5);
        public UpdateField<int> SkillLineID = new(4, 6);
        public UpdateField<int> ChrSpecializationID = new(4, 7);
        public UpdateField<int> CombatConfigFlags = new(8, 9);
        public UpdateField<int> LocalIdentifier = new(8, 10);
        public UpdateField<int> TraitSystemID = new(8, 11);
        static int changeMaskLength = 12;

        public TraitConfig() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(ID);
            data.WriteInt32(Type);
            data.WriteInt32(Entries.Size());
            if (Type == 2)
            {
                data.WriteInt32(SkillLineID);
            }
            if (Type == 1)
            {
                data.WriteInt32(ChrSpecializationID);
                data.WriteInt32(CombatConfigFlags);
                data.WriteInt32(LocalIdentifier);
            }
            if (Type == 3)
            {
                data.WriteInt32(TraitSystemID);
            }
            for (int i = 0; i < Entries.Size(); ++i)
            {
                Entries[i].WriteCreate(data, owner, receiver);
            }
            data.WriteBits(Name.GetValue().GetByteCount(), 9);
            data.WriteString(Name);
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    if (!ignoreChangesMask)
                        Entries.WriteUpdateMask(data);
                    else
                        WriteCompleteDynamicFieldUpdateMask(Entries.Size(), data);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    for (int i = 0; i < Entries.Size(); ++i)
                    {
                        if (Entries.HasChanged(i) || ignoreChangesMask)
                        {
                            Entries[i].WriteUpdate(data, ignoreChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[2])
                {
                    data.WriteInt32(ID);
                }
            }
            if (changesMask[4])
            {
                if (changesMask[5])
                {
                    data.WriteInt32(Type);
                }
                if (changesMask[6])
                {
                    if (Type == 2)
                    {
                        data.WriteInt32(SkillLineID);
                    }
                }
                if (changesMask[7])
                {
                    if (Type == 1)
                    {
                        data.WriteInt32(ChrSpecializationID);
                    }
                }
            }
            if (changesMask[8])
            {
                if (changesMask[9])
                {
                    if (Type == 1)
                    {
                        data.WriteInt32(CombatConfigFlags);
                    }
                }
                if (changesMask[10])
                {
                    if (Type == 1)
                    {
                        data.WriteInt32(LocalIdentifier);
                    }
                }
                if (changesMask[11])
                {
                    if (Type == 3)
                    {
                        data.WriteInt32(TraitSystemID);
                    }
                }
            }
            if (changesMask[0])
            {
                if (changesMask[3])
                {
                    data.WriteBits(Name.GetValue().GetByteCount(), 9);
                    data.WriteString(Name);
                }
            }
            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Entries);
            ClearChangesMask(ID);
            ClearChangesMask(Name);
            ClearChangesMask(Type);
            ClearChangesMask(SkillLineID);
            ClearChangesMask(ChrSpecializationID);
            ClearChangesMask(CombatConfigFlags);
            ClearChangesMask(LocalIdentifier);
            ClearChangesMask(TraitSystemID);
            _changesMask.ResetAll();
        }
    }

    public struct CategoryCooldownMod
    {
        public int SpellCategoryID;
        public int ModCooldown;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(SpellCategoryID);
            data.WriteInt32(ModCooldown);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(SpellCategoryID);
            data.WriteInt32(ModCooldown);
        }
    }

    public struct WeeklySpellUse
    {
        public int SpellCategoryID;
        public byte Uses;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(SpellCategoryID);
            data.WriteUInt8(Uses);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt32(SpellCategoryID);
            data.WriteUInt8(Uses);
        }
    }

    public class StablePetInfo : HasChangesMask
    {
        public UpdateField<int> PetSlot = new(0, 1);
        public UpdateField<int> PetNumber = new(0, 2);
        public UpdateField<int> CreatureID = new(0, 3);
        public UpdateField<int> DisplayID = new(0, 4);
        public UpdateField<int> ExperienceLevel = new(0, 5);
        public UpdateFieldString Name = new(0, 6);
        public UpdateField<byte> PetFlags = new(0, 7);
        public UpdateField<byte> LoyaltyLevel = new(0, 8);
        public UpdateField<uint> LoyaltyPoints = new(0, 9);
        public UpdateField<uint> Happiness = new(0, 10);
        public UpdateField<uint> Experience = new(0, 11);
        public UpdateField<uint> NextLevelExperience = new(0, 12);
        static int changeMaskLength = 13;

        public StablePetInfo() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(PetSlot);
            data.WriteInt32(PetNumber);
            data.WriteInt32(CreatureID);
            data.WriteInt32(DisplayID);
            data.WriteInt32(ExperienceLevel);
            data.WriteUInt8(PetFlags);
            data.WriteUInt8(LoyaltyLevel);
            data.WriteUInt32(LoyaltyPoints);
            data.WriteUInt32(Happiness);
            data.WriteUInt32(Experience);
            data.WriteUInt32(NextLevelExperience);
            data.WriteBits(Name.GetValue().GetByteCount(), 8);
            data.FlushBits();
            data.WriteString(Name);
        }

        // Generated from TrinityCore-forever UF::StablePetInfo::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 13;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[1]) { r.Set(0); r.Set(1); }
            if (ours[2]) { r.Set(0); r.Set(2); }
            if (ours[3]) { r.Set(0); r.Set(3); }
            if (ours[4]) { r.Set(0); r.Set(4); }
            if (ours[5]) { r.Set(0); r.Set(5); }
            if (ours[6]) { r.Set(0); r.Set(6); }
            if (ours[7]) { r.Set(0); r.Set(7); }
            if (ours[8]) { r.Set(0); r.Set(8); }
            if (ours[9]) { r.Set(0); r.Set(9); }
            if (ours[10]) { r.Set(0); r.Set(10); }
            if (ours[11]) { r.Set(0); r.Set(11); }
            if (ours[12]) { r.Set(0); r.Set(12); }
            return r;
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask source = new(changeMaskLength);
            if (ignoreChangesMask)
                source.SetAll();
            else
                source.OR(_changesMask);
            UpdateMask changesMask = ToRefMask(source);

            data.WriteBits(changesMask.GetBlock(0), 13);
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteUInt32((uint)PetSlot.GetValue());
                }
                if (changesMask[2])
                {
                    data.WriteUInt32((uint)PetNumber.GetValue());
                }
                if (changesMask[3])
                {
                    data.WriteUInt32((uint)CreatureID.GetValue());
                }
                if (changesMask[4])
                {
                    data.WriteUInt32((uint)DisplayID.GetValue());
                }
                if (changesMask[5])
                {
                    data.WriteUInt32((uint)ExperienceLevel.GetValue());
                }
                if (changesMask[7])
                {
                    data.WriteUInt8((byte)PetFlags.GetValue());
                }
                if (changesMask[8])
                {
                    data.WriteUInt8((byte)LoyaltyLevel.GetValue());
                }
                if (changesMask[9])
                {
                    data.WriteUInt32((uint)LoyaltyPoints.GetValue());
                }
                if (changesMask[10])
                {
                    data.WriteUInt32((uint)Happiness.GetValue());
                }
                if (changesMask[11])
                {
                    data.WriteUInt32((uint)Experience.GetValue());
                }
                if (changesMask[12])
                {
                    data.WriteUInt32((uint)NextLevelExperience.GetValue());
                }
                if (changesMask[6])
                {
                    data.WriteBits(Name.GetValue().GetByteCount(), 8);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[6])
                {
                    data.WriteString(Name.GetValue());
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(PetSlot);
            ClearChangesMask(PetNumber);
            ClearChangesMask(CreatureID);
            ClearChangesMask(DisplayID);
            ClearChangesMask(ExperienceLevel);
            ClearChangesMask(Name);
            ClearChangesMask(PetFlags);
            ClearChangesMask(LoyaltyLevel);
            ClearChangesMask(LoyaltyPoints);
            ClearChangesMask(Happiness);
            ClearChangesMask(Experience);
            ClearChangesMask(NextLevelExperience);
            _changesMask.ResetAll();
        }
    }

    public class StableInfo : HasChangesMask
    {
        public DynamicUpdateField<StablePetInfo> Pets = new(0, 1);
        public UpdateField<ObjectGuid> StableMaster = new(0, 2);
        static int changeMaskLength = 3;

        public StableInfo() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt32(Pets.Size());
            data.WritePackedGuid(StableMaster);
            for (int i = 0; i < Pets.Size(); ++i)
            {
                Pets[i].WriteCreate(data, owner, receiver);
            }
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    if (!ignoreChangesMask)
                        Pets.WriteUpdateMask(data);
                    else
                        WriteCompleteDynamicFieldUpdateMask(Pets.Size(), data);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    for (int i = 0; i < Pets.Size(); ++i)
                    {
                        if (Pets.HasChanged(i) || ignoreChangesMask)
                        {
                            Pets[i].WriteUpdate(data, ignoreChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[2])
                {
                    data.WritePackedGuid(StableMaster);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Pets);
            ClearChangesMask(StableMaster);
            _changesMask.ResetAll();
        }
    }    

    public struct Research
    {
        public short ResearchProjectID;

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteInt16(ResearchProjectID);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            data.WriteInt16(ResearchProjectID);
        }
    }

    public class BankTabSettings : HasChangesMask
    {
        public UpdateFieldString Name = new(0, 0);
        public UpdateFieldString Icon = new(1, 1);
        public UpdateFieldString Description = new(2, 2);
        public UpdateField<int> DepositFlags = new(3, 3);
        public BankTabSettings() : base(4) { }

        public void WriteCreate(WorldPacket data, Player owner, Player receiver)
        {
            data.WriteBits(Name.GetValue().GetByteCount(), 7);
            data.WriteBits(Icon.GetValue().GetByteCount(), 9);
            data.WriteBits(Description.GetValue().GetByteCount(), 14);
            data.WriteInt32(DepositFlags);
            data.WriteString(Name); data.WriteString(Icon); data.WriteString(Description);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
        {
            UpdateMask mask = new(4);
            for (int i = 0; i < 4; ++i)
                if (ignoreChangesMask || _changesMask[i]) mask.Set(i);
            data.WriteBits(mask.GetBlock(0), 4);
            if (mask[0]) data.WriteBits(Name.GetValue().GetByteCount(), 7);
            if (mask[1]) data.WriteBits(Icon.GetValue().GetByteCount(), 9);
            if (mask[2]) data.WriteBits(Description.GetValue().GetByteCount(), 14);
            data.FlushBits();
            if (mask[3]) data.WriteInt32(DepositFlags);
            if (mask[0]) data.WriteString(Name);
            if (mask[1]) data.WriteString(Icon);
            if (mask[2]) data.WriteString(Description);
        }

        public override void ClearChangesMask() { _changesMask.ResetAll(); }
    }

    public class ActivePlayerData : HasChangesMask
    {        
        public UpdateField<bool> SortBagsRightToLeft = new(0, 1);
        public UpdateField<bool> InsertItemsLeftToRight = new(0, 2);
        public UpdateFieldArray<DynamicUpdateField<ushort>> ResearchSites = new(1, 20, 21);
        public UpdateFieldArray<DynamicUpdateField<uint>> ResearchSiteProgress = new(1, 22, 23);
        public UpdateFieldArray<DynamicUpdateField<Research>> Research = new(1, 24, 25);
        public DynamicUpdateField<ulong> KnownTitles = new(0, 3);
        public DynamicUpdateField<int> DailyQuestsCompleted = new(0, 4);
        public DynamicUpdateField<int> AvailableQuestLineXQuestIDs = new(0, 5);
        public DynamicUpdateField<int> Field_1000 = new(0, 6);
        public DynamicUpdateField<int> Heirlooms = new(0, 7);
        public DynamicUpdateField<uint> HeirloomFlags = new(0, 8);
        public DynamicUpdateField<int> Toys = new(0, 9);
        public DynamicUpdateField<int> Transmog = new(0, 10);
        public DynamicUpdateField<int> ConditionalTransmog = new(0, 11);
        public DynamicUpdateField<int> SelfResSpells = new(0, 12);
        public DynamicUpdateField<SpellPctModByLabel> SpellPctModByLabel = new(0, 14);
        public DynamicUpdateField<SpellFlatModByLabel> SpellFlatModByLabel = new(0, 15);
        public DynamicUpdateField<QuestLog> TaskQuests = new(0, 16);
        public DynamicUpdateField<CategoryCooldownMod> CategoryCooldownMods = new(0, 18);
        public DynamicUpdateField<WeeklySpellUse> WeeklySpellUses = new(0, 19);
        public DynamicUpdateField<CharacterRestriction> CharacterRestrictions = new(0, 13);
        public DynamicUpdateField<TraitConfig> TraitConfigs = new(0, 17);
        public UpdateField<ObjectGuid> FarsightObject = new(0, 26);
        public UpdateField<ObjectGuid> SummonedBattlePetGUID = new(0, 27);
        public UpdateField<long> Coinage = new(0, 28);
        public UpdateField<int> XP = new(0, 29);
        public UpdateField<int> NextLevelXP = new(0, 30);
        public UpdateField<int> TrialXP = new(0, 31);
        public UpdateField<SkillInfo> Skill = new(0, 32);
        public UpdateField<int> CharacterPoints = new(0, 33);
        public UpdateField<int> MaxTalentTiers = new(0, 34);
        public UpdateField<uint> TrackCreatureMask = new(0, 35);
        public UpdateField<float> MainhandExpertise = new(0, 36);
        public UpdateField<float> OffhandExpertise = new(0, 37);
        public UpdateField<float> RangedExpertise = new(38, 39);
        public UpdateField<float> CombatRatingExpertise = new(38, 40);
        public UpdateField<float> BlockPercentage = new(38, 41);
        public UpdateField<float> DodgePercentage = new(38, 42);
        public UpdateField<float> DodgePercentageFromAttribute = new(38, 43);
        public UpdateField<float> ParryPercentage = new(38, 44);
        public UpdateField<float> ParryPercentageFromAttribute = new(38, 45);
        public UpdateField<float> CritPercentage = new(38, 46);
        public UpdateField<float> RangedCritPercentage = new(38, 47);
        public UpdateField<float> OffhandCritPercentage = new(38, 48);
        public UpdateField<int> ShieldBlock = new(38, 49);
        public UpdateField<float> ShieldBlockCritPercentage = new(38, 50);
        public UpdateField<float> Mastery = new(38, 51);
        public UpdateField<float> Speed = new(38, 52);
        public UpdateField<float> Avoidance = new(38, 53);
        public UpdateField<float> Sturdiness = new(38, 54);
        public UpdateField<int> Versatility = new(38, 55);
        public UpdateField<float> VersatilityBonus = new(38, 56);
        public UpdateField<float> PvpPowerDamage = new(38, 57);
        public UpdateField<float> PvpPowerHealing = new(38, 58);
        public UpdateField<int> ModHealingDonePos = new(38, 59);
        public UpdateField<float> ModHealingPercent = new(38, 60);
        public UpdateField<float> ModHealingDonePercent = new(38, 61);
        public UpdateField<float> ModPeriodicHealingDonePercent = new(38, 62);
        public UpdateField<float> ModSpellPowerPercent = new(38, 63);
        public UpdateField<float> ModResiliencePercent = new(38, 64);
        public UpdateField<float> OverrideSpellPowerByAPPercent = new(38, 65);
        public UpdateField<float> OverrideAPBySpellPowerPercent = new(38, 66);
        public UpdateField<int> ModTargetResistance = new(38, 67);
        public UpdateField<int> ModTargetPhysicalResistance = new(38, 68);
        public UpdateField<uint> LocalFlags = new(38, 69);
        public UpdateField<byte> GrantableLevels = new(70, 71);
        public UpdateField<byte> MultiActionBars = new(70, 72);
        public UpdateField<byte> LifetimeMaxRank = new(70, 73);
        public UpdateField<byte> NumRespecs = new(70, 74);
        public UpdateField<int> AmmoID = new(70, 75);
        public UpdateField<uint> PvpMedals = new(70, 76);
        public UpdateField<ushort> TodayHonorableKills = new(70, 77);
        public UpdateField<ushort> TodayDishonorableKills = new(70, 78);
        public UpdateField<ushort> YesterdayHonorableKills = new(70, 79);
        public UpdateField<ushort> YesterdayDishonorableKills = new(70, 80);
        public UpdateField<ushort> LastWeekHonorableKills = new(70, 81);
        public UpdateField<ushort> LastWeekDishonorableKills = new(70, 82);
        public UpdateField<ushort> ThisWeekHonorableKills = new(70, 83);
        public UpdateField<ushort> ThisWeekDishonorableKills = new(70, 84);
        public UpdateField<uint> ThisWeekContribution = new(70, 85);
        public UpdateField<int> LifetimeHonorableKills = new(70, 86);
        public UpdateField<int> LifetimeDishonorableKills = new(70, 87);
        public UpdateField<uint> Field_F24 = new(70, 88);
        public UpdateField<uint> YesterdayContribution = new(70, 89);
        public UpdateField<uint> LastWeekContribution = new(70, 90);
        public UpdateField<uint> LastWeekRank = new(70, 91);
        public UpdateField<int> WatchedFactionIndex = new(70, 92);
        public UpdateField<int> MaxLevel = new(70, 93);
        public UpdateField<int> ScalingPlayerLevelDelta = new(70, 94);
        public UpdateField<int> MaxCreatureScalingLevel = new(70, 95);
        public UpdateField<int> PetSpellPower = new(70, 96);
        public UpdateField<float> UiHitModifier = new(70, 97);
        public UpdateField<float> UiSpellHitModifier = new(70, 98);
        public UpdateField<int> HomeRealmTimeOffset = new(70, 99);
        public UpdateField<float> ModPetHaste = new(70, 100);
        public UpdateField<byte> LocalRegenFlags = new(70, 101);
        public UpdateField<byte> AuraVision = new(102, 103);
        public UpdateField<byte> NumBackpackSlots = new(102, 104);
        public UpdateField<int> OverrideSpellsID = new(102, 105);
        public UpdateField<int> LfgBonusFactionID = new(102, 106);
        public UpdateField<ushort> LootSpecID = new(102, 107);
        public UpdateField<uint> OverrideZonePVPType = new(102, 108);
        public UpdateField<int> Honor = new(102, 109);
        public UpdateField<int> HonorNextLevel = new(102, 110);
        public UpdateField<int> Field_F74 = new(102, 111);
        public UpdateField<int> PvpTierMaxFromWins = new(102, 112);
        public UpdateField<int> PvpLastWeeksTierMaxFromWins = new(102, 113);
        public UpdateField<byte> PvpRankProgress = new(102, 114);
        public UpdateField<int> PerksProgramCurrency = new(102, 115);
        public UpdateField<ResearchHistory> ResearchHistory = new(102, 116);
        public UpdateField<PerksVendorItem> FrozenPerksVendorItem = new(102, 117);
        public UpdateField<RelativeTime> TransportServerTime = new(102, 118);
        public UpdateField<int> ActiveCombatTraitConfigID = new(102, 119);
        public UpdateField<byte> GlyphsEnabled = new(102, 120);
        public UpdateField<byte> LfgRoles = new(102, 121);
        public OptionalUpdateField<StableInfo> PetStable = new(102, 122);
        public UpdateField<byte> NumStableSlots = new(102, 123);
        public UpdateFieldArray<ObjectGuid> InvSlots = new(141, 124, 125);
        public UpdateFieldArray<uint> TrackResourceMask = new(2, 266, 267);
        public UpdateFieldArray<float> SpellCritPercentage = new(7, 269, 270);
        public UpdateFieldArray<int> ModDamageDonePos = new(7, 269, 277);
        public UpdateFieldArray<int> ModDamageDoneNeg = new(7, 269, 284);
        public UpdateFieldArray<float> ModDamageDonePercent = new(7, 269, 291);
        public UpdateFieldArray<ulong> ExploredZones = new(240, 298, 299);
        public UpdateFieldArray<RestInfo> RestInfo = new(2, 539, 540);
        public UpdateFieldArray<float> WeaponDmgMultipliers = new(3, 542, 543);
        public UpdateFieldArray<float> WeaponAtkSpeedMultipliers = new(3, 542, 546);
        public UpdateFieldArray<uint> BuybackPrice = new(12, 549, 550);
        public UpdateFieldArray<TimeSpan> BuybackTimestamp = new(12, 549, 562);
        public UpdateFieldArray<int> CombatRatings = new(32, 574, 575);
        public UpdateFieldArray<PVPInfo> PvpInfo = new(7, 607, 608);
        public UpdateFieldArray<uint> NoReagentCostMask = new(4, 615, 616);
        public UpdateFieldArray<int> ProfessionSkillLine = new(2, 620, 621);
        public UpdateFieldArray<uint> BagSlotFlags = new(4, 623, 624);
        public UpdateFieldArray<uint> BankBagSlotFlags = new(7, 628, 629);
        public UpdateFieldArray<ulong> QuestCompleted = new(875, 636, 637);
        public UpdateFieldArray<int> GlyphSlots = new(6, 1512, 1513);
        public UpdateFieldArray<int> Glyphs = new(6, 1512, 1519);
        public UpdateField<byte> NumCharacterBankTabs = new(1525, 1526);
        public DynamicUpdateField<BankTabSettings> CharacterBankTabSettings = new(1525, 1527);
        static int changeMaskLength = 1528;

        public static int ExploredZonesSize;
        public static int ExploredZonesBits;
        public static int QuestCompletedBitsSize;
        public static int QuestCompletedBitsPerBlock;

        public ActivePlayerData() : base(0, TypeId.ActivePlayer, changeMaskLength)
        {
            ExploredZonesSize = ExploredZones.GetSize();
            ExploredZonesBits = sizeof(ulong) * 8;

            QuestCompletedBitsSize = QuestCompleted.GetSize();
            QuestCompletedBitsPerBlock = sizeof(ulong) * 8;
        }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Player owner, Player receiver)
        {
            // Classic 1.60.1 ActivePlayerData create layout (TrinityCore-forever UF::ActivePlayerData::WriteCreate).
            // This core keeps the WotLK field model; fields it does not have are written with the reference defaults.
            for (int i = 0; i < ClassicInventorySlots.Count; ++i)
                data.WritePackedGuid(GetClassicInvSlot(i));
            data.WritePackedGuid(FarsightObject);
            data.WritePackedGuid(SummonedBattlePetGUID);
            data.WriteUInt32((uint)KnownTitles.Size());
            data.WriteUInt64((ulong)Coinage.GetValue());
            data.WriteUInt64(0); // AccountBankCoinage
            data.WriteInt32(XP);
            data.WriteInt32(NextLevelXP);
            data.WriteInt32(TrialXP);
            WriteClassicSkillInfo(data, Skill.GetValue());
            data.WriteInt32(CharacterPoints);
            data.WriteInt32(MaxTalentTiers);
            data.WriteUInt32(TrackCreatureMask);
            data.WriteFloat(MainhandExpertise);
            data.WriteFloat(OffhandExpertise);
            data.WriteFloat(RangedExpertise);
            data.WriteFloat(CombatRatingExpertise);
            data.WriteFloat(BlockPercentage);
            data.WriteFloat(DodgePercentage);
            data.WriteFloat(DodgePercentageFromAttribute);
            data.WriteFloat(ParryPercentage);
            data.WriteFloat(ParryPercentageFromAttribute);
            data.WriteFloat(CritPercentage);
            data.WriteFloat(RangedCritPercentage);
            data.WriteFloat(OffhandCritPercentage);
            data.WriteFloat(GetClassicSpellCritPercentage()); // retail/Classic has one value, this core one per school
            data.WriteInt32(ShieldBlock);
            data.WriteFloat(ShieldBlockCritPercentage);
            data.WriteFloat(Mastery);
            data.WriteFloat(Speed);
            data.WriteFloat(Avoidance);
            data.WriteFloat(Sturdiness);
            data.WriteInt32(Versatility);
            data.WriteFloat(VersatilityBonus);
            data.WriteFloat(PvpPowerDamage);
            data.WriteFloat(PvpPowerHealing);
            WriteClassicBitVectors(data);
            data.WriteUInt32(0); // CharacterDataElements
            data.WriteUInt32(0); // AccountDataElements
            for (int i = 0; i < 2; ++i)
                RestInfo[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < 7; ++i)
            {
                data.WriteInt32(ModDamageDonePos[i]);
                data.WriteInt32(ModDamageDoneNeg[i]);
                data.WriteFloat(ModDamageDonePercent[i]);
                data.WriteFloat(ModHealingDonePercent); // per school in Classic, a single value in this core
            }
            data.WriteInt32(ModHealingDonePos);
            data.WriteFloat(ModHealingPercent);
            data.WriteFloat(ModPeriodicHealingDonePercent);
            for (int i = 0; i < 3; ++i)
            {
                data.WriteFloat(WeaponDmgMultipliers[i]);
                data.WriteFloat(WeaponAtkSpeedMultipliers[i]);
            }
            data.WriteFloat(ModSpellPowerPercent);
            data.WriteFloat(ModResiliencePercent);
            data.WriteFloat(OverrideSpellPowerByAPPercent);
            data.WriteFloat(OverrideAPBySpellPowerPercent);
            data.WriteInt32(ModTargetResistance);
            data.WriteInt32(ModTargetPhysicalResistance);
            data.WriteUInt32(LocalFlags);
            data.WriteUInt8(GrantableLevels);
            data.WriteUInt8(MultiActionBars);
            data.WriteUInt8(LifetimeMaxRank);
            data.WriteUInt8(NumRespecs);
            data.WriteUInt32(PvpMedals);
            data.WriteUInt32(0); // Classic unknown uint32 (client ActivePlayerData +0x14F8)
            for (int i = 0; i < 12; ++i)
            {
                data.WriteUInt32(BuybackPrice[i]);
                data.WriteInt64(BuybackTimestamp[i].ToSeconds());
            }
            data.WriteUInt16(TodayHonorableKills);
            data.WriteUInt16(YesterdayHonorableKills);
            data.WriteFloat(0.0f); // Classic unknown float (client ActivePlayerData +0x1500)
            data.WriteUInt32((uint)LifetimeHonorableKills.GetValue());
            data.WriteInt32(WatchedFactionIndex);
            for (int i = 0; i < 32; ++i)
                data.WriteInt32(CombatRatings[i]);
            data.WriteUInt32(0); // PvpInfo (the Classic client reads it as a dynamic list)
            data.WriteInt32(MaxLevel);
            data.WriteInt32(ScalingPlayerLevelDelta);
            data.WriteInt32(MaxCreatureScalingLevel);
            data.WriteUInt8(0); // TransmogCostMinScalingLevel
            for (int i = 0; i < 4; ++i)
                data.WriteUInt32(NoReagentCostMask[i]);
            // Classic has no PetSpellPower here
            for (int i = 0; i < 2; ++i)
                data.WriteInt32(ProfessionSkillLine[i]);
            data.WriteFloat(UiHitModifier);
            data.WriteFloat(UiSpellHitModifier);
            data.WriteFloat(0.0f); // Classic third float before HomeRealmTimeOffset (client +0x1560)
            data.WriteFloat(0.0f); // e9e5593: fourth float in builds 70425 and later
            data.WriteInt32(HomeRealmTimeOffset);
            data.WriteFloat(ModPetHaste);
            data.WriteInt8(0); // JailersTowerLevelMax
            data.WriteInt8(0); // JailersTowerLevel
            data.WriteUInt8(LocalRegenFlags);
            data.WriteUInt8(AuraVision);
            data.WriteUInt8(NumBackpackSlots);
            data.WriteInt32(OverrideSpellsID);
            data.WriteUInt16(LootSpecID);
            data.WriteUInt32(OverrideZonePVPType);
            for (int i = 0; i < 5; ++i)
                data.WriteUInt32(i < BagSlotFlags.GetSize() ? BagSlotFlags[i] : 0u);
            data.WriteInt32(Honor);
            data.WriteInt32(HonorNextLevel);
            data.WriteInt32(PerksProgramCurrency);
            data.WriteUInt8(owner.m_playerData.NumBankSlots); // NumBankSlots lives in PlayerData in this core
            data.WriteUInt8(NumCharacterBankTabs);
            data.WriteUInt8(0); // NumAccountBankTabs
            for (int i = 0; i < 1; ++i)
            {
                data.WriteUInt32((uint)ResearchSites[i].Size());
                data.WriteUInt32((uint)ResearchSiteProgress[i].Size());
                data.WriteUInt32((uint)Research[i].Size());
                for (int j = 0; j < ResearchSites[i].Size(); ++j)
                    data.WriteUInt16(ResearchSites[i][j]);
                for (int j = 0; j < ResearchSiteProgress[i].Size(); ++j)
                    data.WriteUInt32(ResearchSiteProgress[i][j]);
                for (int j = 0; j < Research[i].Size(); ++j)
                    Research[i][j].WriteCreate(data, owner, receiver);
            }
            data.WriteUInt32((uint)DailyQuestsCompleted.Size());
            data.WriteUInt32(0); // Field_1328
            data.WriteUInt32((uint)AvailableQuestLineXQuestIDs.Size());
            data.WriteUInt32((uint)Heirlooms.Size());
            data.WriteUInt32((uint)HeirloomFlags.Size());
            data.WriteUInt32((uint)Toys.Size());
            data.WriteUInt32(0); // ToyFlags
            data.WriteUInt32((uint)Transmog.Size());
            data.WriteUInt32((uint)ConditionalTransmog.Size());
            data.WriteUInt32((uint)SelfResSpells.Size());
            data.WriteUInt32(0); // RuneforgePowers
            data.WriteUInt32(0); // TransmogIllusions
            data.WriteUInt32(0); // WarbandScenes
            data.WriteUInt32(0); // HouseRooms
            data.WriteUInt32(0); // HouseExteriorComponents
            data.WriteUInt32(0); // HouseThemes
            data.WriteUInt32(0); // HouseRoomComponentTextures
            data.WriteUInt32(0); // HouseTypes
            data.WriteUInt32(0); // UnlockedTransmogOutfits
            data.WriteUInt32((uint)CharacterRestrictions.Size());
            data.WriteUInt32((uint)SpellPctModByLabel.Size());
            data.WriteUInt32((uint)SpellFlatModByLabel.Size());
            data.WriteUInt32(0); // SpellPctModPVPByLabel
            data.WriteUInt32(0); // SpellFlatModPVPByLabel
            // ResearchHistory
            var completedProjects = ResearchHistory.GetValue().CompletedProjects;
            data.WriteUInt32((uint)completedProjects.Size());
            for (int i = 0; i < completedProjects.Size(); ++i)
            {
                data.WriteInt64(completedProjects[i].FirstCompleted.GetValue());
                data.WriteUInt32(completedProjects[i].ProjectID);
                data.WriteUInt32(completedProjects[i].CompletionCount);
            }
            data.WriteUInt32(0); // Reserved client field
            data.WriteUInt32(0); // MultiFloorExploration
            data.WriteUInt32(0); // RecipeProgression
            WriteEmptyPerksVendorItem(data); // FrozenPerksVendorItem
            // DiscordInfo
            data.WriteBits(1, 24); // AccessToken size + 1 (empty string)
            data.WriteUInt64(0); // DiscordUserID
            data.WriteUInt8(0); // AccountType
            data.WriteUInt64(0); // GuildLobbyID
            data.WriteUInt8(0); // GuildSettings
            data.WriteUInt8(0); // DisplayNameType
            data.WritePackedGuid(ObjectGuid.Empty); // build 70291+
            // SizedCString::Data writes a null terminator only for non-empty strings.
            // AccessToken is empty here, so it contributes no bytes (size + 1 is still 1).
            data.WriteUInt32(0); // ReplayedQuests
            data.WriteUInt32((uint)TaskQuests.Size());
            // LevelLinkInfo
            data.WritePackedGuid(ObjectGuid.Empty);
            data.WriteInt32(0);
            data.WriteUInt32(0); // DisabledSpells
            data.WriteInt32(0); // UiChromieTimeExpansionID
            data.WriteInt32(0); // TimerunningSeasonID
            data.WriteInt32((int)(uint)TransportServerTime.GetValue());
            data.WriteUInt32(0); // WeeklyRewardsPeriodSinceOrigin
            data.WriteInt16(0); // DEBUGSoulbindConduitRank
            // DungeonScore (MythicPlus::DungeonScoreData, not PlayerData's DungeonScoreSummary): Seasons count, TotalRuns
            data.WriteUInt32(0);
            data.WriteInt32(0);
            // TraitConfigs map: count, then (ID, TraitConfig)
            data.WriteUInt32((uint)TraitConfigs.Size());
            for (int i = 0; i < TraitConfigs.Size(); ++i)
            {
                data.WriteInt32(TraitConfigs[i].ID);
                WriteClassicTraitConfig(data, TraitConfigs[i]);
            }
            data.WriteUInt32((uint)ActiveCombatTraitConfigID.GetValue());
            data.WriteUInt8(0); // Classic unknown uint8 after ActiveCombatTraitConfigID
            data.WriteUInt32(0); // CraftingOrders
            data.WriteUInt32(0); // PersonalCraftingOrderCounts
            data.WriteUInt32(0); // NpcCraftingOrders
            data.WriteUInt32((uint)CategoryCooldownMods.Size());
            data.WriteUInt32((uint)WeeklySpellUses.Size());
            for (int i = 0; i < 17; ++i)
                data.WriteFloat(0.0f); // ItemUpgradeHighWatermark
            data.WriteInt32(0); // ItemUpgradeHighOnehandWeaponItemID
            data.WriteInt32(0); // ItemUpgradeHighFingerItemID
            data.WriteFloat(0.0f); // ItemUpgradeHighFingerWatermark
            data.WriteInt32(0); // ItemUpgradeHighTrinketItemID
            data.WriteFloat(0.0f); // ItemUpgradeHighTrinketWatermark
            data.WriteUInt64(0); // LootHistoryInstanceID
            data.WriteUInt8(NumStableSlots); // Classic stable slots (client ActivePlayerData +0x1EA0)
            data.WriteUInt32(0); // TrackedCollectableSources
            data.WriteUInt8(0); // RequiredMountCapabilityFlags
            data.WriteUInt32(0); // DelveData map
            data.WriteUInt32(0); // TransmogOutfits map
            // ViewedOutfit (TransmogOutfitData)
            data.WriteUInt32(0); // Id
            data.WriteUInt8(0); // OutfitInfo.SetType
            data.WriteUInt32(0); // OutfitInfo.Icon
            data.WriteBits(0, 8); // OutfitInfo.Name
            data.WriteBit(false); // OutfitInfo.SituationsEnabled
            data.FlushBits();
            data.WriteUInt32(0); // Situations
            data.WriteUInt32(0); // Slots
            data.WriteUInt32(0); // Flags
            // TransmogMetadata
            data.WriteUInt8(0);
            data.WriteUInt32(0);
            data.WriteUInt8(0);
            data.WriteUInt8(0);
            data.WriteFloat(0.0f);
            data.WriteBit(false);
            data.FlushBits();
            data.FlushBits();

            for (int i = 0; i < KnownTitles.Size(); ++i)
                data.WriteUInt64(KnownTitles[i]);
            for (int i = 0; i < DailyQuestsCompleted.Size(); ++i)
                data.WriteInt32(DailyQuestsCompleted[i]);
            for (int i = 0; i < AvailableQuestLineXQuestIDs.Size(); ++i)
                data.WriteInt32(AvailableQuestLineXQuestIDs[i]);
            for (int i = 0; i < Heirlooms.Size(); ++i)
                data.WriteInt32(Heirlooms[i]);
            for (int i = 0; i < HeirloomFlags.Size(); ++i)
                data.WriteUInt32(HeirloomFlags[i]);
            for (int i = 0; i < Toys.Size(); ++i)
                data.WriteInt32(Toys[i]);
            for (int i = 0; i < Transmog.Size(); ++i)
                data.WriteUInt32((uint)Transmog[i]);
            for (int i = 0; i < ConditionalTransmog.Size(); ++i)
                data.WriteInt32(ConditionalTransmog[i]);
            for (int i = 0; i < SelfResSpells.Size(); ++i)
                data.WriteInt32(SelfResSpells[i]);
            for (int i = 0; i < CharacterRestrictions.Size(); ++i)
                CharacterRestrictions[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < SpellPctModByLabel.Size(); ++i)
                SpellPctModByLabel[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < SpellFlatModByLabel.Size(); ++i)
                SpellFlatModByLabel[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < TaskQuests.Size(); ++i)
                TaskQuests[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < CategoryCooldownMods.Size(); ++i)
                CategoryCooldownMods[i].WriteCreate(data, owner, receiver);
            for (int i = 0; i < WeeklySpellUses.Size(); ++i)
                WeeklySpellUses[i].WriteCreate(data, owner, receiver);

            data.WriteBit(false); // BackpackAutoSortDisabled
            data.WriteBit(false); // BackpackSellJunkDisabled
            data.WriteBit(false); // BankAutoSortDisabled
            data.WriteBit(SortBagsRightToLeft);
            data.WriteBit(InsertItemsLeftToRight);
            data.WriteBit(false); // HasPerksProgramPendingReward
            data.WriteBit(false); // QuestSession
            data.WriteBit(PetStable.HasValue());
            data.WriteBits(CharacterBankTabSettings.Size(), 4);
            data.WriteBits(0, 3); // AccountBankTabSettings
            data.WriteBit(false); // WalkInData
            data.WriteBit(false); // ChallengeModeData
            data.WriteBit(false); // e9e5593: new optional after ChallengeModeData
            data.FlushBits();
            if (PetStable.HasValue())
                WriteClassicStableInfo(data, PetStable.GetValue());
            foreach (var settings in CharacterBankTabSettings)
                settings.WriteCreate(data, owner, receiver);
        }

        // The item in client inventory slot clientSlot (see ClassicInventorySlots for the two numberings).
        ObjectGuid GetClassicInvSlot(int clientSlot)
        {
            byte slot = ClassicInventorySlots.ToServer((byte)clientSlot);
            return slot < InvSlots.GetSize() ? InvSlots[slot] : ObjectGuid.Empty;
        }

        // Marks the client slot of every changed server slot (InvSlots group 163, slots from 164 in the reference numbering).
        static void SetClassicInvSlotBits(UpdateMask ours, UpdateMask r, int ourFirstBit, int ourCount)
        {
            for (int i = 0; i < ourCount; ++i)
            {
                if (!ours[ourFirstBit + i])
                    continue;
                byte clientSlot = ClassicInventorySlots.ToClient((byte)i);
                if (clientSlot >= ClassicInventorySlots.UpdateMaskCount)
                    continue;
                r.Set(163);
                r.Set(164 + clientSlot);
            }
        }
        const int ClassicSkillCount = 300;
        const int ClassicBitVectorCount = 14;
        const int ExploredZonesBitVectorIndex = 1;      // PLAYER_DATA_FLAG_EXPLORED_ZONES_INDEX
        const int QuestCompletedBitVectorIndex = 9;     // PLAYER_DATA_FLAG_CHARACTER_QUEST_COMPLETED_INDEX

        float GetClassicSpellCritPercentage()
        {
            float crit = 0.0f;
            for (int i = 1; i < SpellCritPercentage.GetSize(); ++i)
                crit = Math.Max(crit, SpellCritPercentage[i]);
            return crit;
        }

        static void WriteClassicSkillInfo(WorldPacket data, SkillInfo skill)
        {
            int count = skill.SkillLineID.GetSize();
            for (int i = 0; i < ClassicSkillCount; ++i)
            {
                bool has = i < count;
                data.WriteUInt16(has ? skill.SkillLineID[i] : (ushort)0);
                data.WriteUInt16(has ? skill.SkillStep[i] : (ushort)0);
                data.WriteUInt16(has ? skill.SkillRank[i] : (ushort)0);
                data.WriteUInt16(has ? skill.SkillStartingRank[i] : (ushort)0);
                data.WriteUInt16(has ? skill.SkillMaxRank[i] : (ushort)0);
                data.WriteInt16(has ? skill.SkillTempBonus[i] : (short)0);
                data.WriteUInt16(has ? skill.SkillPermBonus[i] : (ushort)0);
            }
        }

        // BitVectors: 14 x (uint32 count, count x uint64). This core keeps explored zones and completed quests in fixed arrays.
        void WriteClassicBitVectors(WorldPacket data)
        {
            for (int i = 0; i < ClassicBitVectorCount; ++i)
            {
                if (i == ExploredZonesBitVectorIndex)
                    WriteBitVector(data, ExploredZones);
                else if (i == QuestCompletedBitVectorIndex)
                    WriteBitVector(data, QuestCompleted);
                else
                    data.WriteUInt32(0);
            }
        }

        // BitVectors update: the changed vectors (explored zones, completed quests) are sent in full.
        void WriteClassicBitVectorsUpdate(WorldPacket data, bool exploredZonesChanged, bool questCompletedChanged)
        {
            uint block = 0;
            if (exploredZonesChanged)
                block |= 1u | (1u << (1 + ExploredZonesBitVectorIndex));
            if (questCompletedChanged)
                block |= 1u | (1u << (1 + QuestCompletedBitVectorIndex));
            data.WriteBits(block != 0 ? 1 : 0, 1);
            if (block != 0)
                data.WriteBits(block, 32);
            data.FlushBits();
            for (int i = 0; i < ClassicBitVectorCount; ++i)
            {
                UpdateFieldArray<ulong> values = i == ExploredZonesBitVectorIndex && exploredZonesChanged ? ExploredZones
                    : i == QuestCompletedBitVectorIndex && questCompletedChanged ? QuestCompleted : null;
                if (values == null)
                    continue;
                int count = values.GetSize();
                while (count > 0 && values[count - 1] == 0)
                    --count;
                // BitVector: changes mask (group + Values), then Values as a complete dynamic field
                data.WriteBits(3, 2);
                WriteCompleteDynamicFieldUpdateMask(count, data);
                data.FlushBits();
                for (int j = 0; j < count; ++j)
                    data.WriteUInt64(values[j]);
            }
        }

        // TraitConfigs map update sent as a full replacement.
        void WriteClassicTraitConfigsUpdate(WorldPacket data)
        {
            data.WriteUInt8(1);
            data.WriteUInt32((uint)TraitConfigs.Size());
            for (int i = 0; i < TraitConfigs.Size(); ++i)
            {
                data.WriteInt32(TraitConfigs[i].ID);
                WriteClassicTraitConfig(data, TraitConfigs[i]);
            }
        }

        static void WriteBitVector(WorldPacket data, UpdateFieldArray<ulong> values)
        {
            int count = values.GetSize();
            while (count > 0 && values[count - 1] == 0)
                --count;
            data.WriteUInt32((uint)count);
            for (int i = 0; i < count; ++i)
                data.WriteUInt64(values[i]);
        }

        static void WriteEmptyPerksVendorItem(WorldPacket data)
        {
            data.WriteInt64(0); // AvailableUntil
            for (int i = 0; i < 10; ++i)
                data.WriteInt32(0); // VendorItemID .. WarbandSceneID
            data.WriteBit(false); // Disabled
            data.WriteBit(false); // DoesNotExpire
            data.FlushBits();
        }

        static void WriteClassicTraitConfig(WorldPacket data, TraitConfig config)
        {
            int type = config.Type;
            data.WriteInt32(config.ID);
            data.WriteInt32(type == 1 ? 4 : type); // Classic: combat configs are TraitConfigType::CamelotCombat (4)
            data.WriteUInt32((uint)config.Entries.Size());
            data.WriteUInt32(0); // SubTrees
            if (type == 2)
                data.WriteInt32(config.SkillLineID);
            if (type == 1)
            {
                data.WriteInt32(config.ChrSpecializationID);
                data.WriteInt32(config.CombatConfigFlags);
                data.WriteInt32(config.LocalIdentifier);
            }
            if (type == 3)
            {
                data.WriteInt32(config.TraitSystemID);
                data.WriteInt32(0); // VariationID
            }
            for (int i = 0; i < config.Entries.Size(); ++i)
            {
                TraitEntry entry = config.Entries[i];
                data.WriteInt32(entry.TraitNodeID);
                data.WriteInt32(entry.TraitNodeEntryID);
                data.WriteInt32(entry.Rank);
                data.WriteInt32(entry.GrantedRanks);
                data.WriteInt32(0); // BonusRanks
            }
            string name = config.Name.GetValue() ?? "";
            data.WriteBits(name.GetByteCount(), 9);
            data.FlushBits();
            data.WriteString(name);
        }

        static void WriteClassicStableInfo(WorldPacket data, StableInfo stable)
        {
            data.WriteUInt32((uint)stable.Pets.Size());
            data.WritePackedGuid(stable.StableMaster);
            for (int i = 0; i < stable.Pets.Size(); ++i)
            {
                stable.Pets[i].WriteCreate(data, null, null);
            }
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Player owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        // Generated from TrinityCore-forever UF::ActivePlayerData::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 402;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[1526]) { r.Set(102); r.Set(133); }
            if (ours[1527]) { r.Set(32); r.Set(48); }
            if (ours[1]) { r.Set(0); r.Set(4); }
            if (ours[2]) { r.Set(0); r.Set(5); }
            if (ours[3]) { r.Set(0); r.Set(7); }
            if (ours[4]) { r.Set(0); r.Set(11); }
            if (ours[5]) { r.Set(0); r.Set(13); }
            if (ours[7]) { r.Set(0); r.Set(14); }
            if (ours[8]) { r.Set(0); r.Set(15); }
            if (ours[9]) { r.Set(0); r.Set(16); }
            if (ours[10]) { r.Set(0); r.Set(18); }
            if (ours[11]) { r.Set(0); r.Set(19); }
            if (ours[12]) { r.Set(0); r.Set(20); }
            if (ours[13]) { r.Set(0); r.Set(30); }
            if (ours[14]) { r.Set(0); r.Set(31); }
            if (ours[15]) { r.Set(32); r.Set(33); }
            if (ours[16]) { r.Set(32); r.Set(40); }
            if (ours[18]) { r.Set(32); r.Set(45); }
            if (ours[19]) { r.Set(32); r.Set(46); }
            for (int i = 0; i < 1; ++i)
                if (ours[21 + i]) { r.Set(50); r.Set(51 + i); }
            for (int i = 0; i < 1; ++i)
                if (ours[23 + i]) { r.Set(52); r.Set(53 + i); }
            for (int i = 0; i < 1; ++i)
                if (ours[25 + i]) { r.Set(54); r.Set(55 + i); }
            if (ours[26]) { r.Set(32); r.Set(56); }
            if (ours[27]) { r.Set(32); r.Set(57); }
            if (ours[28]) { r.Set(32); r.Set(58); }
            if (ours[29]) { r.Set(32); r.Set(60); }
            if (ours[30]) { r.Set(32); r.Set(61); }
            if (ours[31]) { r.Set(32); r.Set(62); }
            if (ours[32]) { r.Set(32); r.Set(63); }
            if (ours[33]) { r.Set(32); r.Set(64); }
            if (ours[34]) { r.Set(32); r.Set(65); }
            if (ours[35]) { r.Set(32); r.Set(66); }
            if (ours[36]) { r.Set(32); r.Set(67); }
            if (ours[37]) { r.Set(32); r.Set(68); }
            if (ours[39]) { r.Set(32); r.Set(69); }
            if (ours[40]) { r.Set(70); r.Set(71); }
            if (ours[41]) { r.Set(70); r.Set(72); }
            if (ours[42]) { r.Set(70); r.Set(73); }
            if (ours[43]) { r.Set(70); r.Set(74); }
            if (ours[44]) { r.Set(70); r.Set(75); }
            if (ours[45]) { r.Set(70); r.Set(76); }
            if (ours[46]) { r.Set(70); r.Set(77); }
            if (ours[47]) { r.Set(70); r.Set(78); }
            if (ours[48]) { r.Set(70); r.Set(79); }
            if (ours[49]) { r.Set(70); r.Set(81); }
            if (ours[50]) { r.Set(70); r.Set(82); }
            if (ours[51]) { r.Set(70); r.Set(83); }
            if (ours[52]) { r.Set(70); r.Set(84); }
            if (ours[53]) { r.Set(70); r.Set(85); }
            if (ours[54]) { r.Set(70); r.Set(86); }
            if (ours[55]) { r.Set(70); r.Set(87); }
            if (ours[56]) { r.Set(70); r.Set(88); }
            if (ours[57]) { r.Set(70); r.Set(89); }
            if (ours[58]) { r.Set(70); r.Set(90); }
            if (ours[59]) { r.Set(70); r.Set(92); }
            if (ours[60]) { r.Set(70); r.Set(93); }
            if (ours[62]) { r.Set(70); r.Set(94); }
            if (ours[63]) { r.Set(70); r.Set(95); }
            if (ours[64]) { r.Set(70); r.Set(96); }
            if (ours[65]) { r.Set(70); r.Set(97); }
            if (ours[66]) { r.Set(70); r.Set(98); }
            if (ours[67]) { r.Set(70); r.Set(99); }
            if (ours[68]) { r.Set(70); r.Set(100); }
            if (ours[69]) { r.Set(70); r.Set(101); }
            if (ours[71]) { r.Set(102); r.Set(103); }
            if (ours[72]) { r.Set(102); r.Set(104); }
            if (ours[73]) { r.Set(102); r.Set(105); }
            if (ours[74]) { r.Set(102); r.Set(106); }
            if (ours[76]) { r.Set(102); r.Set(107); }
            if (ours[77]) { r.Set(102); r.Set(108); }
            if (ours[79]) { r.Set(102); r.Set(109); }
            if (ours[86]) { r.Set(102); r.Set(110); }
            if (ours[92]) { r.Set(102); r.Set(111); }
            if (ours[93]) { r.Set(102); r.Set(112); }
            if (ours[94]) { r.Set(102); r.Set(113); }
            if (ours[95]) { r.Set(102); r.Set(114); }
            if (ours[96]) { r.Set(102); r.Set(116); }
            if (ours[97]) { r.Set(102); r.Set(117); }
            if (ours[98]) { r.Set(102); r.Set(118); }
            if (ours[99]) { r.Set(102); r.Set(119); }
            if (ours[100]) { r.Set(102); r.Set(120); }
            if (ours[101]) { r.Set(102); r.Set(123); }
            if (ours[103]) { r.Set(102); r.Set(124); }
            if (ours[104]) { r.Set(102); r.Set(125); }
            if (ours[105]) { r.Set(102); r.Set(126); }
            if (ours[107]) { r.Set(102); r.Set(127); }
            if (ours[108]) { r.Set(102); r.Set(128); }
            if (ours[109]) { r.Set(102); r.Set(129); }
            if (ours[110]) { r.Set(102); r.Set(130); }
            if (ours[115]) { r.Set(102); r.Set(131); }
            if (ours[116]) { r.Set(134); r.Set(136); }
            if (ours[118]) { r.Set(134); r.Set(143); }
            if (ours[17]) { r.Set(134); r.Set(147); }
            if (ours[119]) { r.Set(134); r.Set(148); }
            if (ours[122]) { r.Set(134); r.Set(155); }
            for (int i = 0; i < 2; ++i)
                if (ours[540 + i]) { r.Set(272); r.Set(273 + i); }
            for (int i = 0; i < 7; ++i)
                if (ours[277 + i]) { r.Set(275); r.Set(276 + i); }
            for (int i = 0; i < 7; ++i)
                if (ours[284 + i]) { r.Set(275); r.Set(283 + i); }
            for (int i = 0; i < 7; ++i)
                if (ours[291 + i]) { r.Set(275); r.Set(290 + i); }
            for (int i = 0; i < 3; ++i)
                if (ours[543 + i]) { r.Set(304); r.Set(305 + i); }
            for (int i = 0; i < 3; ++i)
                if (ours[546 + i]) { r.Set(304); r.Set(308 + i); }
            for (int i = 0; i < 12; ++i)
                if (ours[550 + i]) { r.Set(311); r.Set(312 + i); }
            for (int i = 0; i < 12; ++i)
                if (ours[562 + i]) { r.Set(311); r.Set(324 + i); }
            for (int i = 0; i < 32; ++i)
                if (ours[575 + i]) { r.Set(336); r.Set(337 + i); }
            for (int i = 0; i < 4; ++i)
                if (ours[616 + i]) { r.Set(369); r.Set(370 + i); }
            for (int i = 0; i < 2; ++i)
                if (ours[621 + i]) { r.Set(374); r.Set(375 + i); }
            for (int i = 0; i < 4; ++i)
                if (ours[624 + i]) { r.Set(377); r.Set(378 + i); }
            if (ours[123]) { r.Set(134); r.Set(401); }
            for (int i = 0; i < 7; ++i) if (ours[270 + i]) { r.Set(70); r.Set(80); } // SpellCritPercentage (per school here, one value in Classic)
            if (ours[61]) { r.Set(275); for (int i = 0; i < 7; ++i) r.Set(297 + i); } // ModHealingDonePercent (one value here, per school in Classic)
            if (ours[298] || ours[636]) { r.Set(70); r.Set(91); } // ExploredZones / QuestCompleted -> BitVectors
            SetClassicInvSlotBits(ours, r, 125, 141); // InvSlots, renumbered by ClassicInventorySlots
            return r;
        }

        public void WriteUpdate(WorldPacket data, UpdateMask ourChangesMask, bool ignoreNestedChangesMask, Player owner, Player receiver)
        {
            UpdateMask changesMask = ToRefMask(ourChangesMask);

            uint[] classicBlocks = ClassicUpdateMask.WriteHeader(data, changesMask, RefChangeMaskLength, 14, ClassicUpdateMask.ActivePlayerDataBit, ClassicUpdateMask.ActivePlayerDataGroups);
            bool classicGroup134 = (classicBlocks[4] & (1u << (134 % 32))) != 0;
            bool classicGroup166 = (classicBlocks[5] & (1u << (166 % 32))) != 0;
            if (changesMask[0])
            {
                if (changesMask[4])
                {
                    data.WriteBit(SortBagsRightToLeft.GetValue());
                }
                if (changesMask[5])
                {
                    data.WriteBit(InsertItemsLeftToRight.GetValue());
                }
                if (changesMask[7])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        KnownTitles.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(KnownTitles.Size(), data);
                    }
                }
            }
            if (changesMask[50])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[51])
                    {
                        if (!ignoreNestedChangesMask)
                        {
                            ResearchSites[i].WriteUpdateMask(data);
                        }
                        else
                        {
                            WriteCompleteDynamicFieldUpdateMask(ResearchSites[i].Size(), data);
                        }
                    }
                }
            }
            if (changesMask[52])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[53])
                    {
                        if (!ignoreNestedChangesMask)
                        {
                            ResearchSiteProgress[i].WriteUpdateMask(data);
                        }
                        else
                        {
                            WriteCompleteDynamicFieldUpdateMask(ResearchSiteProgress[i].Size(), data);
                        }
                    }
                }
            }
            if (changesMask[54])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[55])
                    {
                        if (!ignoreNestedChangesMask)
                        {
                            Research[i].WriteUpdateMask(data);
                        }
                        else
                        {
                            WriteCompleteDynamicFieldUpdateMask(Research[i].Size(), data);
                        }
                    }
                }
            }
            if (changesMask[50])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[51])
                    {
                        for (int j = 0; j < ResearchSites[i].Size(); ++j)
                        {
                            if (ResearchSites[i].HasChanged(j) || ignoreNestedChangesMask)
                            {
                                data.WriteUInt16(ResearchSites[i][j]);
                            }
                        }
                    }
                }
            }
            if (changesMask[52])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[53])
                    {
                        for (int j = 0; j < ResearchSiteProgress[i].Size(); ++j)
                        {
                            if (ResearchSiteProgress[i].HasChanged(j) || ignoreNestedChangesMask)
                            {
                                data.WriteUInt32(ResearchSiteProgress[i][j]);
                            }
                        }
                    }
                }
            }
            if (changesMask[54])
            {
                for (int i = 0; i < 1; ++i)
                {
                    if (changesMask[55])
                    {
                        for (int j = 0; j < Research[i].Size(); ++j)
                        {
                            if (Research[i].HasChanged(j) || ignoreNestedChangesMask)
                            {
                                Research[i][j].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                            }
                        }
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[11])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        DailyQuestsCompleted.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(DailyQuestsCompleted.Size(), data);
                    }
                }
                if (changesMask[13])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        AvailableQuestLineXQuestIDs.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(AvailableQuestLineXQuestIDs.Size(), data);
                    }
                }
                if (changesMask[14])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        Heirlooms.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(Heirlooms.Size(), data);
                    }
                }
                if (changesMask[15])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        HeirloomFlags.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(HeirloomFlags.Size(), data);
                    }
                }
                if (changesMask[16])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        Toys.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(Toys.Size(), data);
                    }
                }
                if (changesMask[18])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        Transmog.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(Transmog.Size(), data);
                    }
                }
                if (changesMask[19])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        ConditionalTransmog.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(ConditionalTransmog.Size(), data);
                    }
                }
                if (changesMask[20])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        SelfResSpells.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(SelfResSpells.Size(), data);
                    }
                }
                if (changesMask[30])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        CharacterRestrictions.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(CharacterRestrictions.Size(), data);
                    }
                }
                if (changesMask[31])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        SpellPctModByLabel.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(SpellPctModByLabel.Size(), data);
                    }
                }
            }
            if (changesMask[32])
            {
                if (changesMask[33])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        SpellFlatModByLabel.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(SpellFlatModByLabel.Size(), data);
                    }
                }
                if (changesMask[40])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        TaskQuests.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(TaskQuests.Size(), data);
                    }
                }
                if (changesMask[45])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        CategoryCooldownMods.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(CategoryCooldownMods.Size(), data);
                    }
                }
                if (changesMask[46])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        WeeklySpellUses.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(WeeklySpellUses.Size(), data);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[7])
                {
                    for (int i = 0; i < KnownTitles.Size(); ++i)
                    {
                        if (KnownTitles.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteUInt64((ulong)KnownTitles[i]);
                        }
                    }
                }
                if (changesMask[11])
                {
                    for (int i = 0; i < DailyQuestsCompleted.Size(); ++i)
                    {
                        if (DailyQuestsCompleted.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)DailyQuestsCompleted[i]);
                        }
                    }
                }
                if (changesMask[13])
                {
                    for (int i = 0; i < AvailableQuestLineXQuestIDs.Size(); ++i)
                    {
                        if (AvailableQuestLineXQuestIDs.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)AvailableQuestLineXQuestIDs[i]);
                        }
                    }
                }
                if (changesMask[14])
                {
                    for (int i = 0; i < Heirlooms.Size(); ++i)
                    {
                        if (Heirlooms.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)Heirlooms[i]);
                        }
                    }
                }
                if (changesMask[15])
                {
                    for (int i = 0; i < HeirloomFlags.Size(); ++i)
                    {
                        if (HeirloomFlags.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteUInt32((uint)HeirloomFlags[i]);
                        }
                    }
                }
                if (changesMask[16])
                {
                    for (int i = 0; i < Toys.Size(); ++i)
                    {
                        if (Toys.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)Toys[i]);
                        }
                    }
                }
                if (changesMask[18])
                {
                    for (int i = 0; i < Transmog.Size(); ++i)
                    {
                        if (Transmog.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteUInt32((uint)Transmog[i]);
                        }
                    }
                }
                if (changesMask[19])
                {
                    for (int i = 0; i < ConditionalTransmog.Size(); ++i)
                    {
                        if (ConditionalTransmog.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)ConditionalTransmog[i]);
                        }
                    }
                }
                if (changesMask[20])
                {
                    for (int i = 0; i < SelfResSpells.Size(); ++i)
                    {
                        if (SelfResSpells.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)SelfResSpells[i]);
                        }
                    }
                }
                if (changesMask[30])
                {
                    for (int i = 0; i < CharacterRestrictions.Size(); ++i)
                    {
                        if (CharacterRestrictions.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            CharacterRestrictions[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[31])
                {
                    for (int i = 0; i < SpellPctModByLabel.Size(); ++i)
                    {
                        if (SpellPctModByLabel.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            SpellPctModByLabel[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
            }
            if (changesMask[32])
            {
                if (changesMask[33])
                {
                    for (int i = 0; i < SpellFlatModByLabel.Size(); ++i)
                    {
                        if (SpellFlatModByLabel.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            SpellFlatModByLabel[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[40])
                {
                    for (int i = 0; i < TaskQuests.Size(); ++i)
                    {
                        if (TaskQuests.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            TaskQuests[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[45])
                {
                    for (int i = 0; i < CategoryCooldownMods.Size(); ++i)
                    {
                        if (CategoryCooldownMods.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            CategoryCooldownMods[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[46])
                {
                    for (int i = 0; i < WeeklySpellUses.Size(); ++i)
                    {
                        if (WeeklySpellUses.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            WeeklySpellUses[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
            }
            data.FlushBits();
            if (changesMask[32])
            {
                bool classicBankTabs = changesMask[48] || changesMask[49];
                if (changesMask[48])
                {
                    if (!ignoreNestedChangesMask)
                        CharacterBankTabSettings.WriteUpdateMask(data, 4);
                    else
                        WriteCompleteDynamicFieldUpdateMask(CharacterBankTabSettings.Size(), data, 4);
                }
                if (classicBankTabs)
                    data.FlushBits();
                if (changesMask[48])
                    for (int i = 0; i < CharacterBankTabSettings.Size(); ++i)
                        if (CharacterBankTabSettings.HasChanged(i) || ignoreNestedChangesMask)
                            CharacterBankTabSettings[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[56])
                {
                    data.WritePackedGuid(FarsightObject);
                }
                if (changesMask[57])
                {
                    data.WritePackedGuid(SummonedBattlePetGUID);
                }
                if (changesMask[58])
                {
                    data.WriteUInt64((ulong)Coinage.GetValue());
                }
                if (changesMask[60])
                {
                    data.WriteInt32((int)XP.GetValue());
                }
                if (changesMask[61])
                {
                    data.WriteInt32((int)NextLevelXP.GetValue());
                }
                if (changesMask[62])
                {
                    data.WriteInt32((int)TrialXP.GetValue());
                }
                if (changesMask[63])
                {
                    Skill.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                }
                if (changesMask[64])
                {
                    data.WriteInt32((int)CharacterPoints.GetValue());
                }
                if (changesMask[65])
                {
                    data.WriteInt32((int)MaxTalentTiers.GetValue());
                }
                if (changesMask[66])
                {
                    data.WriteUInt32((uint)TrackCreatureMask.GetValue());
                }
                if (changesMask[67])
                {
                    data.WriteFloat((float)MainhandExpertise.GetValue());
                }
                if (changesMask[68])
                {
                    data.WriteFloat((float)OffhandExpertise.GetValue());
                }
                if (changesMask[69])
                {
                    data.WriteFloat((float)RangedExpertise.GetValue());
                }
            }
            if (changesMask[70])
            {
                if (changesMask[71])
                {
                    data.WriteFloat((float)CombatRatingExpertise.GetValue());
                }
                if (changesMask[72])
                {
                    data.WriteFloat((float)BlockPercentage.GetValue());
                }
                if (changesMask[73])
                {
                    data.WriteFloat((float)DodgePercentage.GetValue());
                }
                if (changesMask[74])
                {
                    data.WriteFloat((float)DodgePercentageFromAttribute.GetValue());
                }
                if (changesMask[75])
                {
                    data.WriteFloat((float)ParryPercentage.GetValue());
                }
                if (changesMask[76])
                {
                    data.WriteFloat((float)ParryPercentageFromAttribute.GetValue());
                }
                if (changesMask[77])
                {
                    data.WriteFloat((float)CritPercentage.GetValue());
                }
                if (changesMask[78])
                {
                    data.WriteFloat((float)RangedCritPercentage.GetValue());
                }
                if (changesMask[79])
                {
                    data.WriteFloat((float)OffhandCritPercentage.GetValue());
                }
                if (changesMask[80])
                {
                    data.WriteFloat(GetClassicSpellCritPercentage());
                }
                if (changesMask[81])
                {
                    data.WriteInt32((int)ShieldBlock.GetValue());
                }
                if (changesMask[82])
                {
                    data.WriteFloat((float)ShieldBlockCritPercentage.GetValue());
                }
                if (changesMask[83])
                {
                    data.WriteFloat((float)Mastery.GetValue());
                }
                if (changesMask[84])
                {
                    data.WriteFloat((float)Speed.GetValue());
                }
                if (changesMask[85])
                {
                    data.WriteFloat((float)Avoidance.GetValue());
                }
                if (changesMask[86])
                {
                    data.WriteFloat((float)Sturdiness.GetValue());
                }
                if (changesMask[87])
                {
                    data.WriteInt32((int)Versatility.GetValue());
                }
                if (changesMask[88])
                {
                    data.WriteFloat((float)VersatilityBonus.GetValue());
                }
                if (changesMask[89])
                {
                    data.WriteFloat((float)PvpPowerDamage.GetValue());
                }
                if (changesMask[90])
                {
                    data.WriteFloat((float)PvpPowerHealing.GetValue());
                }
                if (changesMask[91])
                {
                    WriteClassicBitVectorsUpdate(data, ourChangesMask[298], ourChangesMask[636]);
                }
                if (changesMask[92])
                {
                    data.WriteInt32((int)ModHealingDonePos.GetValue());
                }
                if (changesMask[93])
                {
                    data.WriteFloat((float)ModHealingPercent.GetValue());
                }
                if (changesMask[94])
                {
                    data.WriteFloat((float)ModPeriodicHealingDonePercent.GetValue());
                }
                if (changesMask[95])
                {
                    data.WriteFloat((float)ModSpellPowerPercent.GetValue());
                }
                if (changesMask[96])
                {
                    data.WriteFloat((float)ModResiliencePercent.GetValue());
                }
                if (changesMask[97])
                {
                    data.WriteFloat((float)OverrideSpellPowerByAPPercent.GetValue());
                }
                if (changesMask[98])
                {
                    data.WriteFloat((float)OverrideAPBySpellPowerPercent.GetValue());
                }
                if (changesMask[99])
                {
                    data.WriteInt32((int)ModTargetResistance.GetValue());
                }
                if (changesMask[100])
                {
                    data.WriteInt32((int)ModTargetPhysicalResistance.GetValue());
                }
                if (changesMask[101])
                {
                    data.WriteUInt32((uint)LocalFlags.GetValue());
                }
            }
            if (changesMask[102])
            {
                if (changesMask[103])
                {
                    data.WriteUInt8((byte)GrantableLevels.GetValue());
                }
                if (changesMask[104])
                {
                    data.WriteUInt8((byte)MultiActionBars.GetValue());
                }
                if (changesMask[105])
                {
                    data.WriteUInt8((byte)LifetimeMaxRank.GetValue());
                }
                if (changesMask[106])
                {
                    data.WriteUInt8((byte)NumRespecs.GetValue());
                }
                if (changesMask[107])
                {
                    data.WriteUInt32((uint)PvpMedals.GetValue());
                }
                if (changesMask[108])
                {
                    data.WriteUInt16((ushort)TodayHonorableKills.GetValue());
                }
                if (changesMask[109])
                {
                    data.WriteUInt16((ushort)YesterdayHonorableKills.GetValue());
                }
                if (changesMask[110])
                {
                    data.WriteUInt32((uint)LifetimeHonorableKills.GetValue());
                }
                if (changesMask[111])
                {
                    data.WriteInt32((int)WatchedFactionIndex.GetValue());
                }
                if (changesMask[112])
                {
                    data.WriteInt32((int)MaxLevel.GetValue());
                }
                if (changesMask[113])
                {
                    data.WriteInt32((int)ScalingPlayerLevelDelta.GetValue());
                }
                if (changesMask[114])
                {
                    data.WriteInt32((int)MaxCreatureScalingLevel.GetValue());
                }
                if (changesMask[116])
                {
                    data.WriteInt32((int)PetSpellPower.GetValue());
                }
                if (changesMask[117])
                {
                    data.WriteFloat((float)UiHitModifier.GetValue());
                }
                if (changesMask[118])
                {
                    data.WriteFloat((float)UiSpellHitModifier.GetValue());
                }
                if (changesMask[119])
                {
                    data.WriteInt32((int)HomeRealmTimeOffset.GetValue());
                }
                if (changesMask[120])
                {
                    data.WriteFloat((float)ModPetHaste.GetValue());
                }
                if (changesMask[123])
                {
                    data.WriteUInt8((byte)LocalRegenFlags.GetValue());
                }
                if (changesMask[124])
                {
                    data.WriteUInt8((byte)AuraVision.GetValue());
                }
                if (changesMask[125])
                {
                    data.WriteUInt8((byte)NumBackpackSlots.GetValue());
                }
                if (changesMask[126])
                {
                    data.WriteInt32((int)OverrideSpellsID.GetValue());
                }
                if (changesMask[127])
                {
                    data.WriteUInt16((ushort)LootSpecID.GetValue());
                }
                if (changesMask[128])
                {
                    data.WriteUInt32((uint)OverrideZonePVPType.GetValue());
                }
                if (changesMask[129])
                {
                    data.WriteInt32((int)Honor.GetValue());
                }
                if (changesMask[130])
                {
                    data.WriteInt32((int)HonorNextLevel.GetValue());
                }
                if (changesMask[131])
                {
                    data.WriteInt32((int)PerksProgramCurrency.GetValue());
                }
                if (changesMask[133])
                    data.WriteUInt8(NumCharacterBankTabs);
            }
            if (changesMask[134])
            {
                if (changesMask[136])
                {
                    ResearchHistory.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                }
                if (changesMask[143])
                {
                    data.WriteInt32((int)(uint)TransportServerTime.GetValue());
                }
                if (changesMask[147])
                {
                    WriteClassicTraitConfigsUpdate(data);
                }
                if (changesMask[148])
                {
                    data.WriteUInt32((uint)ActiveCombatTraitConfigID.GetValue());
                }
                if (changesMask[401])
                {
                    data.WriteUInt8((byte)NumStableSlots.GetValue());
                }
                if (changesMask[162])
                {
                    data.WriteUInt8(0); data.WriteUInt32(0); data.WriteUInt8(0); data.WriteUInt8(0); data.WriteFloat(0.0f); data.WriteBit(false); data.FlushBits();
                }
            }
            if (classicGroup134)
            {
                data.WriteBit(false);
                data.WriteBit(PetStable.HasValue());
                data.WriteBit(false);
                data.WriteBit(false);
            }
            if (classicGroup166)
            {
                data.WriteBit(false);
            }
            if (classicGroup134 || classicGroup166)
            {
                data.FlushBits();
            }
            if (classicGroup134)
            {
                if (changesMask[155])
                {
                    if (PetStable.HasValue())
                    {
                        PetStable.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            if (changesMask[163])
            {
                for (int i = 0; i < 108; ++i)
                {
                    if (changesMask[164 + i])
                    {
                        data.WritePackedGuid(GetClassicInvSlot(i));
                    }
                }
            }
            if (changesMask[272])
            {
                for (int i = 0; i < 2; ++i)
                {
                    if (changesMask[273 + i])
                    {
                        RestInfo[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            if (changesMask[275])
            {
                for (int i = 0; i < 7; ++i)
                {
                    if (changesMask[276 + i])
                    {
                        data.WriteInt32((int)ModDamageDonePos[i]);
                    }
                    if (changesMask[283 + i])
                    {
                        data.WriteInt32((int)ModDamageDoneNeg[i]);
                    }
                    if (changesMask[290 + i])
                    {
                        data.WriteFloat((float)ModDamageDonePercent[i]);
                    }
                    if (changesMask[297 + i])
                    {
                        data.WriteFloat(ModHealingDonePercent.GetValue());
                    }
                }
            }
            if (changesMask[304])
            {
                for (int i = 0; i < 3; ++i)
                {
                    if (changesMask[305 + i])
                    {
                        data.WriteFloat((float)WeaponDmgMultipliers[i]);
                    }
                    if (changesMask[308 + i])
                    {
                        data.WriteFloat((float)WeaponAtkSpeedMultipliers[i]);
                    }
                }
            }
            if (changesMask[311])
            {
                for (int i = 0; i < 12; ++i)
                {
                    if (changesMask[312 + i])
                    {
                        data.WriteUInt32((uint)BuybackPrice[i]);
                    }
                    if (changesMask[324 + i])
                    {
                        data.WriteInt64((long)BuybackTimestamp[i].ToSeconds());
                    }
                }
            }
            if (changesMask[336])
            {
                for (int i = 0; i < 32; ++i)
                {
                    if (changesMask[337 + i])
                    {
                        data.WriteInt32((int)CombatRatings[i]);
                    }
                }
            }
            if (changesMask[369])
            {
                for (int i = 0; i < 4; ++i)
                {
                    if (changesMask[370 + i])
                    {
                        data.WriteUInt32((uint)NoReagentCostMask[i]);
                    }
                }
            }
            if (changesMask[374])
            {
                for (int i = 0; i < 2; ++i)
                {
                    if (changesMask[375 + i])
                    {
                        data.WriteInt32((int)ProfessionSkillLine[i]);
                    }
                }
            }
            if (changesMask[377])
            {
                for (int i = 0; i < 5; ++i)
                {
                    if (changesMask[378 + i])
                    {
                        data.WriteUInt32((uint)BagSlotFlags[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(SortBagsRightToLeft);
            ClearChangesMask(InsertItemsLeftToRight);
            ClearChangesMask(ResearchSites);
            ClearChangesMask(ResearchSiteProgress);
            ClearChangesMask(Research);
            ClearChangesMask(KnownTitles);
            ClearChangesMask(DailyQuestsCompleted);

            ClearChangesMask(AvailableQuestLineXQuestIDs);
            ClearChangesMask(Field_1000);
            ClearChangesMask(Heirlooms);
            ClearChangesMask(HeirloomFlags);
            ClearChangesMask(Toys);
            ClearChangesMask(Transmog);
            ClearChangesMask(ConditionalTransmog);

            ClearChangesMask(SelfResSpells);
            ClearChangesMask(SpellPctModByLabel);
            ClearChangesMask(SpellFlatModByLabel);
            ClearChangesMask(TaskQuests);
            ClearChangesMask(CategoryCooldownMods);
            ClearChangesMask(WeeklySpellUses);
            ClearChangesMask(CharacterRestrictions);
            ClearChangesMask(TraitConfigs);

            ClearChangesMask(FarsightObject);
            ClearChangesMask(SummonedBattlePetGUID);
            ClearChangesMask(Coinage);
            ClearChangesMask(XP);
            ClearChangesMask(NextLevelXP);
            ClearChangesMask(TrialXP);
            ClearChangesMask(Skill);
            ClearChangesMask(CharacterPoints);

            ClearChangesMask(MaxTalentTiers);
            ClearChangesMask(TrackCreatureMask);
            ClearChangesMask(MainhandExpertise);
            ClearChangesMask(OffhandExpertise);
            ClearChangesMask(RangedExpertise);
            ClearChangesMask(CombatRatingExpertise);
            ClearChangesMask(BlockPercentage);
            ClearChangesMask(DodgePercentage);
            ClearChangesMask(DodgePercentageFromAttribute);

            ClearChangesMask(ParryPercentage);
            ClearChangesMask(ParryPercentageFromAttribute);
            ClearChangesMask(CritPercentage);
            ClearChangesMask(RangedCritPercentage);
            ClearChangesMask(OffhandCritPercentage);
            ClearChangesMask(ShieldBlock);
            ClearChangesMask(ShieldBlockCritPercentage);
            ClearChangesMask(Mastery);
            ClearChangesMask(Speed);

            ClearChangesMask(Avoidance);
            ClearChangesMask(Sturdiness);
            ClearChangesMask(Versatility);
            ClearChangesMask(VersatilityBonus);
            ClearChangesMask(PvpPowerDamage);
            ClearChangesMask(PvpPowerHealing);
            ClearChangesMask(ModHealingDonePos);
            ClearChangesMask(ModHealingPercent);
            ClearChangesMask(ModHealingDonePercent);
            ClearChangesMask(ModPeriodicHealingDonePercent);

            ClearChangesMask(ModSpellPowerPercent);
            ClearChangesMask(ModResiliencePercent);
            ClearChangesMask(OverrideSpellPowerByAPPercent);
            ClearChangesMask(OverrideAPBySpellPowerPercent);
            ClearChangesMask(ModTargetResistance);
            ClearChangesMask(ModTargetPhysicalResistance);
            ClearChangesMask(LocalFlags);

            ClearChangesMask(GrantableLevels);
            ClearChangesMask(MultiActionBars);
            ClearChangesMask(LifetimeMaxRank);
            ClearChangesMask(NumRespecs);
            ClearChangesMask(AmmoID);
            ClearChangesMask(PvpMedals);
            ClearChangesMask(TodayHonorableKills);
            ClearChangesMask(TodayDishonorableKills);
            ClearChangesMask(YesterdayHonorableKills);
            ClearChangesMask(YesterdayDishonorableKills);

            ClearChangesMask(LastWeekHonorableKills);
            ClearChangesMask(LastWeekDishonorableKills);
            ClearChangesMask(ThisWeekHonorableKills);
            ClearChangesMask(ThisWeekDishonorableKills);
            ClearChangesMask(ThisWeekContribution);
            ClearChangesMask(LifetimeHonorableKills);
            ClearChangesMask(LifetimeDishonorableKills);
            ClearChangesMask(Field_F24);

            ClearChangesMask(YesterdayContribution);
            ClearChangesMask(LastWeekContribution);
            ClearChangesMask(LastWeekRank);
            ClearChangesMask(WatchedFactionIndex);
            ClearChangesMask(MaxLevel);
            ClearChangesMask(ScalingPlayerLevelDelta);
            ClearChangesMask(MaxCreatureScalingLevel);
            ClearChangesMask(PetSpellPower);
            ClearChangesMask(UiHitModifier);
            ClearChangesMask(UiSpellHitModifier);

            ClearChangesMask(HomeRealmTimeOffset);
            ClearChangesMask(ModPetHaste);
            ClearChangesMask(LocalRegenFlags);
            ClearChangesMask(AuraVision);
            ClearChangesMask(NumBackpackSlots);
            ClearChangesMask(OverrideSpellsID);
            ClearChangesMask(LfgBonusFactionID);
            ClearChangesMask(LootSpecID);
            ClearChangesMask(OverrideZonePVPType);
            ClearChangesMask(Honor);
            ClearChangesMask(HonorNextLevel);
            ClearChangesMask(Field_F74);

            ClearChangesMask(PvpTierMaxFromWins);
            ClearChangesMask(PvpLastWeeksTierMaxFromWins);
            ClearChangesMask(PvpRankProgress);
            ClearChangesMask(PerksProgramCurrency);
            ClearChangesMask(ResearchHistory);
            ClearChangesMask(FrozenPerksVendorItem);
            ClearChangesMask(TransportServerTime);
            ClearChangesMask(ActiveCombatTraitConfigID);
            ClearChangesMask(GlyphsEnabled);
            ClearChangesMask(LfgRoles);

            ClearChangesMask(PetStable);
            ClearChangesMask(NumStableSlots);
            ClearChangesMask(InvSlots);
            ClearChangesMask(TrackResourceMask);
            ClearChangesMask(SpellCritPercentage);
            ClearChangesMask(ModDamageDonePos);
            ClearChangesMask(ModDamageDoneNeg);
            ClearChangesMask(ModDamageDonePercent);

            ClearChangesMask(ExploredZones);
            ClearChangesMask(RestInfo);
            ClearChangesMask(WeaponDmgMultipliers);
            ClearChangesMask(WeaponAtkSpeedMultipliers);
            ClearChangesMask(BuybackPrice);
            ClearChangesMask(BuybackTimestamp);
            ClearChangesMask(CombatRatings);

            ClearChangesMask(PvpInfo);
            ClearChangesMask(NoReagentCostMask);
            ClearChangesMask(ProfessionSkillLine);
            ClearChangesMask(BagSlotFlags);
            ClearChangesMask(BankBagSlotFlags);
            ClearChangesMask(QuestCompleted);
            ClearChangesMask(GlyphSlots);
            ClearChangesMask(Glyphs);
            ClearChangesMask(NumCharacterBankTabs);
            ClearChangesMask(CharacterBankTabSettings);
            _changesMask.ResetAll();
        }
    }

    public class GameObjectFieldData : HasChangesMask
    {
        public UpdateField<List<int>> StateWorldEffectIDs = new(0, 1);
        public DynamicUpdateField<int> EnableDoodadSets = new(0, 2);
        public DynamicUpdateField<int> WorldEffects = new(0, 3);
        public UpdateField<int> DisplayID = new(0, 4);
        public UpdateField<int> SpellVisualID = new(0, 5);
        public UpdateField<int> StateSpellVisualID = new(0, 6);
        public UpdateField<int> SpawnTrackingStateAnimID = new(0, 7);
        public UpdateField<int> SpawnTrackingStateAnimKitID = new(0, 8);
        public UpdateField<ObjectGuid> CreatedBy = new(0, 9);
        public UpdateField<ObjectGuid> GuildGUID = new(0, 10);
        public UpdateField<uint> Flags = new(0, 11);
        public UpdateField<Quaternion> ParentRotation = new(0, 12);
        public UpdateField<int> FactionTemplate = new(0, 13);
        public UpdateField<uint> Level = new(0, 14);
        public UpdateField<sbyte> State = new(0, 15);
        public UpdateField<sbyte> TypeID = new(0, 16);
        public UpdateField<byte> PercentHealth = new(0, 17);
        public UpdateField<int> ArtKit = new(0, 18);
        public UpdateField<int> CustomParam = new(0, 19);
        // Classic 1.60.1 GameObjectData has 28 wire bits. The local generated
        // retail field table models 20 fields; WriteUpdate remaps those bits.
        static int changeMaskLength = 28;

        // Internal field bits use the WotLK model, including its visibility masks.
        // ToRefMask translates them to Forever wire bits; keep these number spaces separate.
        public GameObjectFieldData() : base(0, TypeId.GameObject, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, GameObject owner, Player receiver)
        {
            data.WriteInt32(DisplayID);
            data.WriteInt32(SpellVisualID);
            data.WriteInt32(StateSpellVisualID);
            data.WriteInt32(SpawnTrackingStateAnimID);
            data.WriteInt32(SpawnTrackingStateAnimKitID);
            data.WriteInt32(StateWorldEffectIDs.GetValue().Count);
            data.WriteUInt32(0); // Classic StateWorldEffectsQuestObjectiveID
            for (int i = 0; i < StateWorldEffectIDs.GetValue().Count; ++i)
            {
                data.WriteInt32(StateWorldEffectIDs.GetValue()[i]);
            }
            data.WritePackedGuid(CreatedBy);
            data.WritePackedGuid(GuildGUID);
            data.WriteUInt32(GetViewerGameObjectFlags(this, owner, receiver));
            data.WriteUInt32(0); // Classic FlagsB
            data.WriteQuaternion(ParentRotation);
            data.WriteInt32(FactionTemplate);
            data.WriteInt32((int)Level.GetValue());
            data.WriteInt8(GetViewerGameObjectState(this, owner, receiver));
            data.WriteInt8(TypeID);
            data.WriteUInt8(PercentHealth);
            data.WriteInt32(ArtKit);
            data.WriteInt32(EnableDoodadSets.Size());
            data.WriteInt32(CustomParam);
            data.WriteInt32((int)Level.GetValue()); // Classic duplicate level field
            data.WriteUInt32(0); // AnimGroupInstance
            data.WriteUInt32(0); // UiWidgetItemID
            data.WriteUInt32(0); // UiWidgetItemQuality
            data.WriteUInt32(0); // UiWidgetItemCount
            data.WriteInt32(WorldEffects.Size());
            
            for (int i = 0; i < EnableDoodadSets.Size(); ++i)
            {
                data.WriteInt32(EnableDoodadSets[i]);
            }
            for (int i = 0; i < WorldEffects.Size(); ++i)
            {
                data.WriteInt32(WorldEffects[i]);
            }
            data.WriteBit(false); // AssistActionData is not modeled by this core
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, GameObject owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        // Generated from TrinityCore-forever UF::GameObjectData::WriteUpdate (genupdate.py): reference bit numbering.
        const int RefChangeMaskLength = 27;

        static UpdateMask ToRefMask(UpdateMask ours)
        {
            UpdateMask r = new(RefChangeMaskLength);
            if (ours[1]) { r.Set(0); r.Set(1); }
            if (ours[2]) { r.Set(0); r.Set(2); }
            if (ours[3]) { r.Set(0); r.Set(3); }
            if (ours[4]) { r.Set(0); r.Set(4); }
            if (ours[5]) { r.Set(0); r.Set(5); }
            if (ours[6]) { r.Set(0); r.Set(6); }
            if (ours[7]) { r.Set(0); r.Set(7); }
            if (ours[8]) { r.Set(0); r.Set(8); }
            if (ours[9]) { r.Set(0); r.Set(10); }
            if (ours[10]) { r.Set(0); r.Set(11); }
            if (ours[11]) { r.Set(0); r.Set(12); }
            if (ours[12]) { r.Set(0); r.Set(14); }
            if (ours[13]) { r.Set(0); r.Set(15); }
            if (ours[15]) { r.Set(0); r.Set(16); }
            if (ours[16]) { r.Set(0); r.Set(17); }
            if (ours[17]) { r.Set(0); r.Set(18); }
            if (ours[18]) { r.Set(0); r.Set(19); }
            if (ours[19]) { r.Set(0); r.Set(20); }
            if (ours[14]) { r.Set(0); r.Set(21); }
            return r;
        }

        public void WriteUpdate(WorldPacket data, UpdateMask ourChangesMask, bool ignoreNestedChangesMask, GameObject owner, Player receiver)
        {
            UpdateMask changesMask = ToRefMask(ourChangesMask);

            uint retailBits = changesMask.GetBlock(0);
            uint classicBits = (retailBits & 0xFFFFu) | ((retailBits & ~0xFFFFu) << 1) | (changesMask[21] ? (1u << 16) : 0u);
            data.WriteBits(classicBits, 28);
            List<int> stateWorldEffectIDs = null;
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    stateWorldEffectIDs = StateWorldEffectIDs.GetValue();
                    data.WriteBits(stateWorldEffectIDs.Count, 32);
                    for (int i = 0; i < stateWorldEffectIDs.Count; ++i)
                    {
                        data.WriteUInt32((uint)stateWorldEffectIDs[i]);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[2])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        EnableDoodadSets.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(EnableDoodadSets.Size(), data);
                    }
                }
                if (changesMask[3])
                {
                    if (!ignoreNestedChangesMask)
                    {
                        WorldEffects.WriteUpdateMask(data);
                    }
                    else
                    {
                        WriteCompleteDynamicFieldUpdateMask(WorldEffects.Size(), data);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[2])
                {
                    for (int i = 0; i < EnableDoodadSets.Size(); ++i)
                    {
                        if (EnableDoodadSets.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)EnableDoodadSets[i]);
                        }
                    }
                }
                if (changesMask[3])
                {
                    for (int i = 0; i < WorldEffects.Size(); ++i)
                    {
                        if (WorldEffects.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            data.WriteInt32((int)WorldEffects[i]);
                        }
                    }
                }
                if (changesMask[4])
                {
                    data.WriteInt32((int)DisplayID.GetValue());
                }
                if (changesMask[5])
                {
                    data.WriteUInt32((uint)SpellVisualID.GetValue());
                }
                if (changesMask[6])
                {
                    data.WriteUInt32((uint)StateSpellVisualID.GetValue());
                }
                if (changesMask[7])
                {
                    data.WriteUInt32((uint)SpawnTrackingStateAnimID.GetValue());
                }
                if (changesMask[8])
                {
                    data.WriteUInt32((uint)SpawnTrackingStateAnimKitID.GetValue());
                }
                if (changesMask[10])
                {
                    data.WritePackedGuid(CreatedBy);
                }
                if (changesMask[11])
                {
                    data.WritePackedGuid(GuildGUID);
                }
                if (changesMask[12])
                {
                    data.WriteUInt32(GetViewerGameObjectFlags(this, owner, receiver));
                }
                if (changesMask[14])
                {
                    data.WriteFloat(ParentRotation.GetValue().X);
                    data.WriteFloat(ParentRotation.GetValue().Y);
                    data.WriteFloat(ParentRotation.GetValue().Z);
                    data.WriteFloat(ParentRotation.GetValue().W);
                }
                if (changesMask[15])
                {
                    data.WriteInt32((int)FactionTemplate.GetValue());
                }
                if (changesMask[21])
                {
                    data.WriteInt32((int)Level.GetValue());
                }
                if (changesMask[16])
                {
                    data.WriteInt8(GetViewerGameObjectState(this, owner, receiver));
                }
                if (changesMask[17])
                {
                    data.WriteInt8((sbyte)TypeID.GetValue());
                }
                if (changesMask[18])
                {
                    data.WriteUInt8((byte)PercentHealth.GetValue());
                }
                if (changesMask[19])
                {
                    data.WriteUInt32((uint)ArtKit.GetValue());
                }
                if (changesMask[20])
                {
                    data.WriteUInt32((uint)CustomParam.GetValue());
                }
                if (changesMask[21])
                {
                    data.WriteInt32((int)Level.GetValue());
                }
                data.WriteBit(false);
            }
            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(StateWorldEffectIDs);
            ClearChangesMask(EnableDoodadSets);
            ClearChangesMask(WorldEffects);
            ClearChangesMask(DisplayID);
            ClearChangesMask(SpellVisualID);
            ClearChangesMask(StateSpellVisualID);
            ClearChangesMask(SpawnTrackingStateAnimID);
            ClearChangesMask(SpawnTrackingStateAnimKitID);
            ClearChangesMask(CreatedBy);
            ClearChangesMask(GuildGUID);
            ClearChangesMask(Flags);
            ClearChangesMask(ParentRotation);
            ClearChangesMask(FactionTemplate);
            ClearChangesMask(Level);
            ClearChangesMask(State);
            ClearChangesMask(TypeID);
            ClearChangesMask(PercentHealth);
            ClearChangesMask(ArtKit);
            ClearChangesMask(CustomParam);
            _changesMask.ResetAll();
        }

        uint GetViewerGameObjectFlags(GameObjectFieldData gameObjectData, GameObject gameObject, Player receiver)
        {
            uint flags = gameObjectData.Flags;
            if (gameObject.GetGoType() == GameObjectTypes.Chest)
                if (gameObject.GetGoInfo().Chest.usegrouplootrules != 0 && !gameObject.IsLootAllowedFor(receiver))
                    flags |= (uint)(GameObjectFlags.Locked | GameObjectFlags.NotSelectable);

            return flags;
        }

        sbyte GetViewerGameObjectState(GameObjectFieldData gameObjectData, GameObject gameObject, Player receiver)
        {
            return (sbyte)gameObject.GetGoStateFor(receiver.GetGUID());
        }
    }

    public class DynamicObjectData : HasChangesMask
    {
        public UpdateField<ObjectGuid> Caster = new(0, 1);
        public UpdateField<byte> Type = new(0, 2);
        public UpdateField<int> SpellXSpellVisualID = new(0, 3);
        public UpdateField<int> SpellID = new(0, 4);
        public UpdateField<float> Radius = new(0, 5);
        public UpdateField<RelativeTime> CastTime = new(0, 6);
        static int changeMaskLength = 7;

        public DynamicObjectData() : base(0, TypeId.DynamicObject, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, DynamicObject owner, Player receiver)
        {
            data.WritePackedGuid(Caster);
            data.WriteUInt8(Type);
            data.WriteInt32(SpellXSpellVisualID);
            data.WriteInt32(0); // SpellCastVisual.ScriptVisualID
            data.WriteInt32(SpellID);
            data.WriteFloat(Radius);
            data.WriteUInt32(CastTime.GetValue());
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, DynamicObject owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, DynamicObject owner, Player receiver)
        {
            data.WriteBits(_changesMask.GetBlock(0), changeMaskLength);

            data.FlushBits();
            if (_changesMask[0])
            {
                if (_changesMask[1])
                {
                    data.WritePackedGuid(Caster);
                }
                if (_changesMask[2])
                {
                    data.WriteUInt8(Type);
                }
                if (_changesMask[3])
                {
                    data.WriteInt32(SpellXSpellVisualID);
                    data.WriteInt32(0); // SpellCastVisual.ScriptVisualID
                }
                if (_changesMask[4])
                {
                    data.WriteInt32(SpellID);
                }
                if (_changesMask[5])
                {
                    data.WriteFloat(Radius);
                }
                if (_changesMask[6])
                {
                    data.WriteUInt32(CastTime.GetValue());
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Caster);
            ClearChangesMask(Type);
            ClearChangesMask(SpellXSpellVisualID);
            ClearChangesMask(SpellID);
            ClearChangesMask(Radius);
            ClearChangesMask(CastTime);
            _changesMask.ResetAll();
        }
    }

    public class CorpseData : HasChangesMask
    {
        public DynamicUpdateField<ChrCustomizationChoice> Customizations = new(0, 1);
        public UpdateField<uint> DynamicFlags = new(0, 2);
        public UpdateField<ObjectGuid> Owner = new(0, 3);
        public UpdateField<ObjectGuid> PartyGUID = new(0, 4);
        public UpdateField<ObjectGuid> GuildGUID = new(0, 5);
        public UpdateField<int> DisplayID = new(0, 6);
        public UpdateField<byte> RaceID = new(0, 7);
        public UpdateField<byte> Sex = new(0, 8);
        public UpdateField<byte> Class = new(0, 9);
        public UpdateField<uint> Flags = new(0, 10);
        public UpdateField<int> FactionTemplate = new(0, 11);
        public UpdateField<uint> StateSpellVisualKitID = new(0, 12);
        public UpdateFieldArray<int> Items = new(19, 13, 14);
        static int changeMaskLength = 33;

        public CorpseData() : base(0, TypeId.Corpse, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Corpse owner, Player receiver)
        {
            data.WriteUInt32(DynamicFlags);
            data.WritePackedGuid(Owner);
            data.WritePackedGuid(PartyGUID);
            data.WritePackedGuid(GuildGUID);
            data.WriteInt32(DisplayID);
            for (int i = 0; i < 19; ++i)
            {
                data.WriteInt32(Items[i]);
            }
            data.WriteUInt8(RaceID);
            data.WriteUInt8(Sex);
            data.WriteUInt8(Class);
            data.WriteInt32(Customizations.Size());
            data.WriteUInt32(Flags);
            data.WriteInt32(FactionTemplate);
            data.WriteUInt32(0); // Classic StateSpellVisualKitID
            for (int i = 0; i < Customizations.Size(); ++i)
            {
                Customizations[i].WriteCreate(data, owner, receiver);
            }
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Corpse owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, Corpse owner, Player receiver)
        {
            // CorpseData has 33 mask bits, so its update mask spans two 32-bit blocks.
            data.WriteBits(changesMask.GetBlocksMask(0), 2);
            for (uint i = 0; i < 2; ++i)
                if (changesMask.GetBlock(i) != 0)
                    data.WriteBits(changesMask.GetBlock(i), 32);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    if (!ignoreNestedChangesMask)
                        Customizations.WriteUpdateMask(data);
                    else
                        WriteCompleteDynamicFieldUpdateMask(Customizations.Size(), data);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    for (int i = 0; i < Customizations.Size(); ++i)
                    {
                        if (Customizations.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            Customizations[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[2])
                {
                    data.WriteUInt32(DynamicFlags);
                }
                if (changesMask[3])
                {
                    data.WritePackedGuid(Owner);
                }
                if (changesMask[4])
                {
                    data.WritePackedGuid(PartyGUID);
                }
                if (changesMask[5])
                {
                    data.WritePackedGuid(GuildGUID);
                }
                if (changesMask[6])
                {
                    data.WriteInt32(DisplayID);
                }
                if (changesMask[7])
                {
                    data.WriteUInt8(RaceID);
                }
                if (changesMask[8])
                {
                    data.WriteUInt8(Sex);
                }
                if (changesMask[9])
                {
                    data.WriteUInt8(Class);
                }
                if (changesMask[10])
                {
                    data.WriteUInt32(Flags);
                }
                if (changesMask[11])
                {
                    data.WriteInt32(FactionTemplate);
                }
                if (changesMask[12])
                {
                    data.WriteUInt32(StateSpellVisualKitID);
                }
            }
            if (changesMask[13])
            {
                for (int i = 0; i < 19; ++i)
                {
                    if (changesMask[14 + i])
                    {
                        data.WriteInt32(Items[i]);
                    }
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Customizations);
            ClearChangesMask(DynamicFlags);
            ClearChangesMask(Owner);
            ClearChangesMask(PartyGUID);
            ClearChangesMask(GuildGUID);
            ClearChangesMask(DisplayID);
            ClearChangesMask(RaceID);
            ClearChangesMask(Sex);
            ClearChangesMask(Class);
            ClearChangesMask(Flags);
            ClearChangesMask(FactionTemplate);
            ClearChangesMask(StateSpellVisualKitID);
            ClearChangesMask(Items);
            _changesMask.ResetAll();
        }
    }

    public class ScaleCurve : HasChangesMask
    {
        public UpdateField<bool> OverrideActive = new(0, 1);
        public UpdateField<RelativeTime> StartTimeOffset = new(0, 2);
        public UpdateField<uint> ParameterCurve = new(0, 3);
        public UpdateFieldArray<Vector2> Points = new(2, 4, 5);
        static int changeMaskLength = 7;

        public ScaleCurve() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, AreaTrigger owner, Player receiver)
        {
            data.WriteUInt32(StartTimeOffset.GetValue());
            for (int i = 0; i < 2; ++i)
            {
                data.WriteVector2(Points[i]);
            }
            data.WriteUInt32(ParameterCurve);
            data.WriteBit((bool)OverrideActive);
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, AreaTrigger owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteBit(OverrideActive);
                }
            }

            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[2])
                {
                    data.WriteUInt32(StartTimeOffset.GetValue());
                }
                if (changesMask[3])
                {
                    data.WriteUInt32(ParameterCurve);
                }
            }
            if (changesMask[4])
            {
                for (int i = 0; i < 2; ++i)
                {
                    if (changesMask[5 + i])
                    {
                        data.WriteVector2(Points[i]);
                    }
                }
            }
            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(OverrideActive);
            ClearChangesMask(StartTimeOffset);
            ClearChangesMask(ParameterCurve);
            ClearChangesMask(Points);
            _changesMask.ResetAll();
        }
    }

    public class VisualAnim : HasChangesMask
    {
        public UpdateField<bool> Field_C = new(0, 1);
        public UpdateField<int> AnimationDataID = new(0, 2);
        public UpdateField<int> AnimKitID = new(0, 3);
        public UpdateField<uint> AnimProgress = new(0, 4);
        static int changeMaskLength = 5;

        public VisualAnim() : base(0, TypeId.AreaTrigger, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, AreaTrigger owner, Player receiver)
        {
            data.WriteInt32(AnimationDataID);
            data.WriteInt32(AnimKitID);
            data.WriteUInt32(AnimProgress);
            data.WriteBit(Field_C);
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, AreaTrigger owner, Player receiver)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    data.WriteBit(Field_C);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[2])
                {
                    data.WriteInt32(AnimationDataID);
                }
                if (changesMask[3])
                {
                    data.WriteInt32(AnimKitID);
                }
                if (changesMask[4])
                {
                    data.WriteUInt32(AnimProgress);
                }
            }
            data.FlushBits();
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Field_C);
            ClearChangesMask(AnimationDataID);
            ClearChangesMask(AnimKitID);
            ClearChangesMask(AnimProgress);
            _changesMask.ResetAll();
        }
    }

    public class AreaTriggerFieldData : HasChangesMask
    {
        public UpdateField<ScaleCurve> OverrideScaleCurve = new(0, 1);
        public UpdateField<ScaleCurve> ExtraScaleCurve = new(0, 2);
        public UpdateField<ScaleCurve> OverrideMoveCurveX = new(0, 3);
        public UpdateField<ScaleCurve> OverrideMoveCurveY = new(0, 4);
        public UpdateField<ScaleCurve> OverrideMoveCurveZ = new(0, 5);
        public UpdateField<ScaleCurve> OverrideShapeCurve = new(0, 6);
        public UpdateField<ObjectGuid> Caster = new(0, 7);
        public UpdateField<Milliseconds> Duration = new(0, 8);
        public UpdateField<Milliseconds> TimeToTarget = new(0, 9);
        public UpdateField<Milliseconds> TimeToTargetScale = new(0, 10);
        public UpdateField<Milliseconds> TimeToTargetExtraScale = new(0, 11);
        public UpdateField<Milliseconds> TimeToTargetPos = new(0, 12);
        public UpdateField<Milliseconds> TimeToTargetShape = new(0, 13);
        public UpdateField<int> SpellID = new(0, 14);
        public UpdateField<int> SpellForVisuals = new(0, 15);
        public UpdateField<SpellCastVisual> SpellVisual = new(0, 16);
        public UpdateField<float> BoundsRadius2D = new(0, 17);
        public UpdateField<int> DecalPropertiesID = new(0, 18);
        public UpdateField<ObjectGuid> CreatingEffectGUID = new(0, 19);
        public UpdateField<ObjectGuid> OrbitPathTarget = new(0, 20);
        public UpdateField<Vector3> RollPitchYaw = new(0, 21);
        public UpdateField<int> PositionalSoundKitID = new(0, 22);
        public UpdateField<uint> MovementStartTime = new(0, 23);
        public UpdateField<uint> CreationTime = new(0, 24);
        public UpdateField<float> ZOffset = new(0, 25);
        public UpdateField<Vector3> TargetRollPitchYaw = new(0, 26);
        public UpdateField<uint> Flags = new(0, 27);
        public UpdateField<VisualAnim> VisualAnim = new(0, 28);
        public UpdateField<uint> ScaleCurveId = new(0, 29);
        public UpdateField<uint> FacingCurveId = new(0, 30);
        public UpdateField<uint> MorphCurveId = new(0, 31);
        public UpdateField<uint> MoveCurveId = new(0, 33);
        public UpdateField<float> Facing = new(0, 34);
        public UpdateField<int> PathType = new(0, 36);
        public UpdateField<uint> ShapeType = new(0, 37);
        public UpdateField<AreaTriggerPeriodModifier> PeriodModifier = new(0, 38);
        static int changeMaskLength = 39;

        public AreaTriggerFieldData() : base(0, TypeId.AreaTrigger, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, AreaTrigger owner, Player receiver)
        {
            OverrideScaleCurve.GetValue().WriteCreate(data, owner, receiver);
            ExtraScaleCurve.GetValue().WriteCreate(data, owner, receiver);
            OverrideMoveCurveX.GetValue().WriteCreate(data, owner, receiver);
            OverrideMoveCurveY.GetValue().WriteCreate(data, owner, receiver);
            OverrideMoveCurveZ.GetValue().WriteCreate(data, owner, receiver);
            OverrideShapeCurve.GetValue().WriteCreate(data, owner, receiver);
            data.WritePackedGuid(Caster);
            data.WriteUInt32((uint)Duration.GetValue());
            data.WriteUInt32((uint)TimeToTarget.GetValue());
            data.WriteUInt32((uint)TimeToTargetScale.GetValue());
            data.WriteUInt32((uint)TimeToTargetExtraScale.GetValue());
            data.WriteUInt32((uint)TimeToTargetPos.GetValue());
            data.WriteUInt32((uint)TimeToTargetShape.GetValue());
            data.WriteInt32(SpellID);
            data.WriteInt32(SpellForVisuals);
            SpellVisual.GetValue().Write(data);
            data.WriteFloat(BoundsRadius2D);
            data.WriteUInt32((uint)DecalPropertiesID.GetValue());
            data.WritePackedGuid(CreatingEffectGUID);
            data.WritePackedGuid(OrbitPathTarget);
            data.WriteVector3(owner.GetRollPitchYaw());
            data.WriteInt32(PositionalSoundKitID);
            data.WriteUInt32(MovementStartTime);
            data.WriteUInt32(CreationTime);
            data.WriteFloat(ZOffset);
            data.WriteUInt32(Flags);
            VisualAnim.GetValue().WriteCreate(data, owner, receiver);
            AreaTriggerCreateProperties properties = owner.GetCreateProperties();
            data.WriteUInt32((uint)(properties?.ScaleCurveId ?? 0));
            data.WriteUInt32((uint)(properties?.FacingCurveId ?? 0));
            data.WriteUInt32((uint)(properties?.MorphCurveId ?? 0));
            data.WriteUInt32((uint)(properties?.MoveCurveId ?? 0));
            data.WriteFloat(owner.GetOrientation());
            AreaTriggerShapeInfo shape = owner.GetShape();
            bool hasOrbit = owner.HasOrbit();
            bool hasSpline = owner.HasSplines();
            int pathType = properties?.ScriptId > 0 ? 3 : hasOrbit ? 1 : hasSpline ? 0 : -1;
            uint shapeType = shape == null ? uint.MaxValue : shape.TriggerType switch
            {
                AreaTriggerShapeType.Sphere => 0,
                AreaTriggerShapeType.Box => 1,
                AreaTriggerShapeType.Polygon => 3,
                AreaTriggerShapeType.Cylinder => 4,
                AreaTriggerShapeType.Disk => 7,
                AreaTriggerShapeType.BoundedPlane => 8,
                _ => 2
            };
            data.WriteInt32(pathType);
            data.WriteUInt8((byte)shapeType);
            PeriodModifier.GetValue().WriteCreate(data);

            if (pathType == 0)
            {
                var points = properties?.SplinePoints;
                int count = points?.Count ?? 0;
                data.WriteBits((uint)count, 16);
                data.WriteBit(false);
                data.FlushBits();
                if (points != null)
                    foreach (var point in points)
                        data.WriteVector3(point);
                data.FlushBits();
            }
            else if (pathType == 1)
            {
                AreaTriggerOrbitInfo orbit = owner.GetOrbit();
                data.WriteVector3(orbit?.Center ?? Vector3.Zero);
                data.WriteFloat(orbit?.Radius ?? 0.0f);
                data.WriteFloat(orbit?.InitialAngle ?? 0.0f);
                data.WriteFloat(orbit?.BlendFromRadius ?? 0.0f);
                data.WriteInt32(0);
                data.WriteBit(orbit?.CounterClockwise ?? false);
                data.FlushBits();
            }
            else if (pathType == 3)
            {
                data.WriteInt32(properties.ScriptId);
                data.WriteVector3(new Vector3(owner.GetPositionX(), owner.GetPositionY(), owner.GetPositionZ()));
                data.WriteUInt32((uint)owner.GetTimeSinceCreated());
            }

            WriteShapeCreate(data, shape, shapeType);
            bool hasTargetRollPitchYaw = properties != null && properties.Flags.HasFlag(AreaTriggerCreatePropertiesFlag.HasTargetRollPitchYaw);
            data.WriteBit(hasTargetRollPitchYaw);
            data.WriteBit(false); // ForcedPositionAndRotation is absent in this implementation.
            data.FlushBits();
            if (hasTargetRollPitchYaw)
                data.WriteVector3(owner.GetTargetRollPitchYaw());
        }

        static void WriteShapeCreate(WorldPacket data, AreaTriggerShapeInfo shape, uint shapeType)
        {
            unsafe
            {
                switch (shapeType)
                {
                    case 0:
                        data.WriteFloat(shape.SphereDatas.Radius);
                        data.WriteFloat(shape.SphereDatas.RadiusTarget);
                        break;
                    case 1:
                        for (int i = 0; i < 3; ++i) data.WriteFloat(shape.BoxDatas.Extents[i]);
                        for (int i = 0; i < 3; ++i) data.WriteFloat(shape.BoxDatas.ExtentsTarget[i]);
                        break;
                    case 3:
                    case 2:
                    case 5:
                    case 6:
                        data.WriteUInt32((uint)shape.PolygonVertices.Count);
                        data.WriteUInt32((uint)shape.PolygonVerticesTarget.Count);
                        data.WriteFloat(shape.PolygonDatas.Height);
                        data.WriteFloat(shape.PolygonDatas.HeightTarget);
                        foreach (var point in shape.PolygonVertices) data.WriteVector2(point);
                        foreach (var point in shape.PolygonVerticesTarget) data.WriteVector2(point);
                        break;
                    case 4:
                        data.WriteFloat(shape.CylinderDatas.Radius);
                        data.WriteFloat(shape.CylinderDatas.RadiusTarget);
                        data.WriteFloat(shape.CylinderDatas.Height);
                        data.WriteFloat(shape.CylinderDatas.HeightTarget);
                        data.WriteFloat(shape.CylinderDatas.LocationZOffset);
                        data.WriteFloat(shape.CylinderDatas.LocationZOffsetTarget);
                        break;
                    case 7:
                        data.WriteFloat(shape.DiskDatas.InnerRadius);
                        data.WriteFloat(shape.DiskDatas.InnerRadiusTarget);
                        data.WriteFloat(shape.DiskDatas.OuterRadius);
                        data.WriteFloat(shape.DiskDatas.OuterRadiusTarget);
                        data.WriteFloat(shape.DiskDatas.Height);
                        data.WriteFloat(shape.DiskDatas.HeightTarget);
                        data.WriteFloat(shape.DiskDatas.LocationZOffset);
                        data.WriteFloat(shape.DiskDatas.LocationZOffsetTarget);
                        break;
                    case 8:
                        for (int i = 0; i < 2; ++i) data.WriteFloat(shape.BoundedPlaneDatas.Extents[i]);
                        for (int i = 0; i < 2; ++i) data.WriteFloat(shape.BoundedPlaneDatas.ExtentsTarget[i]);
                        break;
                }
            }
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, AreaTrigger owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, AreaTrigger owner, Player receiver)
        {
            data.WriteUInt32(changesMask.GetBlock(0));
            data.WriteBits(changesMask.GetBlock(1), 7);
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1])
                {
                    OverrideScaleCurve.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                }
                if (changesMask[2]) ExtraScaleCurve.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[3]) OverrideMoveCurveX.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[4]) OverrideMoveCurveY.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[5]) OverrideMoveCurveZ.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[6]) OverrideShapeCurve.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[7])
                {
                    data.WritePackedGuid(Caster);
                }
                if (changesMask[8])
                {
                    data.WriteUInt32((uint)Duration.GetValue());
                }
                if (changesMask[9]) data.WriteUInt32((uint)TimeToTarget.GetValue());
                if (changesMask[10]) data.WriteUInt32((uint)TimeToTargetScale.GetValue());
                if (changesMask[11]) data.WriteUInt32((uint)TimeToTargetExtraScale.GetValue());
                if (changesMask[12]) data.WriteUInt32((uint)TimeToTargetPos.GetValue());
                if (changesMask[13]) data.WriteUInt32((uint)TimeToTargetShape.GetValue());
                if (changesMask[14]) data.WriteInt32(SpellID);
                if (changesMask[15]) data.WriteInt32(SpellForVisuals);
                if (changesMask[16]) SpellVisual.GetValue().Write(data);
                if (changesMask[17]) data.WriteFloat(BoundsRadius2D);
                if (changesMask[18]) data.WriteUInt32((uint)DecalPropertiesID.GetValue());
                if (changesMask[19]) data.WritePackedGuid(CreatingEffectGUID);
                if (changesMask[20]) data.WritePackedGuid(OrbitPathTarget);
                if (changesMask[21]) data.WriteVector3(owner.GetRollPitchYaw());
                if (changesMask[22]) data.WriteInt32(PositionalSoundKitID);
                if (changesMask[23]) data.WriteUInt32(MovementStartTime);
                if (changesMask[24]) data.WriteUInt32(CreationTime);
                if (changesMask[25]) data.WriteFloat(ZOffset);
                if (changesMask[27]) data.WriteUInt32(Flags);
                if (changesMask[28]) VisualAnim.GetValue().WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                if (changesMask[29]) data.WriteUInt32(ScaleCurveId);
                if (changesMask[30]) data.WriteUInt32(FacingCurveId);
                if (changesMask[31]) data.WriteUInt32(MorphCurveId);
            }
            if (changesMask[32])
            {
                if (changesMask[33]) data.WriteUInt32(MoveCurveId);
                if (changesMask[34]) data.WriteFloat(Facing);
                if (changesMask[36]) data.WriteInt32(PathType);
                if (changesMask[37]) data.WriteUInt8((byte)ShapeType.GetValue());
                if (changesMask[38]) PeriodModifier.GetValue().WriteUpdate(data, ignoreNestedChangesMask);
            }
            AreaTriggerCreateProperties properties = owner.GetCreateProperties();
            bool hasTargetRollPitchYaw = properties != null && properties.Flags.HasFlag(AreaTriggerCreatePropertiesFlag.HasTargetRollPitchYaw);
            if (changesMask[0]) data.WriteBit(hasTargetRollPitchYaw);
            if (changesMask[32]) data.WriteBit(false);
            data.FlushBits();
            if (changesMask[0] && changesMask[26] && hasTargetRollPitchYaw)
                data.WriteVector3(owner.GetTargetRollPitchYaw());
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(OverrideScaleCurve);
            ClearChangesMask(ExtraScaleCurve);
            ClearChangesMask(OverrideMoveCurveX);
            ClearChangesMask(OverrideMoveCurveY);
            ClearChangesMask(OverrideMoveCurveZ);
            ClearChangesMask(OverrideShapeCurve);
            ClearChangesMask(Caster);
            ClearChangesMask(Duration);
            ClearChangesMask(TimeToTarget);
            ClearChangesMask(TimeToTargetScale);
            ClearChangesMask(TimeToTargetExtraScale);
            ClearChangesMask(TimeToTargetPos);
            ClearChangesMask(TimeToTargetShape);
            ClearChangesMask(SpellID);
            ClearChangesMask(SpellForVisuals);
            ClearChangesMask(SpellVisual);
            ClearChangesMask(BoundsRadius2D);
            ClearChangesMask(DecalPropertiesID);
            ClearChangesMask(CreatingEffectGUID);
            ClearChangesMask(OrbitPathTarget);
            ClearChangesMask(RollPitchYaw);
            ClearChangesMask(PositionalSoundKitID);
            ClearChangesMask(MovementStartTime);
            ClearChangesMask(CreationTime);
            ClearChangesMask(ZOffset);
            ClearChangesMask(TargetRollPitchYaw);
            ClearChangesMask(Flags);
            ClearChangesMask(VisualAnim);
            ClearChangesMask(ScaleCurveId);
            ClearChangesMask(FacingCurveId);
            ClearChangesMask(MorphCurveId);
            ClearChangesMask(MoveCurveId);
            ClearChangesMask(Facing);
            ClearChangesMask(PathType);
            ClearChangesMask(ShapeType);
            ClearChangesMask(PeriodModifier);
            _changesMask.ResetAll();
        }
    }

    public class AreaTriggerPeriodModifier : HasChangesMask
    {
        public UpdateField<int> Field_0 = new(0, 1);
        public UpdateField<float> Field_4 = new(0, 2);
        static int changeMaskLength = 3;

        public AreaTriggerPeriodModifier() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data)
        {
            data.WriteInt32(Field_0);
            data.WriteFloat(Field_4);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask)
        {
            UpdateMask changesMask = _changesMask;
            if (ignoreChangesMask)
                changesMask.SetAll();

            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[1]) data.WriteInt32(Field_0);
                if (changesMask[2]) data.WriteFloat(Field_4);
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Field_0);
            ClearChangesMask(Field_4);
            _changesMask.ResetAll();
        }
    }

    public class SceneObjectData : HasChangesMask
    {
        public UpdateField<int> ScriptPackageID = new(0, 1);
        public UpdateField<uint> RndSeedVal = new(0, 2);
        public UpdateField<ObjectGuid> CreatedBy = new(0, 3);
        public UpdateField<uint> SceneType = new(0, 4);
        static int changeMaskLength = 5;

        public SceneObjectData() : base(changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, WorldObject owner, Player receiver)
        {
            data.WriteInt32(ScriptPackageID);
            data.WriteUInt32(RndSeedVal);
            data.WritePackedGuid(CreatedBy);
            data.WriteUInt32(SceneType);
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, WorldObject owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, WorldObject owner, Player receiver)
        {
            data.WriteBits(_changesMask.GetBlock(0), changeMaskLength);

            data.FlushBits();
            if (_changesMask[0])
            {
                if (_changesMask[1])
                {
                    data.WriteInt32(ScriptPackageID);
                }
                if (_changesMask[2])
                {
                    data.WriteUInt32(RndSeedVal);
                }
                if (_changesMask[3])
                {
                    data.WritePackedGuid(CreatedBy);
                }
                if (_changesMask[4])
                {
                    data.WriteUInt32(SceneType);
                }
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(ScriptPackageID);
            ClearChangesMask(RndSeedVal);
            ClearChangesMask(CreatedBy);
            ClearChangesMask(SceneType);
            _changesMask.ResetAll();
        }
    }

    public class ConversationLine
    {
        public int ConversationLineID;
        public uint StartTime;
        public int UiCameraID;
        public byte ActorIndex;
        public byte Flags;

        public void WriteCreate(WorldPacket data, Conversation owner, Player receiver)
        {
            data.WriteInt32(ConversationLineID);
            data.WriteUInt32(GetViewerStartTime(this, owner, receiver));
            data.WriteInt32(UiCameraID);
            data.WriteUInt8(ActorIndex);
            data.WriteUInt8(Flags);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Conversation owner, Player receiver)
        {
            data.WriteInt32(ConversationLineID);
            data.WriteUInt32(GetViewerStartTime(this, owner, receiver));
            data.WriteInt32(UiCameraID);
            data.WriteUInt8(ActorIndex);
            data.WriteUInt8(Flags);
        }

        public uint GetViewerStartTime(ConversationLine conversationLine, Conversation conversation, Player receiver)
        {
            uint startTime = conversationLine.StartTime;
            Locale locale = receiver.GetSession().GetSessionDbLocaleIndex();

            TimeSpan localizedStartTime = conversation.GetLineStartTime(locale, conversationLine.ConversationLineID);
            if (localizedStartTime != TimeSpan.Zero)
                startTime = (uint)localizedStartTime.TotalMilliseconds;

            return startTime;
        }
    }

    public class ConversationActorField
    {
        public ConversationActorType Type;
        public int Id;        
        public int CreatureID;
        public int CreatureDisplayInfoID;
        public ObjectGuid ActorGUID;

        public void WriteCreate(WorldPacket data, Conversation owner, Player receiver)
        {
            data.WriteBits((uint)Type, 1);
            data.WriteInt32(Id);
            if (Type == ConversationActorType.TalkingHead)
            {
                data.WriteInt32(CreatureID);
                data.WriteInt32(CreatureDisplayInfoID);
            }
            if (Type == ConversationActorType.WorldObject)
            {
                data.WritePackedGuid(ActorGUID);
            }
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Conversation owner, Player receiver)
        {
            data.WriteBits((uint)Type, 1);
            data.WriteInt32(Id);
            if (Type == ConversationActorType.TalkingHead)
            {
                data.WriteInt32(CreatureID);
                data.WriteInt32(CreatureDisplayInfoID);
            }
            if (Type == ConversationActorType.WorldObject)
            {
                data.WritePackedGuid(ActorGUID);
            }
            data.FlushBits();
        }
    }

    public class ConversationData : HasChangesMask
    {
        public UpdateField<bool> DontPlayBroadcastTextSounds = new(0, 1);
        public UpdateField<bool> Field_33 = new(0, 2);
        public UpdateField<List<ConversationLine>> Lines = new(0, 3);
        public DynamicUpdateField<ConversationActorField> Actors = new(0, 4);
        public UpdateField<int> LastLineEndTime = new(0, 5);
        public UpdateField<uint> Progress = new(0, 6);
        public UpdateField<uint> Flags = new(0, 7);
        static int changeMaskLength = 8;

        public ConversationData() : base(0, TypeId.Conversation, changeMaskLength) { }

        public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Conversation owner, Player receiver)
        {
            data.WriteInt32(Lines.GetValue().Count);
            data.WriteInt32(GetViewerLastLineEndTime(this, owner, receiver));
            data.WriteUInt32(Progress);
            for (int i = 0; i < Lines.GetValue().Count; ++i)
            {
                Lines.GetValue()[i].WriteCreate(data, owner, receiver);
            }
            data.WriteBit(DontPlayBroadcastTextSounds);
            data.WriteBit(Field_33);
            data.WriteInt32(Actors.Size());
            data.WriteUInt32(Flags);
            for (int i = 0; i < Actors.Size(); ++i)
            {
                Actors[i].WriteCreate(data, owner, receiver);
            }
            data.FlushBits();
        }

        public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Conversation owner, Player receiver)
        {
            WriteUpdate(data, _changesMask, false, owner, receiver);
        }

        public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, Conversation owner, Player receiver)
        {
            data.WriteBits(changesMask.GetBlock(0), changeMaskLength);

            if (changesMask[0])
            {
                if (changesMask[1])
                    data.WriteBit(DontPlayBroadcastTextSounds);
                if (changesMask[2])
                    data.WriteBit(Field_33);
                if (changesMask[3])
                {
                    List<ConversationLine> list = Lines;
                    data.WriteBits(list.Count, 32);
                    for (int i = 0; i < list.Count; ++i)
                    {
                        list[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                    }
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[4])
                {
                    if (!ignoreNestedChangesMask)
                        Actors.WriteUpdateMask(data);
                    else
                        WriteCompleteDynamicFieldUpdateMask(Actors.Size(), data);
                }
            }
            data.FlushBits();
            if (changesMask[0])
            {
                if (changesMask[4])
                {
                    for (int i = 0; i < Actors.Size(); ++i)
                    {
                        if (Actors.HasChanged(i) || ignoreNestedChangesMask)
                        {
                            Actors[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
                        }
                    }
                }
                if (changesMask[5])
                {
                    data.WriteInt32(GetViewerLastLineEndTime(this, owner, receiver));
                }
                if (changesMask[6])
                    data.WriteUInt32(Progress);
                if (changesMask[7])
                    data.WriteUInt32(Flags);
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(DontPlayBroadcastTextSounds);
            ClearChangesMask(Field_33);
            ClearChangesMask(Lines);
            ClearChangesMask(Actors);
            ClearChangesMask(LastLineEndTime);
            ClearChangesMask(Progress);
            ClearChangesMask(Flags);
            _changesMask.ResetAll();
        }

        public int GetViewerLastLineEndTime(ConversationData conversationLineData, Conversation conversation, Player receiver)
        {
            Locale locale = receiver.GetSession().GetSessionDbLocaleIndex();
            return (int)conversation.GetLastLineEndTime(locale).TotalMilliseconds;
        }
    }
    
    //public class AzeriteEmpoweredItemData : HasChangesMask
    //{
    //    public UpdateFieldArray<int> Selections = new(5, 0, 1);

    //    public AzeriteEmpoweredItemData() : base(0, TypeId.AzeriteEmpoweredItem, 6) { }

    //    public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, Item owner, Player receiver)
    //    {
    //        for (int i = 0; i < 5; ++i)
    //        {
    //            data.WriteInt32(Selections[i]);
    //        }
    //    }

    //    public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, AzeriteEmpoweredItem owner, Player receiver)
    //    {
    //        WriteUpdate(data, _changesMask, false, owner, receiver);
    //    }

    //    public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, AzeriteEmpoweredItem owner, Player receiver)
    //    {
    //        data.WriteBits(_changesMask.GetBlocksMask(0), 1);
    //        if (_changesMask.GetBlock(0) != 0)
    //            data.WriteBits(_changesMask.GetBlock(0), 32);

    //        data.FlushBits();
    //        if (_changesMask[0])
    //        {
    //            for (int i = 0; i < 5; ++i)
    //            {
    //                if (_changesMask[1 + i])
    //                {
    //                    data.WriteInt32(Selections[i]);
    //                }
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Selections);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class UnlockedAzeriteEssence
    //{
    //    public uint AzeriteEssenceID;
    //    public uint Rank;

    //    public void WriteCreate(WorldPacket data, AzeriteItem owner, Player receiver)
    //    {
    //        data.WriteUInt32(AzeriteEssenceID);
    //        data.WriteUInt32(Rank);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, AzeriteItem owner, Player receiver)
    //    {
    //        data.WriteUInt32(AzeriteEssenceID);
    //        data.WriteUInt32(Rank);
    //    }
    //}

    //public class SelectedAzeriteEssences : HasChangesMask
    //{
    //    public UpdateField<bool> Enabled = new(0, 1);
    //    public UpdateField<uint> SpecializationID = new(0, 2);
    //    public UpdateFieldArray<uint> AzeriteEssenceID = new(4, 3, 4);

    //    public SelectedAzeriteEssences() : base(8) { }

    //    public void WriteCreate(WorldPacket data, AzeriteItem owner, Player receiver)
    //    {
    //        for (int i = 0; i < 4; ++i)
    //        {
    //            data.WriteUInt32(AzeriteEssenceID[i]);
    //        }
    //        data.WriteUInt32(SpecializationID);
    //        data.WriteBit(Enabled);
    //        data.FlushBits();
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, AzeriteItem owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlocksMask(0), 1);
    //        if (changesMask.GetBlock(0) != 0)
    //            data.WriteBits(changesMask.GetBlock(0), 32);

    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WriteBit(Enabled);
    //            }
    //        }
    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[2])
    //            {
    //                data.WriteUInt32(SpecializationID);
    //            }
    //        }
    //        if (changesMask[3])
    //        {
    //            for (int i = 0; i < 4; ++i)
    //            {
    //                if (changesMask[4 + i])
    //                {
    //                    data.WriteUInt32(AzeriteEssenceID[i]);
    //                }
    //            }
    //        }

    //        data.FlushBits();
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Enabled);
    //        ClearChangesMask(SpecializationID);
    //        ClearChangesMask(AzeriteEssenceID);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class AzeriteItemData : HasChangesMask
    //{
    //    public UpdateField<bool> Enabled = new(0, 1);
    //    public DynamicUpdateField<UnlockedAzeriteEssence> UnlockedEssences = new(0, 2);
    //    public DynamicUpdateField<uint> UnlockedEssenceMilestones = new(0, 4);
    //    public DynamicUpdateField<SelectedAzeriteEssences> SelectedEssences = new(0, 3);
    //    public UpdateField<ulong> Xp = new(0, 5);
    //    public UpdateField<uint> Level = new(0, 6);
    //    public UpdateField<uint> AuraLevel = new(0, 7);
    //    public UpdateField<uint> KnowledgeLevel = new(0, 8);
    //    public UpdateField<int> DEBUGknowledgeWeek = new(0, 9);

    //    public AzeriteItemData() : base(10) { }

    //    public void WriteCreate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, AzeriteItem owner, Player receiver)
    //    {
    //        if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
    //        {
    //            data.WriteUInt64(Xp);
    //            data.WriteUInt32(Level);
    //            data.WriteUInt32(AuraLevel);
    //            data.WriteUInt32(KnowledgeLevel);
    //            data.WriteInt32(DEBUGknowledgeWeek);
    //        }
    //        data.WriteInt32(UnlockedEssences.Size());
    //        data.WriteInt32(SelectedEssences.Size());
    //        data.WriteInt32(UnlockedEssenceMilestones.Size());
    //        for (int i = 0; i < UnlockedEssences.Size(); ++i)
    //        {
    //            UnlockedEssences[i].WriteCreate(data, owner, receiver);
    //        }
    //        for (int i = 0; i < UnlockedEssenceMilestones.Size(); ++i)
    //        {
    //            data.WriteUInt32(UnlockedEssenceMilestones[i]);
    //        }
    //        if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
    //        {
    //            data.WriteBit(Enabled);
    //        }
    //        for (int i = 0; i < SelectedEssences.Size(); ++i)
    //        {
    //            SelectedEssences[i].WriteCreate(data, owner, receiver);
    //        }
    //        data.FlushBits();
    //    }

    //    public void WriteUpdate(WorldPacket data, UpdateFieldFlag fieldVisibilityFlags, AzeriteItem owner, Player receiver)
    //    {
    //        UpdateMask allowedMaskForTarget = new(10, new[] { 0x0000001Du });
    //        AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
    //        WriteUpdate(data, _changesMask & allowedMaskForTarget, false, owner, receiver);
    //    }

    //    public void AppendAllowedFieldsMaskForFlag(UpdateMask allowedMaskForTarget, UpdateFieldFlag fieldVisibilityFlags)
    //    {
    //        if (fieldVisibilityFlags.HasFlag(UpdateFieldFlag.Owner))
    //            allowedMaskForTarget.OR(new UpdateMask(10, new[] { 0x000003E2u }));
    //    }

    //    public void FilterDisallowedFieldsMaskForFlag(UpdateMask changesMask, UpdateFieldFlag fieldVisibilityFlags)
    //    {
    //        UpdateMask allowedMaskForTarget = new(10, new[] { 0x0000001Du });
    //        AppendAllowedFieldsMaskForFlag(allowedMaskForTarget, fieldVisibilityFlags);
    //        changesMask.AND(allowedMaskForTarget);
    //    }

    //    public void WriteUpdate(WorldPacket data, UpdateMask changesMask, bool ignoreNestedChangesMask, AzeriteItem owner, Player receiver)
    //    {
    //        data.WriteBits(changesMask.GetBlock(0), 10);

    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WriteBit(Enabled);
    //            }
    //            if (changesMask[2])
    //            {
    //                if (!ignoreNestedChangesMask)
    //                    UnlockedEssences.WriteUpdateMask(data);
    //                else
    //                    WriteCompleteDynamicFieldUpdateMask(UnlockedEssences.Size(), data);
    //            }
    //            if (changesMask[3])
    //            {
    //                if (!ignoreNestedChangesMask)
    //                    SelectedEssences.WriteUpdateMask(data);
    //                else
    //                    WriteCompleteDynamicFieldUpdateMask(SelectedEssences.Size(), data);
    //            }
    //            if (changesMask[4])
    //            {
    //                if (!ignoreNestedChangesMask)
    //                    UnlockedEssenceMilestones.WriteUpdateMask(data);
    //                else
    //                    WriteCompleteDynamicFieldUpdateMask(UnlockedEssenceMilestones.Size(), data);
    //            }
    //        }
    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[2])
    //            {
    //                for (int i = 0; i < UnlockedEssences.Size(); ++i)
    //                {
    //                    if (UnlockedEssences.HasChanged(i) || ignoreNestedChangesMask)
    //                    {
    //                        UnlockedEssences[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
    //                    }
    //                }
    //            }
    //            if (changesMask[4])
    //            {
    //                for (int i = 0; i < UnlockedEssenceMilestones.Size(); ++i)
    //                {
    //                    if (UnlockedEssenceMilestones.HasChanged(i) || ignoreNestedChangesMask)
    //                    {
    //                        data.WriteUInt32(UnlockedEssenceMilestones[i]);
    //                    }
    //                }
    //            }
    //            if (changesMask[3])
    //            {
    //                for (int i = 0; i < SelectedEssences.Size(); ++i)
    //                {
    //                    if (SelectedEssences.HasChanged(i) || ignoreNestedChangesMask)
    //                    {
    //                        SelectedEssences[i].WriteUpdate(data, ignoreNestedChangesMask, owner, receiver);
    //                    }
    //                }
    //            }
    //            if (changesMask[5])
    //            {
    //                data.WriteUInt64(Xp);
    //            }
    //            if (changesMask[6])
    //            {
    //                data.WriteUInt32(Level);
    //            }
    //            if (changesMask[7])
    //            {
    //                data.WriteUInt32(AuraLevel);
    //            }
    //            if (changesMask[8])
    //            {
    //                data.WriteUInt32(KnowledgeLevel);
    //            }
    //            if (changesMask[9])
    //            {
    //                data.WriteInt32(DEBUGknowledgeWeek);
    //            }
    //        }
    //        data.FlushBits();
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Enabled);
    //        ClearChangesMask(UnlockedEssences);
    //        ClearChangesMask(UnlockedEssenceMilestones);
    //        ClearChangesMask(SelectedEssences);
    //        ClearChangesMask(Xp);
    //        ClearChangesMask(Level);
    //        ClearChangesMask(AuraLevel);
    //        ClearChangesMask(KnowledgeLevel);
    //        ClearChangesMask(DEBUGknowledgeWeek);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class SpellCastVisualField
    //{
    //    public uint SpellXSpellVisualID;
    //    public uint ScriptVisualID;

    //    public void WriteCreate(WorldPacket data, WorldObject owner, Player receiver)
    //    {
    //        data.WriteUInt32(SpellXSpellVisualID);
    //        data.WriteUInt32(ScriptVisualID);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, WorldObject owner, Player receiver)
    //    {
    //        data.WriteUInt32(SpellXSpellVisualID);
    //        data.WriteUInt32(ScriptVisualID);
    //    }
    //}

    //public class GlyphInfo : BaseUpdateData<Player>
    //{
    //    public UpdateField<uint> GlyphSlot = new(0, 1);
    //    public UpdateField<uint> Glyph = new(0, 2);

    //    public GlyphInfo() : base(3) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteUInt32(GlyphSlot);
    //        data.WriteUInt32(Glyph);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(WorldMapOverlayIDs.Count);
    //        for (int i = 0; i < WorldMapOverlayIDs.Count; ++i)
    //        {
    //            data.WriteInt32(WorldMapOverlayIDs[i]);
    //        }
    //        data.FlushBits();
    //    }
    //}

    //public class RecipeProgressionInfo
    //{
    //    public ushort RecipeProgressionGroupID;
    //    public ushort Experience;

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteUInt16(RecipeProgressionGroupID);
    //        data.WriteUInt16(Experience);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        data.WriteUInt16(RecipeProgressionGroupID);
    //        data.WriteUInt16(Experience);
    //    }
    //}

    //public class ActivePlayerUnk901 : HasChangesMask
    //{
    //    public UpdateField<ObjectGuid> Field_0 = new(0, 1);
    //    public UpdateField<int> Field_10 = new(0, 2);

    //    public ActivePlayerUnk901() : base(3) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WritePackedGuid(Field_0);
    //        data.WriteInt32(Field_10);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 3);

    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WriteUInt32(GlyphSlot);
    //            }
    //            if (changesMask[2])
    //            {
    //                data.WriteUInt32(Glyph);
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(GlyphSlot);
    //        ClearChangesMask(Glyph);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class QuestSession : HasChangesMask
    //{
    //    public UpdateField<ObjectGuid> Owner = new(0, 1);
    //    public UpdateFieldArray<ulong> QuestCompleted = new(875, 2, 3);

    //    public QuestSession() : base(878) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WritePackedGuid(Owner);
    //        for (int i = 0; i < 875; ++i)
    //        {
    //            data.WriteUInt64(QuestCompleted[i]);
    //        }
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlocksMask(0), 28);
    //        for (uint i = 0; i < 28; ++i)
    //            if (changesMask.GetBlock(i) != 0)
    //                data.WriteBits(changesMask.GetBlock(i), 32);

    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WritePackedGuid(Owner);
    //            }
    //        }
    //        if (changesMask[2])
    //        {
    //            for (int i = 0; i < 875; ++i)
    //            {
    //                if (changesMask[3 + i])
    //                {
    //                    data.WriteUInt64(QuestCompleted[i]);
    //                }
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Owner);
    //        ClearChangesMask(QuestCompleted);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class ReplayedQuest : HasChangesMask
    //{
    //    public UpdateField<int> QuestID = new(0, 1);
    //    public UpdateField<uint> ReplayTime = new(0, 2);

    //    public ReplayedQuest() : base(3) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(QuestID);
    //        data.WriteUInt32(ReplayTime);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 3);

    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WriteInt32(QuestID);
    //            }
    //            if (changesMask[2])
    //            {
    //                data.WriteUInt32(ReplayTime);
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(QuestID);
    //        ClearChangesMask(ReplayTime);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class CollectableSourceTrackedData : HasChangesMask
    //{
    //    public UpdateField<int> TargetType = new(0, 1);
    //    public UpdateField<int> TargetID = new(0, 2);
    //    public UpdateField<int> CollectableSourceInfoID = new(0, 3);

    //    public CollectableSourceTrackedData() : base(4) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(TargetType);
    //        data.WriteInt32(TargetID);
    //        data.WriteInt32(CollectableSourceInfoID);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 4);

    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                data.WriteInt32(TargetType);
    //            }
    //            if (changesMask[2])
    //            {
    //                data.WriteInt32(TargetID);
    //            }
    //            if (changesMask[3])
    //            {
    //                data.WriteInt32(CollectableSourceInfoID);
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(TargetType);
    //        ClearChangesMask(TargetID);
    //        ClearChangesMask(CollectableSourceInfoID);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class CraftingOrderItem : HasChangesMask
    //{
    //    public UpdateField<ulong> Field_0 = new(-1, 0);
    //    public UpdateField<ObjectGuid> ItemGUID = new(-1, 1);
    //    public UpdateField<ObjectGuid> OwnerGUID = new(-1, 2);
    //    public UpdateField<int> ItemID = new(-1, 3);
    //    public UpdateField<uint> Quantity = new(-1, 4);
    //    public UpdateField<int> ReagentQuality = new(-1, 5);
    //    public OptionalUpdateField<byte> DataSlotIndex = new(-1, 6);

    //    public CraftingOrderItem() : base(7) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteUInt64(Field_0);
    //        data.WritePackedGuid(ItemGUID);
    //        data.WritePackedGuid(OwnerGUID);
    //        data.WriteInt32(ItemID);
    //        data.WriteUInt32(Quantity);
    //        data.WriteInt32(ReagentQuality);
    //        data.WriteBits(DataSlotIndex.HasValue(), 1);
    //        data.FlushBits();
    //        if (DataSlotIndex.HasValue())
    //        {
    //            data.WriteUInt8(DataSlotIndex);
    //        }
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 7);

    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            data.WriteUInt64(Field_0);
    //        }
    //        if (changesMask[1])
    //        {
    //            data.WritePackedGuid(ItemGUID);
    //        }
    //        if (changesMask[2])
    //        {
    //            data.WritePackedGuid(OwnerGUID);
    //        }
    //        if (changesMask[3])
    //        {
    //            data.WriteInt32(ItemID);
    //        }
    //        if (changesMask[4])
    //        {
    //            data.WriteUInt32(Quantity);
    //        }
    //        if (changesMask[5])
    //        {
    //            data.WriteInt32(ReagentQuality);
    //        }
    //        data.WriteBits(DataSlotIndex.HasValue(), 1);
    //        data.FlushBits();
    //        if (changesMask[6])
    //        {
    //            if (DataSlotIndex.HasValue())
    //            {
    //                data.WriteUInt8(DataSlotIndex);
    //            }
    //        }
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Field_0);
    //        ClearChangesMask(ItemGUID);
    //        ClearChangesMask(OwnerGUID);
    //        ClearChangesMask(ItemID);
    //        ClearChangesMask(Quantity);
    //        ClearChangesMask(ReagentQuality);
    //        ClearChangesMask(DataSlotIndex);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class CraftingOrderData : HasChangesMask
    //{
    //    public DynamicUpdateField<CraftingOrderItem> Reagents = new(0, 1);
    //    public UpdateField<int> Field_0 = new(0, 2);
    //    public UpdateField<ulong> OrderID = new(0, 3);
    //    public UpdateField<int> SkillLineAbilityID = new(0, 4);
    //    public UpdateField<byte> OrderState = new(5, 6);
    //    public UpdateField<byte> OrderType = new(5, 7);
    //    public UpdateField<byte> MinQuality = new(5, 8);
    //    public UpdateField<long> ExpirationTime = new(5, 9);
    //    public UpdateField<long> ClaimEndTime = new(10, 11);
    //    public UpdateField<long> TipAmount = new(10, 12);
    //    public UpdateField<long> ConsortiumCut = new(10, 13);
    //    public UpdateField<uint> Flags = new(10, 14);
    //    public UpdateField<ObjectGuid> CustomerGUID = new(15, 16);
    //    public UpdateField<ObjectGuid> CustomerAccountGUID = new(15, 17);
    //    public UpdateField<ObjectGuid> CrafterGUID = new(15, 18);
    //    public UpdateField<ObjectGuid> PersonalCrafterGUID = new(15, 19);
    //    public UpdateFieldString CustomerNotes = new(20, 21);
    //    public OptionalUpdateField<CraftingOrderItem> OutputItem = new(20, 22);
    //    public OptionalUpdateField<ItemInstance> OutputItemData = new(20, 23);

    //    public CraftingOrderData() : base(24) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(Field_0);
    //        data.WriteUInt64(OrderID);
    //        data.WriteInt32(SkillLineAbilityID);
    //        data.WriteUInt8(OrderState);
    //        data.WriteUInt8(OrderType);
    //        data.WriteUInt8(MinQuality);
    //        data.WriteInt64(ExpirationTime);
    //        data.WriteInt64(ClaimEndTime);
    //        data.WriteInt64(TipAmount);
    //        data.WriteInt64(ConsortiumCut);
    //        data.WriteUInt32(Flags);
    //        data.WritePackedGuid(CustomerGUID);
    //        data.WritePackedGuid(CustomerAccountGUID);
    //        data.WritePackedGuid(CrafterGUID);
    //        data.WritePackedGuid(PersonalCrafterGUID);
    //        data.WriteInt32(Reagents.Size());
    //        data.WriteBits(CustomerNotes.GetValue().GetByteCount(), 10);
    //        data.WriteBits(OutputItem.HasValue(), 1);
    //        data.WriteBits(OutputItemData.HasValue(), 1);
    //        data.FlushBits();
    //        for (int i = 0; i < Reagents.Size(); ++i)
    //        {
    //            Reagents[i].WriteCreate(data, owner, receiver);
    //        }
    //        data.WriteString(CustomerNotes);
    //        if (OutputItem.HasValue())
    //        {
    //            OutputItem.GetValue().WriteCreate(data, owner, receiver);
    //        }
    //        if (OutputItemData.HasValue())
    //        {
    //            OutputItemData.GetValue().Write(data);
    //        }
    //        data.FlushBits();
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 24);

    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                if (!ignoreChangesMask)
    //                    Reagents.WriteUpdateMask(data);
    //                else
    //                    WriteCompleteDynamicFieldUpdateMask(Reagents.Size(), data);
    //            }
    //        }
    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            if (changesMask[1])
    //            {
    //                for (int i = 0; i < Reagents.Size(); ++i)
    //                {
    //                    if (Reagents.HasChanged(i) || ignoreChangesMask)
    //                    {
    //                        Reagents[i].WriteUpdate(data, ignoreChangesMask, owner, receiver);
    //                    }
    //                }
    //            }
    //            if (changesMask[2])
    //            {
    //                data.WriteInt32(Field_0);
    //            }
    //            if (changesMask[3])
    //            {
    //                data.WriteUInt64(OrderID);
    //            }
    //            if (changesMask[4])
    //            {
    //                data.WriteInt32(SkillLineAbilityID);
    //            }
    //        }
    //        if (changesMask[5])
    //        {
    //            if (changesMask[6])
    //            {
    //                data.WriteUInt8(OrderState);
    //            }
    //            if (changesMask[7])
    //            {
    //                data.WriteUInt8(OrderType);
    //            }
    //            if (changesMask[8])
    //            {
    //                data.WriteUInt8(MinQuality);
    //            }
    //            if (changesMask[9])
    //            {
    //                data.WriteInt64(ExpirationTime);
    //            }
    //        }
    //        if (changesMask[10])
    //        {
    //            if (changesMask[11])
    //            {
    //                data.WriteInt64(ClaimEndTime);
    //            }
    //            if (changesMask[12])
    //            {
    //                data.WriteInt64(TipAmount);
    //            }
    //            if (changesMask[13])
    //            {
    //                data.WriteInt64(ConsortiumCut);
    //            }
    //            if (changesMask[14])
    //            {
    //                data.WriteUInt32(Flags);
    //            }
    //        }
    //        if (changesMask[15])
    //        {
    //            if (changesMask[16])
    //            {
    //                data.WritePackedGuid(CustomerGUID);
    //            }
    //            if (changesMask[17])
    //            {
    //                data.WritePackedGuid(CustomerAccountGUID);
    //            }
    //            if (changesMask[18])
    //            {
    //                data.WritePackedGuid(CrafterGUID);
    //            }
    //            if (changesMask[19])
    //            {
    //                data.WritePackedGuid(PersonalCrafterGUID);
    //            }
    //        }
    //        if (changesMask[20])
    //        {
    //            if (changesMask[21])
    //            {
    //                data.WriteBits(CustomerNotes.GetValue().GetByteCount(), 10);
    //                data.WriteString(CustomerNotes);
    //            }
    //            data.WriteBits(OutputItem.HasValue(), 1);
    //            data.WriteBits(OutputItemData.HasValue(), 1);
    //            data.FlushBits();
    //            if (changesMask[22])
    //            {
    //                if (OutputItem.HasValue())
    //                {
    //                    OutputItem.GetValue().WriteUpdate(data, ignoreChangesMask, owner, receiver);
    //                }
    //            }

    //            if (changesMask[23])
    //            {

    //                if (OutputItemData.HasValue())
    //                {
    //                    OutputItemData.GetValue().Write(data);
    //                }
    //            }
    //        }
    //        data.FlushBits();
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Reagents);
    //        ClearChangesMask(Field_0);
    //        ClearChangesMask(OrderID);
    //        ClearChangesMask(SkillLineAbilityID);
    //        ClearChangesMask(OrderState);
    //        ClearChangesMask(OrderType);
    //        ClearChangesMask(MinQuality);
    //        ClearChangesMask(ExpirationTime);
    //        ClearChangesMask(ClaimEndTime);
    //        ClearChangesMask(TipAmount);
    //        ClearChangesMask(ConsortiumCut);
    //        ClearChangesMask(Flags);
    //        ClearChangesMask(CustomerGUID);
    //        ClearChangesMask(CustomerAccountGUID);
    //        ClearChangesMask(CrafterGUID);
    //        ClearChangesMask(PersonalCrafterGUID);
    //        ClearChangesMask(CustomerNotes);
    //        ClearChangesMask(OutputItem);
    //        ClearChangesMask(OutputItemData);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class CraftingOrder : HasChangesMask
    //{
    //    public DynamicUpdateField<ItemEnchantData> Enchantments = new(-1, 0);
    //    public DynamicUpdateField<ItemGemData> Gems = new(-1, 1);
    //    public UpdateField<CraftingOrderData> Data = new(-1, 2);
    //    public OptionalUpdateField<ItemInstance> RecraftItemInfo = new(-1, 3);

    //    public CraftingOrder() : base(4) { }

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        Data.GetValue().WriteCreate(data, owner, receiver);
    //        data.WriteBits(RecraftItemInfo.HasValue(), 1);
    //        data.WriteBits(Enchantments.Size(), 4);
    //        data.WriteBits(Gems.Size(), 2);
    //        data.FlushBits();
    //        if (RecraftItemInfo.HasValue())
    //        {
    //            RecraftItemInfo.GetValue().Write(data);
    //        }
    //        for (int i = 0; i < Enchantments.Size(); ++i)
    //        {
    //            Enchantments[i].Write(data);
    //        }
    //        for (int i = 0; i < Gems.Size(); ++i)
    //        {
    //            Gems[i].Write(data);
    //        }
    //        data.FlushBits();
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        UpdateMask changesMask = _changesMask;
    //        if (ignoreChangesMask)
    //            changesMask.SetAll();

    //        data.WriteBits(changesMask.GetBlock(0), 4);

    //        if (changesMask[0])
    //        {
    //            if (!ignoreChangesMask)
    //                Enchantments.WriteUpdateMask(data, 4);
    //            else
    //                WriteCompleteDynamicFieldUpdateMask(Enchantments.Size(), data, 4);
    //        }
    //        if (changesMask[1])
    //        {
    //            if (!ignoreChangesMask)
    //                Gems.WriteUpdateMask(data, 2);
    //            else
    //                WriteCompleteDynamicFieldUpdateMask(Gems.Size(), data, 2);
    //        }
    //        data.FlushBits();
    //        if (changesMask[0])
    //        {
    //            for (int i = 0; i < Enchantments.Size(); ++i)
    //            {
    //                if (Enchantments.HasChanged(i) || ignoreChangesMask)
    //                {
    //                    Enchantments[i].Write(data);
    //                }
    //            }
    //        }
    //        if (changesMask[1])
    //        {
    //            for (int i = 0; i < Gems.Size(); ++i)
    //            {
    //                if (Gems.HasChanged(i) || ignoreChangesMask)
    //                {
    //                    Gems[i].Write(data);
    //                }
    //            }
    //        }

    //        if (changesMask[2])
    //        {
    //            Data.GetValue().WriteUpdate(data, ignoreChangesMask, owner, receiver);
    //        }
    //        data.WriteBits(RecraftItemInfo.HasValue(), 1);
    //        data.FlushBits();
    //        if (changesMask[3])
    //        {
    //            if (RecraftItemInfo.HasValue())
    //            {
    //                RecraftItemInfo.GetValue().Write(data);
    //            }
    //        }
    //        data.FlushBits();
    //    }

    //    public override void ClearChangesMask()
    //    {
    //        ClearChangesMask(Enchantments);
    //        ClearChangesMask(Gems);
    //        ClearChangesMask(Data);
    //        ClearChangesMask(RecraftItemInfo);
    //        _changesMask.ResetAll();
    //    }
    //}

    //public class PersonalCraftingOrderCount : IEquatable<PersonalCraftingOrderCount>
    //{
    //    public int ProfessionID;
    //    public uint Count;

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(ProfessionID);
    //        data.WriteUInt32(Count);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        data.WriteInt32(ProfessionID);
    //        data.WriteUInt32(Count);
    //    }

    //    public bool Equals(PersonalCraftingOrderCount right)
    //    {
    //        return ProfessionID == right.ProfessionID && Count == right.Count;
    //    }
    //}

    //public class CTROptions
    //{
    //    public uint ContentTuningConditionMask;
    //    public uint Field_4;
    //    public uint ExpansionLevelMask;

    //    public void WriteCreate(WorldPacket data, Player owner, Player receiver)
    //    {
    //        data.WriteUInt32(ContentTuningConditionMask);
    //        data.WriteUInt32(Field_4);
    //        data.WriteUInt32(ExpansionLevelMask);
    //    }

    //    public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Player owner, Player receiver)
    //    {
    //        data.WriteUInt32(ContentTuningConditionMask);
    //        data.WriteUInt32(Field_4);
    //        data.WriteUInt32(ExpansionLevelMask);
    //    }
    //}
}
