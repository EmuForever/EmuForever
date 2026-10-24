// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    public class WhoIsRequest : ClientPacket
    {
        public WhoIsRequest(WorldPacket packet) : base(packet) { }

        // Forever layout: TC WhoIsRequest::Read
        public override void Read()
        {
            CharName = _worldPacket.ReadString(_worldPacket.ReadBits<int>(6));
        }

        public string CharName;
    }

    public class WhoIsResponse : ServerPacket
    {
        public WhoIsResponse() : base(ServerOpcodes.WhoIs) { }

        // Forever layout: TC WhoIsResponse::Write
        public override void Write()
        {
            _worldPacket.WriteBits(AccountName.GetByteCount(), 11);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(AccountName);
        }

        public string AccountName;
    }

    public class WhoRequestPkt : ClientPacket
    {
        public WhoRequestPkt(WorldPacket packet) : base(packet) { }

        // Forever layout: TC WhoRequestPkt::Read
        public override void Read()
        {
            Request.Read(_worldPacket, Areas);
            RequestID = _worldPacket.ReadUInt32();
            Origin = _worldPacket.ReadUInt8();
            IsFromAddOn = _worldPacket.HasBit();
        }

        public WhoRequest Request = new();
        public uint RequestID;
        public byte Origin;   // 1 = Social, 2 = Chat, 3 = Item
        public bool IsFromAddOn;
        public Array<int> Areas = new(63); // Classic 1.60: inside the request, 6 bit count
    }

    public class WhoResponsePkt : ServerPacket
    {
        public WhoResponsePkt() : base(ServerOpcodes.Who) { }

        // Forever layout: TC WhoResponsePkt::Write
        public override void Write()
        {
            _worldPacket.WriteUInt32(RequestID);
            _worldPacket.WriteInt32(Response.Count); // Classic 1.60: uint32 count

            foreach(var entry in Response)
                entry.Write(_worldPacket);
        }

        public uint RequestID;
        public List<WhoEntry> Response = new();
    }

    //Structs
    public struct WhoRequestServerInfo
    {
        public void Read(WorldPacket data)
        {
            // Classic 1.60: uint8, int32, 6 x uint32, bool
            FactionGroup = data.ReadUInt8();
            Locale = data.ReadInt32();
            RequesterVirtualRealmAddress = data.ReadUInt32();
            data.Skip(5 * 4 + 1);
        }

        public byte FactionGroup;
        public int Locale;
        public uint RequesterVirtualRealmAddress;
    }

    public class WhoRequest
    {
        // Forever layout: TC ReadWhoRequest (Classic 1.60)
        public void Read(WorldPacket data, Array<int> areas)
        {
            MinLevel = data.ReadInt32();
            MaxLevel = data.ReadInt32();
            RaceFilter = (RaceMask)data.ReadInt64();
            ClassFilter = (ClassMask)data.ReadInt32();

            int nameLength = data.ReadBits<int>(6);
            int virtualRealmNameLength = data.ReadBits<int>(9);
            int surnameLength = data.ReadBits<int>(9);
            int guildNameLength = data.ReadBits<int>(7);
            int guildVirtualRealmNameLength = data.ReadBits<int>(9);
            int wordsCount = data.ReadBits<int>(3);

            ShowEnemies = data.HasBit();
            ShowArenaPlayers = data.HasBit();
            ExactName = data.HasBit();
            Unknown = data.HasBit();
            uint areasCount = data.ReadBits<uint>(6);
            if (data.HasBit())
                ServerInfo = new();

            data.ResetBitPos();

            Name = data.ReadString(nameLength);
            VirtualRealmName = data.ReadString(virtualRealmNameLength);
            Surname = data.ReadString(surnameLength);
            Guild = data.ReadString(guildNameLength);
            GuildVirtualRealmName = data.ReadString(guildVirtualRealmNameLength);

            for (int i = 0; i < wordsCount; ++i)
            {
                int len = data.ReadBits<int>(7);
                data.ResetBitPos();
                Words.Add(data.ReadString(len));
            }

            for (int i = 0; i < areasCount; ++i)
                areas.Add(data.ReadInt32());

            if (ServerInfo.HasValue)
            {
                var info = new WhoRequestServerInfo();
                info.Read(data);
                ServerInfo = info;
            }
        }

        public int MinLevel;
        public int MaxLevel;
        public string Name;
        public string VirtualRealmName;
        public string Surname;                            // Classic 1.60
        public string Guild;
        public string GuildVirtualRealmName;
        public RaceMask RaceFilter;
        public ClassMask ClassFilter = ClassMask.AllPermanent;
        public List<string> Words = new();
        public bool ShowEnemies;
        public bool ShowArenaPlayers;
        public bool ExactName;
        public bool Unknown;                              // Classic 1.60: fourth flag bit
        public WhoRequestServerInfo? ServerInfo;
    }

    public class WhoEntry
    {
        // Forever layout: TC operator<<(WhoEntry)
        public void Write(WorldPacket data)
        {
            PlayerData.Write(data);

            data.WritePackedGuid(GuildGUID);
            data.WriteUInt32(GuildVirtualRealmAddress);
            data.WriteInt32(AreaID);
            // Classic 1.60: two more int32 after the area, a {4 x uint32, int8} block and one more bit
            data.WriteInt32(0);
            data.WriteInt32(0);
            for (int i = 0; i < 4; ++i)
                data.WriteUInt32(0);
            data.WriteInt8(0);

            data.WriteBits(GuildName.GetByteCount(), 7);
            data.WriteBit(IsGM);
            data.WriteBit(false);
            data.FlushBits();

            data.WriteString(GuildName);
        }

        public PlayerGuidLookupData PlayerData = new();
        public ObjectGuid GuildGUID;
        public uint GuildVirtualRealmAddress;
        public string GuildName = string.Empty;
        public int AreaID;
        public bool IsGM;
    }
}
