// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    class CommerceTokenGetLog : ClientPacket
    {
        public CommerceTokenGetLog(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            UnkInt = _worldPacket.ReadUInt32();
        }

        public uint UnkInt;
    }

    class CommerceTokenGetLogResponse : ServerPacket
    {
        public CommerceTokenGetLogResponse() : base(ServerOpcodes.CommerceTokenGetLogResponse, ConnectionType.Instance) { }

        public override void Write()
        {
            // Forever layout: TC CommerceTokenGetLogResponse::Write
            _worldPacket.WriteUInt32(UnkInt);
            _worldPacket.WriteUInt32((uint)Result);
            _worldPacket.WriteInt32(AuctionableTokenAuctionableList.Count);

            foreach (AuctionableTokenInfo auctionableTokenAuctionable in AuctionableTokenAuctionableList)
            {
                _worldPacket.WriteUInt64(auctionableTokenAuctionable.UnkInt1);      // Id
                _worldPacket.WriteInt64(auctionableTokenAuctionable.UnkInt2);       // LastUpdate (Timestamp<int64>)
                _worldPacket.WriteInt32((int)auctionableTokenAuctionable.Owner);    // Status
                _worldPacket.WriteUInt64(auctionableTokenAuctionable.BuyoutPrice);  // Price
                _worldPacket.WriteUInt32(auctionableTokenAuctionable.DurationLeft);
            }
        }

        public uint UnkInt; // send CMSG_UPDATE_WOW_TOKEN_AUCTIONABLE_LIST
        public TokenResult Result;
        List<AuctionableTokenInfo> AuctionableTokenAuctionableList = new();

        struct AuctionableTokenInfo
        {
            public ulong UnkInt1;
            public long UnkInt2;
            public uint Owner;
            public ulong BuyoutPrice;
            public uint DurationLeft;
        }
    }

    class CommerceTokenGetMarketPrice : ClientPacket
    {
        public CommerceTokenGetMarketPrice(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            UnkInt = _worldPacket.ReadUInt32();
        }

        public uint UnkInt;
    }

    class CommerceTokenGetMarketPriceResponse : ServerPacket
    {
        public CommerceTokenGetMarketPriceResponse() : base(ServerOpcodes.CommerceTokenGetMarketPriceResponse) { }

        public override void Write()
        {
            // Forever layout: TC CommerceTokenGetMarketPriceResponse::Write
            _worldPacket.WriteUInt32(UnkInt);
            _worldPacket.WriteInt32((int)Result);
            _worldPacket.WriteUInt64(CurrentMarketPrice);
            _worldPacket.WriteUInt32(AuctionDuration);
        }

        public ulong CurrentMarketPrice;
        public uint UnkInt; // send CMSG_REQUEST_WOW_TOKEN_MARKET_PRICE
        public TokenResult Result;
        public uint AuctionDuration; // preset auction duration enum
    }
}
