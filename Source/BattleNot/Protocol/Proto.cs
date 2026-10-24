using System.Buffers.Binary;
using System.Text;

namespace BattleNot;

sealed class ProtoWriter
{
    readonly MemoryStream _stream = new();

    public void Varint(int field, ulong value) => WriteKey(field, 0, value);
    public void Bool(int field, bool value) => Varint(field, value ? 1ul : 0ul);
    public void Fixed32(int field, uint value)
    {
        WriteKey(field, 5);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void Fixed64(int field, ulong value)
    {
        WriteKey(field, 1);
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        _stream.Write(buf);
    }

    public void Bytes(int field, ReadOnlySpan<byte> data)
    {
        WriteKey(field, 2);
        WriteVarint((ulong)data.Length);
        _stream.Write(data);
    }

    public void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    public void Message(int field, ProtoWriter nested) => Bytes(field, nested.ToArray());
    public byte[] ToArray() => _stream.ToArray();

    void WriteKey(int field, int wire, ulong? varint = null)
    {
        WriteVarint((ulong)((field << 3) | wire));
        if (varint.HasValue)
            WriteVarint(varint.Value);
    }

    void WriteVarint(ulong value)
    {
        while (value > 0x7F)
        {
            _stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _stream.WriteByte((byte)value);
    }
}

sealed class ProtoReader
{
    readonly ReadOnlyMemory<byte> _data;
    int _pos;

    public ProtoReader(byte[] data)
        : this(data.AsMemory())
    {
    }

    public ProtoReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public bool Next(out int field, out int wire)
    {
        field = 0;
        wire = 0;
        if (_pos >= _data.Length)
            return false;
        ulong key = ReadVarint();
        field = (int)(key >> 3);
        wire = (int)(key & 7);
        return true;
    }

    public ulong ReadVarint()
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= _data.Length)
                throw new InvalidDataException("truncated varint");
            byte b = _data.Span[_pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }
    }

    public uint ReadFixed32()
    {
        if (_pos + 4 > _data.Length)
            throw new InvalidDataException("truncated fixed32");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Span.Slice(_pos, 4));
        _pos += 4;
        return value;
    }

    public ulong ReadFixed64()
    {
        if (_pos + 8 > _data.Length)
            throw new InvalidDataException("truncated fixed64");
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Span.Slice(_pos, 8));
        _pos += 8;
        return value;
    }

    public byte[] ReadBytes()
    {
        int length = (int)ReadVarint();
        if (_pos + length > _data.Length)
            throw new InvalidDataException("truncated bytes");
        byte[] data = _data.Span.Slice(_pos, length).ToArray();
        _pos += length;
        return data;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

    public void Skip(int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(); break;
            case 1: _pos += 8; break;
            case 2: ReadBytes(); break;
            case 5: _pos += 4; break;
            default: throw new InvalidDataException($"unsupported wire type {wire}");
        }
    }
}

static class Rpc
{
    public const uint ResponseServiceId = 0xFE;
    public const uint ErrorOk = 0;
    public const uint ErrorTimedOut = 0x2;
    public const uint ErrorDenied = 0x3;
    public const uint ErrorGameAccountBanned = 0x34;
    public const uint ErrorGameAccountSuspended = 0x35;
    public const uint ErrorBadProgram = 0x4D;
    public const uint ErrorBadLocale = 0x4E;
    public const uint ErrorBadPlatform = 0x4F;
    public const uint ErrorRpcInvalidService = 0xBC2;
    public const uint ErrorRpcInvalidMethod = 0xBC3;
    public const uint ErrorRpcMalformedRequest = 0xBC5;
    public const uint ErrorRpcNotImplemented = 0xBC7;
    public const uint ErrorRiskAccountLocked = 0xA413;
    public const uint ErrorUtilUnknownRealm = 0x80000069;
    public const uint ErrorUtilInvalidIdentity = 0x8000006E;
    public const uint ErrorUtilSerialize = 0x80000073;
    public const uint ErrorUserNotPermitted = 0x800000E1;
    public const uint ErrorDeniedRealmListTicket = 0x80000132;

    public const uint Wow = 0x00576F57;

    public const uint AuthV1 = 0x0DECFC01;
    public const uint AuthListenerV1 = 0x71240E35;
    public const uint AuthV2 = 0xC02F8216;
    public const uint AuthListenerV2 = 0x9DA8116B;
    public const uint ChallengeListener = 0xBBDA171F;
    public const uint Connection = 0x65446991;
    public const uint GameUtilitiesV1 = 0x3FC1274D;
    public const uint GameUtilitiesV2 = 0x5DBB51C2;
    public const uint AccountV1 = 0x62DA0891;
    public const uint AccountV2 = 0x22DC2464;

    public static readonly uint[] StubServices =
    [
        0x8E8F5FB0, 0xB5DD8A75, 0x94B94786, 0xE273DE0E,
        0xA3DDB1BD, 0x6F259A13,
        0x0CBE3C43, 0xE1CB2EA8, 0xF8E1EB98,
        0xFA0796FF, 0x138D200C,
        0x7CAF61C9, 0x3A4218FB, 0xC341E463,
        0xECBE75BA, 0xFEE1AA14
    ];
}
