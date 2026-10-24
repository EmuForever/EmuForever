// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;

namespace Framework.Constants
{
    public enum AuctionResult
    {
        Ok = 0,
        Inventory = 1,
        DatabaseError = 2,
        NotEnoughMoney = 3,
        ItemNotFound = 4,
        HigherBid = 5,
        BidIncrement = 7,
        BidOwn = 10,
        RestrictedAccountTrial = 13,
        HasRestriction = 17,
        AuctionHouseBusy = 18,
        AuctionHouseUnavailable = 19,
        CommodityPurchaseFailed = 21,
        ItemHasQuote = 23
    }

    public enum AuctionCommand
    {
        SellItem = 0,
        Cancel = 1,
        PlaceBid = 2
    }

    public enum AuctionMailType
    {
        Outbid = 0,
        Won = 1,
        Sold = 2,
        Expired = 3,
        Removed = 4,
        Cancelled = 5,
        Invoice = 6
    }

    // retail auction house (Forever 1.60.1 client), TC AuctionHouseMgr.h
    public enum AuctionHouseSortOrder : byte
    {
        Price = 0,
        Name = 1,
        Level = 2,
        Bid = 3,
        Buyout = 4,
        TimeRemaining = 5
    }

    [Flags]
    public enum AuctionHouseFilterMask : uint
    {
        None = 0x0000,
        UncollectedOnly = 0x0002,
        UsableOnly = 0x0004,
        CurrentExpansionOnly = 0x0008,
        UpgradesOnly = 0x0010,
        ExactMatch = 0x0020,
        PoorQuality = 0x0040,
        CommonQuality = 0x0080,
        UncommonQuality = 0x0100,
        RareQuality = 0x0200,
        EpicQuality = 0x0400,
        LegendaryQuality = 0x0800,
        ArtifactQuality = 0x1000,
        LegendaryCraftedItemOnly = 0x2000,
    }

    public enum AuctionHouseSortDirection : byte
    {
        Ascending = 0,
        Descending = 1,
    }

    public enum AuctionHouseBrowseMode
    {
        Search = 0,
        SpecificKeys = 1
    }

    public enum AuctionHouseListType
    {
        Commodities = 1,
        Items = 2
    }

    public enum AuctionPostingServerFlag
    {
        None = 0x0,
        GmLogBuyer = 0x1  // write transaction to gm log file for buyer (optimization flag - avoids querying database for offline player permissions)
    }

    public enum AuctionHouseId
    {
        Alliance = 2,
        Horde = 6,
        Neutral = 7,
    }
}
