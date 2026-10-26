// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Game.Entities;
using Framework.Constants;

namespace Game.Networking.Packets
{
    public class BankerActivate : ClientPacket
    {
        public BankerActivate(WorldPacket packet) : base(packet) { }
        public ObjectGuid Banker;
        public PlayerInteractionType InteractionType;
        public override void Read()
        {
            Banker = _worldPacket.ReadPackedGuid();
            InteractionType = _worldPacket.GetSize() - _worldPacket.Position >= 4
                ? (PlayerInteractionType)_worldPacket.ReadInt32() : PlayerInteractionType.CharacterBanker;
        }
    }

    public class BuyBankTab : ClientPacket
    {
        public BuyBankTab(WorldPacket packet) : base(packet) { }
        public ObjectGuid Banker;
        public byte BankType;
        public override void Read() { Banker = _worldPacket.ReadPackedGuid(); BankType = _worldPacket.ReadUInt8(); }
    }

    public class AutoDepositCharacterBank : ClientPacket
    {
        public AutoDepositCharacterBank(WorldPacket packet) : base(packet) { }
        public ObjectGuid Banker;
        public override void Read() { Banker = _worldPacket.ReadPackedGuid(); }
    }

    public class UpdateBankTabSettings : ClientPacket
    {
        public UpdateBankTabSettings(WorldPacket packet) : base(packet) { }
        public ObjectGuid Banker;
        public byte BankType, Tab;
        public string Name, Icon, Description;
        public BagSlotFlags DepositFlags;
        public override void Read()
        {
            Banker = _worldPacket.ReadPackedGuid();
            BankType = _worldPacket.ReadUInt8(); Tab = _worldPacket.ReadUInt8();
            int nameLength = _worldPacket.ReadBits<int>(7);
            int iconLength = _worldPacket.ReadBits<int>(9);
            int descriptionLength = _worldPacket.ReadBits<int>(14);
            DepositFlags = (BagSlotFlags)_worldPacket.ReadInt32();
            Name = _worldPacket.ReadString(nameLength);
            Icon = _worldPacket.ReadString(iconLength);
            Description = _worldPacket.ReadString(descriptionLength);
        }
    }

    public class AutoBankItem : ClientPacket
    {
        public InvUpdate Inv;
        public byte Bag;
        public byte Slot;

        public byte BankType;

        public AutoBankItem(WorldPacket packet) : base(packet) { }

        // Forever layout: TC AutoBankItem::Read
        public override void Read()
        {
            Inv = new InvUpdate(_worldPacket);
            BankType = _worldPacket.ReadUInt8();
            Bag = _worldPacket.ReadUInt8();
            Slot = _worldPacket.ReadUInt8();
            ClassicInventorySlots.ToServer(ref Bag, ref Slot);
        }
    }

    public class AutoStoreBankItem : ClientPacket
    {
        public InvUpdate Inv;
        public byte Bag;
        public byte Slot;

        public AutoStoreBankItem(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Inv = new InvUpdate(_worldPacket);
            Bag = _worldPacket.ReadUInt8();
            Slot = _worldPacket.ReadUInt8();
            ClassicInventorySlots.ToServer(ref Bag, ref Slot);
        }
    }

    public class BuyBankSlot : ClientPacket
    {
        public BuyBankSlot(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Guid = _worldPacket.ReadPackedGuid();
        }

        public ObjectGuid Guid;
    }
}
