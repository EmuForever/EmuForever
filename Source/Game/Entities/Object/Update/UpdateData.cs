// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.IO;
using Game.Networking;
using Game.Networking.Packets;
using System.Collections.Generic;
using System;

namespace Game.Entities
{
    public class UpdateData
    {
        int MapId;
        uint BlockCount;
        HashSet<ObjectGuid> destroyGUIDs = new();
        HashSet<ObjectGuid> outOfRangeGUIDs = new();
        ByteBuffer data = new();

        public UpdateData(int mapId)
        {
            MapId = mapId;
        }

        public void AddDestroyObject(ObjectGuid guid)
        {
            outOfRangeGUIDs.Remove(guid);
            CheckRemovalCapacity(guid);
            destroyGUIDs.Add(guid);
        }

        public void AddOutOfRangeGUID(List<ObjectGuid> guids)
        {
            foreach (var guid in guids) AddOutOfRangeGUID(guid);
        }

        public void AddOutOfRangeGUID(ObjectGuid guid)
        {
            // Destroy wins if both notifications arrive in a batch. Each GUID is
            // serialized once, and the 16-bit protocol count cannot wrap.
            if (destroyGUIDs.Contains(guid)) return;
            CheckRemovalCapacity(guid);
            outOfRangeGUIDs.Add(guid);
        }

        void CheckRemovalCapacity(ObjectGuid guid)
        {
            if (destroyGUIDs.Count + outOfRangeGUIDs.Count >= ushort.MaxValue &&
                !destroyGUIDs.Contains(guid) && !outOfRangeGUIDs.Contains(guid))
                throw new InvalidOperationException("Object removal batch exceeds protocol capacity");
        }

        public void AddUpdateBlock(ByteBuffer block)
        {
            data.WriteBytes(block.GetData());
            ++BlockCount;
        }

        public void AddUpdateBlock(ByteBuffer block, UpdateFieldFlag fieldFlags)
        {
            data.WriteBytes(EntityFragmentSerializer.WrapValuesUpdate(block.GetData(), fieldFlags));
            ++BlockCount;
        }

        public bool BuildPacket(out UpdateObject packet)
        {
            packet = new UpdateObject();

            packet.NumObjUpdates = BlockCount;
            packet.MapID = (ushort)MapId;

            WorldPacket buffer = new();
            buffer.WriteBit(true); // 1.60.1 SMSG_UPDATE_OBJECT header bit
            if (buffer.WriteBit(!outOfRangeGUIDs.Empty() || !destroyGUIDs.Empty()))
            {
                buffer.WriteUInt16((ushort)destroyGUIDs.Count);
                buffer.WriteInt32(destroyGUIDs.Count + outOfRangeGUIDs.Count);

                foreach (var destroyGuid in destroyGUIDs)
                    buffer.WritePackedGuid(destroyGuid);

                foreach (var outOfRangeGuid in outOfRangeGUIDs)
                    buffer.WritePackedGuid(outOfRangeGuid);
            }

            var bytes = data.GetData();
            buffer.WriteInt32(bytes.Length);
            buffer.WriteBytes(bytes);

            packet.Data = buffer.GetData();
            return true;
        }

        public void Clear()
        {
            data.Clear();
            destroyGUIDs.Clear();
            outOfRangeGUIDs.Clear();
            BlockCount = 0;
            MapId = 0;
        }

        public bool HasData() { return BlockCount > 0 || !outOfRangeGUIDs.Empty() || !destroyGUIDs.Empty(); }

        // Compatibility snapshot: callers cannot append around the deduplication policy.
        public List<ObjectGuid> GetOutOfRangeGUIDs() { return new(outOfRangeGUIDs); }

        public void SetMapId(ushort mapId) { MapId = mapId; }
    }
}
