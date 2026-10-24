// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;
using System.IO;

namespace Game.Entities
{
    /// <summary>
    /// Adapts the legacy per-type field writers to the 1.60.1 entity-fragment
    /// envelope. CGObject is the sole indirect/updateable fragment used by the
    /// current entity classes; the type tags identify the concrete field layout.
    /// </summary>
    internal static class EntityFragmentSerializer
    {
        const byte CgObject = 3; // Retail CGObject fragment id 2 is shifted by one in Classic.
        const byte End = 255;

        public static byte[] WrapCreateFields(byte[] legacy, TypeId objectType)
        {
            if (legacy == null || legacy.Length < sizeof(uint) + 1)
                throw new InvalidDataException("Create values block is shorter than its size and field-flag header.");

            byte[] tags = GetTypeTags(objectType);
            byte fieldFlags = legacy[sizeof(uint)];
            int valueOffset = sizeof(uint) + 1;
            int valueLength = legacy.Length - valueOffset;
            // Size starts immediately after the uint32. The body contains the
            // field flags, CGObject ID, concrete type tags, End marker, the
            // indirect-fragment activation byte, and the CGObject payload.
            int bodyLength = 1 + 1 + tags.Length + 1 + 1 + valueLength;

            byte[] result = new byte[sizeof(uint) + bodyLength];
            WriteUInt32(result, 0, checked((uint)bodyLength));
            int offset = sizeof(uint);
            result[offset++] = fieldFlags;
            result[offset++] = CgObject;
            Buffer.BlockCopy(tags, 0, result, offset, tags.Length);
            offset += tags.Length;
            result[offset++] = End;
            result[offset++] = 1; // indirect CGObject fragment is active on create
            Buffer.BlockCopy(legacy, valueOffset, result, offset, valueLength);
            if (offset + valueLength != result.Length)
                throw new InvalidDataException("Create fragment framing produced an inconsistent byte count.");
            return result;
        }

        public static byte[] WrapValuesUpdate(byte[] legacy, UpdateFieldFlag fieldFlags)
        {
            if (legacy == null || legacy.Length < 1 + 2 + sizeof(uint))
                throw new InvalidDataException("Values update block is shorter than its opcode, guid, and size header.");

            // Classic packed ObjectGuid starts with low/high byte-presence masks.
            int guidEnd = 3 + CountBits(legacy[1]) + CountBits(legacy[2]);
            if (guidEnd + sizeof(uint) > legacy.Length)
                throw new InvalidDataException("Values update block contains a truncated packed ObjectGuid.");

            uint oldSize = ReadUInt32(legacy, guidEnd);
            int valuesOffset = guidEnd + sizeof(uint);
            if (oldSize != legacy.Length - valuesOffset)
                throw new InvalidDataException($"Values update size says {oldSize} bytes, but {legacy.Length - valuesOffset} remain.");

            int valuesLength = legacy.Length - valuesOffset;
            // The fragment is indirect: bit 0 announces that CGObject exists,
            // and bit 1 marks its contents as changed. The legacy payload starts
            // with a *type* mask, whose first four bytes may be zero even when a
            // later field group changed. The reference serializer sets both
            // bits for every emitted CGObject values block.
            const byte contentsChangedMask = 0x03;
            int envelopeLength = 3 + valuesLength;
            byte[] result = new byte[guidEnd + sizeof(uint) + envelopeLength];
            Buffer.BlockCopy(legacy, 0, result, 0, guidEnd);
            WriteUInt32(result, guidEnd, checked((uint)envelopeLength));
            int offset = guidEnd + sizeof(uint);
            result[offset++] = fieldFlags.HasFlag(UpdateFieldFlag.Owner) ? (byte)1 : (byte)0;
            result[offset++] = 0; // fragment IDs are unchanged after create
            result[offset++] = contentsChangedMask;
            if (valuesLength != 0)
                Buffer.BlockCopy(legacy, valuesOffset, result, offset, valuesLength);
            return result;
        }

        static byte[] GetTypeTags(TypeId type) => type switch
        {
            TypeId.Item => [200],
            TypeId.Container => [200, 201],
            TypeId.AzeriteEmpoweredItem => [200, 202],
            TypeId.AzeriteItem => [200, 203],
            TypeId.Unit => [204],
            TypeId.Player or TypeId.ActivePlayer => [204, 205],
            TypeId.GameObject => [206],
            TypeId.DynamicObject => [207],
            TypeId.Corpse => [208],
            TypeId.AreaTrigger => [209],
            TypeId.SceneObject => [210],
            TypeId.Conversation => [211],
            _ => []
        };

        static int CountBits(byte value)
        {
            int count = 0;
            for (; value != 0; value &= (byte)(value - 1))
                ++count;
            return count;
        }

        static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);

        static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
