using Game.Networking;
using System;

namespace Game.Entities
{
    // UF::UnitAssistActionData, UpdateFields.h / UpdateFields.cpp.
    public class UnitAssistActionData : HasChangesMask
    {
        public UpdateField<byte> Type = new(0, 1);
        public UpdateFieldString PlayerName = new(0, 2);
        public UpdateField<uint> VirtualRealmAddress = new(0, 3);

        public UnitAssistActionData() : base(4) { }

        public void WriteCreate(WorldPacket data, Unit owner, Player receiver)
        {
            data.WriteUInt8(Type);
            data.WriteUInt32(VirtualRealmAddress);
            data.WriteBits(PlayerName.GetValue().GetByteCount(), 6);
            data.FlushBits();
            data.WriteString(PlayerName);
        }

        public void WriteUpdate(WorldPacket data, bool ignoreChangesMask, Unit owner, Player receiver)
        {
            UpdateMask mask = new(4);
            mask.OR(_changesMask);
            if (ignoreChangesMask) mask.SetAll();
            data.WriteBits(mask.GetBlock(0), 4);
            data.FlushBits();
            if (!mask[0]) return;
            if (mask[1]) data.WriteUInt8(Type);
            if (mask[3]) data.WriteUInt32(VirtualRealmAddress);
            if (mask[2])
            {
                data.WriteBits(PlayerName.GetValue().GetByteCount(), 6);
                data.FlushBits();
                data.WriteString(PlayerName);
            }
        }

        public override void ClearChangesMask()
        {
            ClearChangesMask(Type);
            ClearChangesMask(PlayerName);
            ClearChangesMask(VirtualRealmAddress);
            _changesMask.ResetAll();
        }
    }
}
