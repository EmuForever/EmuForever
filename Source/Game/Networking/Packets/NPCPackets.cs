// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Networking.Packets
{
    // CMSG_BANKER_ACTIVATE
    // CMSG_BINDER_ACTIVATE
    // CMSG_BINDER_CONFIRM
    // CMSG_GOSSIP_HELLO
    // CMSG_LIST_INVENTORY
    // CMSG_TRAINER_LIST
    // CMSG_BATTLEMASTER_HELLO
    public class Hello : ClientPacket
    {
        public Hello(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Unit = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid Unit;
    }

    public class NPCInteractionOpenResult : ServerPacket
    {
        public NPCInteractionOpenResult() : base(ServerOpcodes.NpcInteractionOpenResult) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(Npc);
            _worldPacket.WriteInt32((int)InteractionType);
            _worldPacket.WriteBit(Success);
            _worldPacket.FlushBits();
        }

        public ObjectGuid Npc;
        public PlayerInteractionType InteractionType;
        public bool Success = true;
    }
    
    public class GossipMessagePkt : ServerPacket
    {
        public GossipMessagePkt() : base(ServerOpcodes.GossipMessage) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GossipGUID);
            // Forever layout: TC GossipMessage::Write
            _worldPacket.WriteInt32(GossipID);
            _worldPacket.WriteInt32(LfgDungeonsID);
            _worldPacket.WriteInt32(FriendshipFactionID);
            _worldPacket.WriteInt32(GossipOptions.Count);
            _worldPacket.WriteInt32(GossipText.Count);

            foreach (ClientGossipOptions options in GossipOptions)
                options.Write(_worldPacket);

            foreach (ClientGossipText text in GossipText)
                text.Write(_worldPacket);

            _worldPacket.WriteBit(TextID.HasValue);
            _worldPacket.WriteBit(BroadcastTextID.HasValue);
            _worldPacket.FlushBits();

            if (TextID.HasValue)
                _worldPacket.WriteInt32(TextID.Value);

            if (BroadcastTextID.HasValue)
                _worldPacket.WriteInt32(BroadcastTextID.Value);
        }

        public List<ClientGossipOptions> GossipOptions = new();
        public int FriendshipFactionID;
        public ObjectGuid GossipGUID;
        public List<ClientGossipText> GossipText = new();
        public int? TextID; // in classic variants this still holds npc_text id
        public int? BroadcastTextID;
        public int GossipID;
        public int LfgDungeonsID;
    }

    public class GossipSelectOption : ClientPacket
    {
        public GossipSelectOption(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            GossipUnit = _worldPacket.ReadPackedGuid();
            GossipID = _worldPacket.ReadInt32();
            GossipOptionID = _worldPacket.ReadInt32();

            int length = _worldPacket.ReadBits<int>(8);
            PromotionCode = _worldPacket.ReadString(length);
        }

        public ObjectGuid GossipUnit;
        public int GossipOptionID;
        public int GossipID;
        public string PromotionCode;
    }

    class GossipOptionNPCInteraction : ServerPacket
    {
        public GossipOptionNPCInteraction() : base(ServerOpcodes.GossipOptionNpcInteraction) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GossipGUID);
            _worldPacket.WriteInt32(GossipNpcOptionID);
            _worldPacket.WriteBit(FriendshipFactionID.HasValue);
            _worldPacket.FlushBits();

            if (FriendshipFactionID.HasValue)
                _worldPacket.WriteInt32(FriendshipFactionID.Value);
        }

        public ObjectGuid GossipGUID;
        public int GossipNpcOptionID;
        public int? FriendshipFactionID;
    }
    
    public class GossipComplete : ServerPacket
    {
        public bool SuppressSound;

        public GossipComplete() : base(ServerOpcodes.GossipComplete) { }

        public override void Write()
        {
            _worldPacket.WriteBit(SuppressSound);
            _worldPacket.FlushBits();
        }
    }

    public class VendorInventory : ServerPacket
    {
        public VendorInventory() : base(ServerOpcodes.VendorInventory) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(Vendor);
            // Forever layout: TC VendorInventory::Write
            _worldPacket.WriteInt32(Reason);
            _worldPacket.WriteInt32(Items.Count);

            foreach (VendorItemPkt item in Items)
                item.Write(_worldPacket);
        }

        public byte Reason = 0;
        public List<VendorItemPkt> Items = new();
        public ObjectGuid Vendor;
    }

    public class TrainerList : ServerPacket
    {
        public TrainerList() : base(ServerOpcodes.TrainerList) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(TrainerGUID);
            // Forever layout: TC TrainerList::Write
            _worldPacket.WriteInt8((sbyte)TrainerType);
            _worldPacket.WriteUInt32((uint)TrainerID);

            _worldPacket.WriteInt32(Spells.Count);
            foreach (TrainerListSpell spell in Spells)
            {
                // Classic 1.60.1: 33 bytes per spell; int32 at +4 stays 0 (client derives usable state itself)
                _worldPacket.WriteInt32(spell.SpellID);
                _worldPacket.WriteInt32(0);
                _worldPacket.WriteUInt32(spell.MoneyCost);
                _worldPacket.WriteUInt8(spell.ReqLevel);
                _worldPacket.WriteUInt32((uint)spell.ReqSkillLine);
                _worldPacket.WriteUInt32((uint)spell.ReqSkillRank);

                for (int i = 0; i < 3; ++i)
                    _worldPacket.WriteInt32(i < spell.ReqAbility.Length ? spell.ReqAbility[i] : 0);
            }

            _worldPacket.WriteBits(Greeting.GetByteCount(), 11);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(Greeting);
        }

        public ObjectGuid TrainerGUID;
        public int TrainerType;
        public int TrainerID = 1;
        public List<TrainerListSpell> Spells = new();
        public string Greeting;
    }

    class GossipPOI : ServerPacket
    {
        public GossipPOI() : base(ServerOpcodes.GossipPoi) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Id);
            _worldPacket.WriteUInt32(Flags);
            _worldPacket.WriteVector3(Pos);
            _worldPacket.WriteInt32(Icon);
            _worldPacket.WriteInt32(Importance);
            _worldPacket.WriteInt32(WMOGroupID);
            _worldPacket.WriteBits(Name.GetByteCount(), 6);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(Name);
        }

        public int Id;
        public uint Flags;
        public Vector3 Pos;
        public int Icon;
        public int Importance;
        public int WMOGroupID;
        public string Name;
    }

    class SpiritHealerActivate : ClientPacket
    {
        public SpiritHealerActivate(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Healer = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid Healer;
    }

    class TabardVendorActivate : ClientPacket
    {
        public TabardVendorActivate(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Vendor = _worldPacket.ReadPackedGuid();
            Type = _worldPacket.ReadInt32();
        }

        public ObjectGuid Vendor;
        public int Type;
    }

    class TrainerBuySpell : ClientPacket
    {
        public TrainerBuySpell(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            TrainerGUID = _worldPacket.ReadPackedGuid();
            TrainerID = _worldPacket.ReadInt32();
            SpellID= _worldPacket.ReadInt32();
        }

        public ObjectGuid TrainerGUID;
        public int TrainerID;
        public int SpellID;
    }

    class TrainerBuyFailed : ServerPacket
    {
        public TrainerBuyFailed() : base(ServerOpcodes.TrainerBuyFailed) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(TrainerGUID);
            _worldPacket.WriteInt32(SpellID);
            _worldPacket.WriteInt32((int)TrainerFailedReason);
        }

        public ObjectGuid TrainerGUID;
        public int SpellID;
        public TrainerFailReason TrainerFailedReason;
    }

    class RequestStabledPets : ClientPacket
    {
        public RequestStabledPets(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            StableMaster = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid StableMaster;
    }

    class SetPetSlot : ClientPacket
    {
        public SetPetSlot(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            PetNumber = _worldPacket.ReadInt32();
            DestSlot = _worldPacket.ReadUInt8();
            StableMaster = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid StableMaster;
        public int PetNumber;
        public byte DestSlot;
    }
    
    //Structs
    public struct TreasureItem
    {
        public GossipOptionRewardType Type;
        public int ID;
        public int Quantity;
        public sbyte ItemContext;

        // Forever layout: TC operator<<(TreasureItem)
        public void Write(WorldPacket data)
        {
            data.WriteBits((byte)Type, 1);
            data.WriteInt32(ID);
            data.WriteInt32(Quantity);
            data.WriteInt8(ItemContext);
        }
    }

    public class TreasureLootList
    {
        public List<TreasureItem> Items = new();

        public void Write(WorldPacket data)
        {
            data.WriteInt32(Items.Count);
            foreach (TreasureItem treasureItem in Items)
                treasureItem.Write(data);
        }
    }

    public class ClientGossipOptions
    {
        public int GossipOptionID;
        public GossipOptionNpc OptionNPC;
        public byte OptionFlags;
        public int OptionCost;
        public int OptionLanguage;
        public GossipOptionFlags Flags;
        public int OrderIndex;
        public GossipOptionStatus Status;
        public string Text = "";
        public string Confirm = "";
        public TreasureLootList Treasure = new();
        public int? SpellID;
        public int? OverrideIconID;
        public string FailureDescription = "";

        // Forever layout: TC operator<<(ClientGossipOptions)
        public void Write(WorldPacket data)
        {
            data.WriteInt32(GossipOptionID);
            data.WriteUInt32((uint)OptionNPC);
            data.WriteInt8((sbyte)OptionFlags);
            data.WriteUInt64((ulong)OptionCost);
            data.WriteUInt32((uint)OptionLanguage);
            Treasure.Write(data);
            data.WriteInt32((int)Flags);
            data.WriteInt32(OrderIndex);
            data.WriteBits(Text.GetByteCount(), 12);
            data.WriteBits(Confirm.GetByteCount(), 12);
            data.WriteBits((byte)Status, 2);
            data.WriteBit(SpellID.HasValue);
            data.WriteBit(OverrideIconID.HasValue);
            data.WriteBits((FailureDescription ?? "").GetByteCount() + 1, 8); // SizedCString: length incl. terminator
            data.FlushBits();

            data.WriteString(Text);
            data.WriteString(Confirm);

            if (SpellID.HasValue)
                data.WriteInt32(SpellID.Value);

            if (OverrideIconID.HasValue)
                data.WriteInt32(OverrideIconID.Value);

            // TC SizedCString::Data writes nothing for an empty string (its size is still 1); a stray terminator here
            // shifted every following option, so later gossip options lost their text.
            if (!string.IsNullOrEmpty(FailureDescription))
                data.WriteCString(FailureDescription);
        }
    }

    public class ClientGossipText
    {
        // Forever layout: TC operator<<(ClientGossipText)
        public void Write(WorldPacket data)
        {
            data.WriteInt32(QuestID);
            data.WriteInt32(ContentTuningID);
            data.WriteInt32(QuestType);
            data.WriteInt32(QuestInfoID);
            data.WriteUInt32((uint)QuestFlags);
            data.WriteUInt32((uint)QuestFlagsEx);
            data.WriteInt32(QuestFlags2);
            data.WriteInt32(QuestFlags3);

            data.WriteBit(Repeatable);
            data.WriteBit(ResetByScheduler);
            data.WriteBit(Important);
            data.WriteBit(Meta);
            data.WriteBits(QuestTitle.GetByteCount(), 9);
            data.FlushBits();

            data.WriteString(QuestTitle);
        }
        
        public int QuestID;
        public int ContentTuningID;
        public int QuestType;
        public int QuestLevel;
        public int QuestMaxScalingLevel; // not sent in Forever layout
        public int QuestInfoID;
        public int QuestFlags2;
        public int QuestFlags3;
        public bool ResetByScheduler;
        public bool Meta;
        public bool Repeatable;
        public bool Important;
        public string QuestTitle;
        public QuestFlags QuestFlags;
        public QuestFlagsEx QuestFlagsEx;
    }

    public class VendorItemPkt
    {
        public void Write(WorldPacket data)
        {
            data.WriteUInt64(Price);
            // Forever layout: TC operator<<(VendorItem)
            data.WriteUInt32((uint)MuID);
            data.WriteInt32(Type);
            data.WriteInt32(StackCount);
            data.WriteInt32(Quantity);
            data.WriteInt32(ExtendedCostID);
            data.WriteInt32(PlayerConditionFailed);
            Item.Write(data);
            data.WriteBit(Locked);
            data.WriteBit(DoNotFilterOnVendor);
            data.WriteBit(Refundable);
            data.FlushBits();
        }

        public int MuID;
        public int Type;
        public ItemInstance Item = new();
        public int Quantity = -1;
        public ulong Price;
        public int Durability;
        public int StackCount;
        public int ExtendedCostID;
        public int PlayerConditionFailed;
        public bool Locked;
        public bool DoNotFilterOnVendor;
        public bool Refundable;
    }

    public class TrainerListSpell
    {
        public int SpellID;
        public uint MoneyCost;
        public SkillType ReqSkillLine;
        public int ReqSkillRank;
        public int[] ReqAbility = new int[SharedConst.MaxTrainerspellAbilityReqs];
        public TrainerSpellState Usable;
        public byte ReqLevel;
    }
}
