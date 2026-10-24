#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
namespace Game.Services.Classic;
sealed class BnetNote
{
    public uint AccountId;
    public uint Service;
    public uint Method;
    public byte[] Body = [];
}

sealed class BnetCall
{
    public uint Status;
    public byte[] Body = [];
    public string Command = "";
    public List<BnetNote> Notes { get; } = [];
    public HashSet<uint>? FriendIds;
}

sealed class ProtoWriter
{
    readonly List<byte> _data = new();

    public void Varint(int field, ulong value)
    {
        Key(field, 0);
        WriteVarint(value);
    }

    public void Bool(int field, bool value) => Varint(field, value ? 1UL : 0UL);

    public void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

    public void Bytes(int field, byte[] value)
    {
        Key(field, 2);
        WriteVarint((ulong)value.Length);
        _data.AddRange(value);
    }

    public void Fixed32(int field, uint value)
    {
        Key(field, 5);
        _data.Add((byte)value);
        _data.Add((byte)(value >> 8));
        _data.Add((byte)(value >> 16));
        _data.Add((byte)(value >> 24));
    }

    public void Message(int field, ProtoWriter inner) => Bytes(field, inner.ToArray());

    public byte[] ToArray() => _data.ToArray();

    void Key(int field, int wire) => WriteVarint((ulong)((field << 3) | wire));

    void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _data.Add((byte)(value | 0x80));
            value >>= 7;
        }
        _data.Add((byte)value);
    }
}

readonly record struct ProtoField(int Field, int Wire, ulong Varint, byte[] Bytes)
{
    public static List<ProtoField> Walk(byte[] data)
    {
        var list = new List<ProtoField>();
        int pos = 0;
        while (pos < data.Length)
        {
            if (!TryVarint(data, ref pos, out ulong key))
                break;
            int field = (int)(key >> 3);
            int wire = (int)(key & 7);
            switch (wire)
            {
                case 0:
                    if (!TryVarint(data, ref pos, out ulong varint))
                        return list;
                    list.Add(new ProtoField(field, wire, varint, []));
                    break;
                case 1:
                    if (pos + 8 > data.Length)
                        return list;
                    pos += 8;
                    list.Add(new ProtoField(field, wire, 0, []));
                    break;
                case 2:
                    if (!TryVarint(data, ref pos, out ulong length) || pos + (int)length > data.Length)
                        return list;
                    byte[] bytes = data[pos..(pos + (int)length)];
                    pos += (int)length;
                    list.Add(new ProtoField(field, wire, 0, bytes));
                    break;
                case 5:
                    if (pos + 4 > data.Length)
                        return list;
                    pos += 4;
                    list.Add(new ProtoField(field, wire, 0, []));
                    break;
                default:
                    return list;
            }
        }
        return list;
    }

    static bool TryVarint(byte[] data, ref int pos, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < data.Length && shift < 64)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
        }
        return false;
    }
}

