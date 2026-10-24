#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Constants;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Game.Services.Classic;

/// <summary>Trinity ServiceBuffer bit order: bits are packed from the high end of each byte.</summary>
sealed class ServiceBuffer
{
    byte[] _data;
    int _dataOffset;
    int _length;
    int _rpos;
    int _bitpos = 8;
    byte _curbit;
    bool _readOnlyBacking;

    public ServiceBuffer() => _data = new byte[256];

    ServiceBuffer(byte[] data, int offset, int length)
    {
        _data = data;
        _dataOffset = offset;
        _length = length;
        _readOnlyBacking = true;
    }

    public int Length => _length;
    public int Remaining => _length - _rpos;
    public byte[] ToArray() => _data.AsSpan(_dataOffset, _length).ToArray();
    public byte[] CopyRange(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > _length - count)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _data.AsSpan(_dataOffset + offset, count).ToArray();
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return;
        FlushBits();
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_data.AsSpan(_dataOffset + _length));
        _length += bytes.Length;
    }

    public void WriteUInt8(byte value)
    {
        FlushBits();
        EnsureCapacity(1);
        _data[_dataOffset + _length++] = value;
    }

    public void WriteInt8(sbyte value) => WriteUInt8((byte)value);

    public void WriteUInt16(ushort value)
    {
        FlushBits();
        EnsureCapacity(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_dataOffset + _length), value);
        _length += sizeof(ushort);
    }

    public void WriteInt16(short value) => WriteUInt16((ushort)value);

    public void WriteUInt32(uint value)
    {
        FlushBits();
        EnsureCapacity(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(_dataOffset + _length), value);
        _length += sizeof(uint);
    }

    public void WriteInt32(int value) => WriteUInt32((uint)value);

    public void WriteUInt64(ulong value)
    {
        FlushBits();
        EnsureCapacity(sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(_dataOffset + _length), value);
        _length += sizeof(ulong);
    }

    public void WriteInt64(long value) => WriteUInt64((ulong)value);

    public void WriteFloat(float value) => WriteUInt32(BitConverter.SingleToUInt32Bits(value));

    public void WriteBit(bool bit)
    {
        _bitpos--;
        if (bit)
            _curbit |= (byte)(1 << _bitpos);
        if (_bitpos == 0)
        {
            _bitpos = 8;
            EnsureCapacity(1);
            _data[_dataOffset + _length++] = _curbit;
            _curbit = 0;
        }
    }

    public void WriteBits(ulong value, int bits)
    {
        if (bits < 64)
            value &= (1UL << bits) - 1;
        if (bits > _bitpos)
        {
            int bitsInBuffer = _bitpos;
            _curbit |= (byte)(value >> (bits - bitsInBuffer));
            bits -= bitsInBuffer;
            EnsureCapacity(1 + bits / 8);
            _data[_dataOffset + _length++] = _curbit;
            _curbit = 0;
            while (bits >= 8)
            {
                bits -= 8;
                _data[_dataOffset + _length++] = (byte)(value >> bits);
            }
            _bitpos = 8 - bits;
            if (bits > 0)
                _curbit = (byte)((value & ((1UL << bits) - 1)) << _bitpos);
        }
        else
        {
            _bitpos -= bits;
            _curbit |= (byte)(value << _bitpos);
            if (_bitpos == 0)
            {
                _bitpos = 8;
                EnsureCapacity(1);
                _data[_dataOffset + _length++] = _curbit;
                _curbit = 0;
            }
        }
    }

    public bool HasUnfinishedBitPack => _bitpos != 8;

    public void FlushBits()
    {
        if (_bitpos == 8)
            return;
        _bitpos = 8;
        EnsureCapacity(1);
        _data[_dataOffset + _length++] = _curbit;
        _curbit = 0;
    }

    public void WriteSizedString(string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        // Trinity ServiceBuffer::WriteString does nothing for an empty value,
        // including leaving an unfinished bit pack alone.
        if (count == 0) return;
        FlushBits();
        EnsureCapacity(count);
        Encoding.UTF8.GetBytes(value, _data.AsSpan(_dataOffset + _length, count));
        _length += count;
    }

    // PacketOperators.h SizedCString::Data: its length prefix is always
    // byteCount + 1, but an empty value has no data and no zero terminator.
    public void WriteSizedCString(string value)
    {
        if (value.Length == 0) return;
        WriteSizedString(value);
        WriteUInt8(0);
    }

    public static void WriteGuid(ServiceBuffer buf, ulong high, ulong low)
    {

        Span<byte> raw = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(raw, low);
        BinaryPrimitives.WriteUInt64LittleEndian(raw[8..], high);
        Span<byte> packed = stackalloc byte[18];
        int mask = 0;
        int output = 2;
        for (int i = 0; i < 16; i++)
        {
            byte b = raw[i];
            packed[output] = b;
            int has = b != 0 ? 1 : 0;
            mask |= has << i;
            output += has;
        }
        packed[0] = (byte)(mask & 0xFF);
        packed[1] = (byte)((mask >> 8) & 0xFF);
        buf.Write(packed[..output]);
    }

    public static void WriteEmptyGuid(ServiceBuffer buf) => WriteGuid(buf, 0, 0);

    public static (ulong High, ulong Low) ReadGuid(ServiceBuffer buf)
    {
        ushort mask = buf.ReadUInt16();
        ReadOnlySpan<byte> packed = buf.ReadSpan(BitOperations.PopCount((uint)mask));
        ulong low = 0;
        ulong high = 0;
        int packedIndex = 0;
        while (mask != 0)
        {
            int byteIndex = BitOperations.TrailingZeroCount((uint)mask);
            ulong value = packed[packedIndex++];
            if (byteIndex < 8)
                low |= value << (byteIndex * 8);
            else
                high |= value << ((byteIndex - 8) * 8);
            mask &= (ushort)(mask - 1);
        }
        return (high, low);
    }

    public static ulong ReadGuidLow(ServiceBuffer buf)
    {
        ushort mask = buf.ReadUInt16();
        int packedLength = BitOperations.PopCount((uint)mask);
        ReadOnlySpan<byte> packed = buf.ReadSpan(packedLength);
        ushort lowMask = (ushort)(mask & 0x00FF);
        ulong low = 0;
        int packedIndex = 0;
        while (lowMask != 0)
        {
            int byteIndex = BitOperations.TrailingZeroCount((uint)lowMask);
            low |= (ulong)packed[packedIndex++] << (byteIndex * 8);
            lowMask &= (ushort)(lowMask - 1);
        }
        return low;
    }

    public byte[] ReadBytes(int count) => ReadSpan(count).ToArray();

    public string ReadString(int count) => Encoding.UTF8.GetString(ReadSpan(count));

    public string ReadString(int count, bool trimTrailingNull)
    {
        ReadOnlySpan<byte> value = ReadSpan(count);
        if (trimTrailingNull && !value.IsEmpty && value[^1] == 0)
            value = value[..^1];
        return Encoding.UTF8.GetString(value);
    }

    public void Skip(int count) => _ = ReadSpan(count);

    public void ResetBitPos()
    {
        _bitpos = 8;
        _curbit = 0;
    }

    public byte ReadUInt8() => ReadSpan(sizeof(byte))[0];

    public sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

    public short ReadInt16() => unchecked((short)ReadUInt16());

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(sizeof(ushort)));

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public long ReadInt64() => unchecked((long)ReadUInt64());

    public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32()));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(sizeof(uint)));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(sizeof(ulong)));

    public bool ReadBit()
    {
        if (_bitpos >= 8)
        {
            if (_rpos >= _length)
                throw new InvalidDataException("packet truncated");
            _curbit = _data[_dataOffset + _rpos++];
            _bitpos = 0;
        }
        return ((_curbit >> (8 - ++_bitpos)) & 1) != 0;
    }

    public uint ReadBits(int bits)
    {
        uint value = 0;
        if (bits > 8 - _bitpos)
        {
            int bitsInBuffer = 8 - _bitpos;
            if (bitsInBuffer > 0)
                value = (uint)(_curbit & ((1 << bitsInBuffer) - 1)) << (bits - bitsInBuffer);
            bits -= bitsInBuffer;
            while (bits >= 8)
            {
                bits -= 8;
                value |= (uint)ReadUInt8() << bits;
            }
            if (bits > 0)
            {
                if (_rpos >= _length)
                    throw new InvalidDataException("packet truncated");
                _curbit = _data[_dataOffset + _rpos++];
                value |= (uint)((_curbit >> (8 - bits)) & ((1 << bits) - 1));
                _bitpos = bits;
            }
        }
        else
        {
            value = (uint)((_curbit >> (8 - _bitpos - bits)) & ((1 << bits) - 1));
            _bitpos += bits;
        }
        return value;
    }

    public static ServiceBuffer From(byte[] data) => new(data, 0, data.Length);

    public static ServiceBuffer From(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) && segment.Array != null)
            return new ServiceBuffer(segment.Array, segment.Offset, segment.Count);
        return From(data.ToArray());
    }

    public ReadOnlySpan<byte> ReadSpan(int count)
    {
        ResetBitPos();
        if (count < 0 || count > _length - _rpos)
            throw new InvalidDataException("packet truncated");
        ReadOnlySpan<byte> span = _data.AsSpan(_dataOffset + _rpos, count);
        _rpos += count;
        return span;
    }

    void EnsureCapacity(int additional)
    {
        int required = checked(_length + additional);
        if (_readOnlyBacking)
        {
            int capacity = Math.Max(required, Math.Max(256, _length * 2));
            byte[] copy = new byte[capacity];
            _data.AsSpan(_dataOffset, _length).CopyTo(copy);
            _data = copy;
            _dataOffset = 0;
            _readOnlyBacking = false;
            return;
        }
        if (_dataOffset + required <= _data.Length)
            return;
        int newSize = Math.Max(required, Math.Max(256, _data.Length * 2));
        byte[] expanded = new byte[newSize];
        _data.AsSpan(_dataOffset, _length).CopyTo(expanded);
        _data = expanded;
        _dataOffset = 0;
    }
}


