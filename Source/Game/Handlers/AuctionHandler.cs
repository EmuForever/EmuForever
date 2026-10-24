// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Entities;
using Game.Networking;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    // Forever 1.60.1 uses the retail auction house protocol (TC AuctionHouseHandler.cpp)
    public partial class WorldSession
    {
        Creature GetAuctioneerForSession(ObjectGuid auctioneer, string handler)
        {
            Creature creature = GetPlayer().GetNPCIfCanInteractWith(auctioneer, NPCFlags1.Auctioneer, NPCFlags2.None);
            if (creature == null)
            {
                Log.outDebug(LogFilter.Network, $"WORLD: {handler} - {auctioneer} not found or you can't interact with him.");
                return null;
            }

            // remove fake death
            if (GetPlayer().HasUnitState(UnitState.Died))
                GetPlayer().RemoveAurasByType(AuraType.FeignDeath);

            return creature;
        }

        [WorldPacketHandler(ClientOpcodes.AuctionBrowseQuery)]
        void HandleAuctionBrowseQuery(AuctionBrowseQuery browseQuery)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, browseQuery.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(browseQuery.Auctioneer, nameof(HandleAuctionBrowseQuery));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            Log.outDebug(LogFilter.Auctionhouse,
                $"Auctionhouse search ({browseQuery.Auctioneer}), searchedname: {browseQuery.Name}, " +
                $"levelmin: {browseQuery.MinLevel}, levelmax: {browseQuery.MaxLevel}, filters: {browseQuery.Filters}");

            AuctionSearchClassFilters classFilters = null;
            if (!browseQuery.ItemClassFilters.Empty())
            {
                classFilters = new();

                foreach (var classFilter in browseQuery.ItemClassFilters)
                {
                    if (classFilter.ItemClass < 0 || classFilter.ItemClass >= ItemClass.Max)
                        continue;

                    if (!classFilter.SubClassFilters.Empty())
                    {
                        foreach (var subClassFilter in classFilter.SubClassFilters)
                        {
                            int subClass = subClassFilter.ItemSubclass.data;
                            if (subClass < 0 || subClass >= 32)
                                continue;

                            classFilters.SubclassMask[(int)classFilter.ItemClass] |= 1u << subClass;
                            if (subClass < ItemConst.MaxItemSubclassTotal)
                                classFilters.InvTypes[(int)classFilter.ItemClass, subClass] = subClassFilter.InvTypeMask;
                        }
                    }
                    else
                        classFilters.SubclassMask[(int)classFilter.ItemClass] = AuctionSearchClassFilters.SkipSubclass;
                }
            }

            AuctionListBucketsResult listBucketsResult = new();
            auctionHouse.BuildListBuckets(listBucketsResult, _player, browseQuery.Name, browseQuery.MinLevel, browseQuery.MaxLevel,
                browseQuery.Filters, classFilters, browseQuery.Offset, browseQuery.Sorts);

            listBucketsResult.BrowseMode = AuctionHouseBrowseMode.Search;
            listBucketsResult.DesiredDelay = throttle.DelayUntilNext;
            SendPacket(listBucketsResult);
        }

        [WorldPacketHandler(ClientOpcodes.AuctionCancelCommoditiesPurchase)]
        void HandleAuctionCancelCommoditiesPurchase(AuctionCancelCommoditiesPurchase cancelCommoditiesPurchase)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, cancelCommoditiesPurchase.TaintedBy.HasValue, AuctionCommand.PlaceBid);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(cancelCommoditiesPurchase.Auctioneer, nameof(HandleAuctionCancelCommoditiesPurchase));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());
            auctionHouse.CancelCommodityQuote(_player.GetGUID());
        }

        [WorldPacketHandler(ClientOpcodes.AuctionConfirmCommoditiesPurchase)]
        void HandleAuctionConfirmCommoditiesPurchase(AuctionConfirmCommoditiesPurchase confirmCommoditiesPurchase)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, confirmCommoditiesPurchase.TaintedBy.HasValue, AuctionCommand.PlaceBid);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(confirmCommoditiesPurchase.Auctioneer, nameof(HandleAuctionConfirmCommoditiesPurchase));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            SQLTransaction trans = new();
            if (auctionHouse.BuyCommodity(trans, _player, confirmCommoditiesPurchase.ItemID, confirmCommoditiesPurchase.Quantity, throttle.DelayUntilNext))
            {
                var buyerGuid = _player.GetGUID();
                AddTransactionCallback(DB.Characters.AsyncCommitTransaction(trans)).AfterComplete(success =>
                {
                    if (GetPlayer() != null && GetPlayer().GetGUID() == buyerGuid)
                    {
                        if (success)
                        {
                            GetPlayer().UpdateCriteria(CriteriaType.AuctionsWon, 1);
                            SendAuctionCommandResult(0, AuctionCommand.PlaceBid, AuctionResult.Ok, throttle.DelayUntilNext);
                        }
                        else
                            SendAuctionCommandResult(0, AuctionCommand.PlaceBid, AuctionResult.CommodityPurchaseFailed, throttle.DelayUntilNext);
                    }
                });
            }
        }

        [WorldPacketHandler(ClientOpcodes.AuctionGetCommodityQuote)]
        void HandleAuctionGetCommodityQuote(AuctionGetCommodityQuote getCommodityQuote)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, getCommodityQuote.TaintedBy.HasValue, AuctionCommand.PlaceBid);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(getCommodityQuote.Auctioneer, nameof(HandleAuctionGetCommodityQuote));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionGetCommodityQuoteResult commodityQuoteResult = new();

            CommodityQuote quote = auctionHouse.CreateCommodityQuote(_player, getCommodityQuote.ItemID, getCommodityQuote.Quantity);
            if (quote != null)
            {
                commodityQuoteResult.TotalPrice = quote.TotalPrice;
                commodityQuoteResult.Quantity = quote.Quantity;
                commodityQuoteResult.QuoteDuration = quote.ValidTo - LoopTime.ServerTime;
            }

            commodityQuoteResult.ItemID = getCommodityQuote.ItemID;
            commodityQuoteResult.DesiredDelay = throttle.DelayUntilNext;

            SendPacket(commodityQuoteResult);
        }

        [WorldPacketHandler(ClientOpcodes.AuctionHelloRequest)]
        void HandleAuctionHello(AuctionHelloRequest hello)
        {
            Creature unit = GetAuctioneerForSession(hello.Guid, nameof(HandleAuctionHello));
            if (unit == null)
                return;

            SendAuctionHello(hello.Guid, unit);
        }

        [WorldPacketHandler(ClientOpcodes.AuctionListBiddedItems)]
        void HandleAuctionListBiddedItems(AuctionListBiddedItems listBiddedItems)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, listBiddedItems.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(listBiddedItems.Auctioneer, nameof(HandleAuctionListBiddedItems));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionListBiddedItemsResult result = new();
            auctionHouse.BuildListBiddedItems(result, _player, listBiddedItems.Sorts);
            result.DesiredDelay = throttle.DelayUntilNext;
            SendPacket(result);
        }

        [WorldPacketHandler(ClientOpcodes.AuctionListBucketsByBucketKeys)]
        void HandleAuctionListBucketsByBucketKeys(AuctionListBucketsByBucketKeys listBucketsByBucketKeys)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, listBucketsByBucketKeys.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(listBucketsByBucketKeys.Auctioneer, nameof(HandleAuctionListBucketsByBucketKeys));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionListBucketsResult listBucketsResult = new();
            auctionHouse.BuildListBuckets(listBucketsResult, _player, listBucketsByBucketKeys.BucketKeys, listBucketsByBucketKeys.Sorts);

            listBucketsResult.BrowseMode = AuctionHouseBrowseMode.SpecificKeys;
            listBucketsResult.DesiredDelay = throttle.DelayUntilNext;
            SendPacket(listBucketsResult);
        }

        static AuctionHouseListType GetAuctionListType(int itemId)
        {
            ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate(itemId);
            return itemTemplate != null && itemTemplate.GetMaxStackSize() > 1 ? AuctionHouseListType.Commodities : AuctionHouseListType.Items;
        }

        [WorldPacketHandler(ClientOpcodes.AuctionListItemsByBucketKey)]
        void HandleAuctionListItemsByBucketKey(AuctionListItemsByBucketKey listItemsByBucketKey)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, listItemsByBucketKey.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(listItemsByBucketKey.Auctioneer, nameof(HandleAuctionListItemsByBucketKey));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionListItemsResult listItemsResult = new();
            listItemsResult.DesiredDelay = throttle.DelayUntilNext;
            listItemsResult.BucketKey = listItemsByBucketKey.BucketKey;
            listItemsResult.ListType = GetAuctionListType(listItemsByBucketKey.BucketKey.ItemID);

            auctionHouse.BuildListAuctionItems(listItemsResult, _player, new AuctionsBucketKey(listItemsByBucketKey.BucketKey),
                listItemsByBucketKey.Offset, listItemsByBucketKey.Sorts);

            SendPacket(listItemsResult);
        }

        [WorldPacketHandler(ClientOpcodes.AuctionListItemsByItemId)]
        void HandleAuctionListItemsByItemID(AuctionListItemsByItemID listItemsByItemID)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, listItemsByItemID.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(listItemsByItemID.Auctioneer, nameof(HandleAuctionListItemsByItemID));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionListItemsResult listItemsResult = new();
            listItemsResult.DesiredDelay = throttle.DelayUntilNext;
            listItemsResult.BucketKey.ItemID = listItemsByItemID.ItemID;
            listItemsResult.ListType = GetAuctionListType(listItemsByItemID.ItemID);

            auctionHouse.BuildListAuctionItems(listItemsResult, _player, listItemsByItemID.ItemID, listItemsByItemID.Offset, listItemsByItemID.Sorts);

            SendPacket(listItemsResult);
        }

        //this void sends player info about his auctions
        [WorldPacketHandler(ClientOpcodes.AuctionListOwnedItems)]
        void HandleAuctionListOwnedItems(AuctionListOwnedItems listOwnedItems)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, listOwnedItems.TaintedBy.HasValue);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(listOwnedItems.Auctioneer, nameof(HandleAuctionListOwnedItems));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionListOwnedItemsResult result = new();
            auctionHouse.BuildListOwnedItems(result, _player, listOwnedItems.Sorts);
            result.DesiredDelay = throttle.DelayUntilNext;
            SendPacket(result);
        }

        // this function is called when client bids or buys out auction
        [WorldPacketHandler(ClientOpcodes.AuctionPlaceBid)]
        void HandleAuctionPlaceBid(AuctionPlaceBid placeBid)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, placeBid.TaintedBy.HasValue, AuctionCommand.PlaceBid);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(placeBid.Auctioneer, nameof(HandleAuctionPlaceBid));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionPosting auction = auctionHouse.GetAuction(placeBid.AuctionID);
            if (auction == null || auction.IsCommodity())
            {
                SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                return;
            }

            Player player = GetPlayer();

            // check auction owner - cannot buy own auctions
            if (auction.Owner == player.GetGUID() || auction.OwnerAccount == GetAccountGUID())
            {
                SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.BidOwn, throttle.DelayUntilNext);
                return;
            }

            bool canBid = auction.MinBid != 0;
            bool canBuyout = auction.BuyoutPrice != 0;

            // buyout attempt with wrong amount
            if (!canBid && placeBid.BidAmount != auction.BuyoutPrice)
            {
                SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.BidIncrement, throttle.DelayUntilNext);
                return;
            }

            if (canBid)
            {
                if (canBuyout && placeBid.BidAmount > auction.BuyoutPrice || placeBid.BidAmount == 0)
                {
                    SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                    return;
                }

                bool isFirstBid = auction.Bidder.IsEmpty();
                long currentBidAmount = isFirstBid ? auction.MinBid : auction.BidAmount;
                long nextBidAmount = isFirstBid ? auction.MinBid : AuctionPosting.CalculateNextBidAmount(auction.BidAmount);

                if (placeBid.BidAmount < currentBidAmount)
                {
                    SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.HigherBid, throttle.DelayUntilNext);
                    return;
                }

                if (placeBid.BidAmount < nextBidAmount && !(canBuyout && placeBid.BidAmount == auction.BuyoutPrice))
                {
                    SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.BidIncrement, throttle.DelayUntilNext);
                    return;
                }
            }

            SQLTransaction trans = new();
            long priceToPay = placeBid.BidAmount;
            if (!auction.Bidder.IsEmpty())
            {
                // return money to previous bidder
                if (auction.Bidder != player.GetGUID())
                    auctionHouse.SendAuctionOutbid(auction, player.GetGUID(), placeBid.BidAmount, trans);
                else
                    priceToPay = placeBid.BidAmount - auction.BidAmount;
            }

            // check money
            if (!player.HasEnoughMoney(priceToPay))
            {
                SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                return;
            }

            player.ModifyMoney(-priceToPay);
            auction.Bidder = player.GetGUID();
            auction.BidAmount = placeBid.BidAmount;
            if (HasPermission(RBACPermissions.LogGmTrade))
                auction.ServerFlags |= AuctionPostingServerFlag.GmLogBuyer;
            else
                auction.ServerFlags &= ~AuctionPostingServerFlag.GmLogBuyer;

            bool auctionSold = canBuyout && placeBid.BidAmount == auction.BuyoutPrice;

            if (auctionSold)
            {
                // buyout
                auctionHouse.SendAuctionInvoice(auction, null, trans);
                auctionHouse.SendAuctionSold(auction, null, trans);
                auctionHouse.SendAuctionWon(auction, player, trans);
                auctionHouse.RemoveAuction(trans, auction);
            }
            else
            {
                // place bid
                PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_AUCTION_BID);
                stmt.SetInt64(0, auction.Bidder.GetCounter());
                stmt.SetInt64(1, auction.BidAmount);
                stmt.SetUInt8(2, (byte)auction.ServerFlags);
                stmt.SetInt32(3, auction.Id);
                trans.Append(stmt);

                if (auction.BidderHistory.Add(player.GetGUID()))
                {
                    stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_AUCTION_BIDDER);
                    stmt.SetInt32(0, auction.Id);
                    stmt.SetInt64(1, player.GetGUID().GetCounter());
                    trans.Append(stmt);
                }

                auctionHouse.AddBidder(auction.Bidder, auction);

                // Not sure if we must send this now.
                Player owner = Global.ObjAccessor.FindConnectedPlayer(auction.Owner);
                if (owner != null)
                    owner.GetSession().SendAuctionOwnerBidNotification(auction);
            }

            player.SaveInventoryAndGoldToDB(trans);
            var bidderGuid = player.GetGUID();
            AddTransactionCallback(DB.Characters.AsyncCommitTransaction(trans)).AfterComplete(success =>
            {
                if (GetPlayer() != null && GetPlayer().GetGUID() == bidderGuid)
                {
                    if (success)
                    {
                        GetPlayer().UpdateCriteria(CriteriaType.HighestAuctionBid, placeBid.BidAmount);
                        SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.Ok, throttle.DelayUntilNext);
                    }
                    else
                        SendAuctionCommandResult(placeBid.AuctionID, AuctionCommand.PlaceBid, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                }
            });
        }

        [WorldPacketHandler(ClientOpcodes.AuctionRemoveItem)]
        void HandleAuctionRemoveItem(AuctionRemoveItem removeItem)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, removeItem.TaintedBy.HasValue, AuctionCommand.Cancel);
            if (throttle.Throttled)
                return;

            Creature creature = GetAuctioneerForSession(removeItem.Auctioneer, nameof(HandleAuctionRemoveItem));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionPosting auction = auctionHouse.GetAuction(removeItem.AuctionID);
            Player player = GetPlayer();

            SQLTransaction trans = new();
            if (auction != null && auction.Owner == player.GetGUID())
            {
                if (!auction.Bidder.IsEmpty())                   // If we have a bidder, we have to send him the money he paid
                {
                    long cancelCost = MathFunctions.CalculatePct(auction.BidAmount, auctionHouse.ConsignmentRate);
                    if (!player.HasEnoughMoney(cancelCost))          //player doesn't have enough money
                    {
                        SendAuctionCommandResult(0, AuctionCommand.Cancel, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                        return;
                    }
                    auctionHouse.SendAuctionCancelledToBidder(auction, trans);
                    player.ModifyMoney(-cancelCost);
                }

                auctionHouse.SendAuctionRemoved(auction, player, trans);
            }
            else
            {
                SendAuctionCommandResult(0, AuctionCommand.Cancel, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                //this code isn't possible ... maybe there should be assert
                Log.outError(LogFilter.Network, $"CHEATER: {player.GetGUID()} tried to cancel auction (id: {removeItem.AuctionID}) of another player or auction is null");
                return;
            }

            // client bug - instead of removing auction in the UI, it only substracts 1 from visible count
            int auctionIdForClient = auction.Id;

            // Now remove the auction
            player.SaveInventoryAndGoldToDB(trans);
            auctionHouse.RemoveAuction(trans, auction);

            var ownerGuid = player.GetGUID();
            AddTransactionCallback(DB.Characters.AsyncCommitTransaction(trans)).AfterComplete(success =>
            {
                if (GetPlayer() != null && GetPlayer().GetGUID() == ownerGuid)
                {
                    if (success)
                        SendAuctionCommandResult(auctionIdForClient, AuctionCommand.Cancel, AuctionResult.Ok, throttle.DelayUntilNext);        //inform player, that auction is removed
                    else
                        SendAuctionCommandResult(0, AuctionCommand.Cancel, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                }
            });
        }

        [WorldPacketHandler(ClientOpcodes.AuctionReplicateItems)]
        void HandleAuctionReplicateItems(AuctionReplicateItems replicateItems)
        {
            Creature creature = GetAuctioneerForSession(replicateItems.Auctioneer, nameof(HandleAuctionReplicateItems));
            if (creature == null)
                return;

            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            AuctionReplicateResponse response = new();

            auctionHouse.BuildReplicate(response, GetPlayer(), replicateItems.ChangeNumberGlobal, replicateItems.ChangeNumberCursor, replicateItems.ChangeNumberTombstone, replicateItems.Count);

            response.DesiredDelay = WorldConfig.Values[WorldCfg.AuctionSearchDelay].TimeSpan * 5;
            response.Result = 0;

            SendPacket(response);
        }

        // Classic 1.60: 2, 8 and 24 hours
        static bool IsValidAuctionRunTime(TimeSpan runTime)
        {
            return runTime == SharedConst.MinAuctionTime || runTime == SharedConst.MinAuctionTime * 4 || runTime == SharedConst.MinAuctionTime * 12;
        }

        bool CanAuctionItem(Item item)
        {
            return Global.AuctionHouseMgr.GetAItem(item.GetGUID()) == null && item.CanBeTraded() && !item.IsNotEmptyBag() &&
                !item.GetTemplate().HasFlag(ItemFlags.Conjured) && item.m_itemData.Expiration == Seconds.Zero;
        }

        [WorldPacketHandler(ClientOpcodes.AuctionSellCommodity)]
        void HandleAuctionSellCommodity(AuctionSellCommodity sellCommodity)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, sellCommodity.TaintedBy.HasValue, AuctionCommand.SellItem);
            if (throttle.Throttled)
                return;

            if (sellCommodity.UnitPrice <= 0 || sellCommodity.UnitPrice > PlayerConst.MaxMoneyAmount)
            {
                Log.outDebug(LogFilter.Network, $"WORLD: HandleAuctionSellCommodity - Player {_player.GetName()} ({_player.GetGUID()}) attempted to sell item with invalid price.");
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                return;
            }

            Creature creature = GetAuctioneerForSession(sellCommodity.Auctioneer, nameof(HandleAuctionSellCommodity));
            if (creature == null)
                return;

            AuctionHouseRecord auctionHouseEntry = Global.AuctionHouseMgr.GetAuctionHouseEntry(creature.GetFaction());
            if (auctionHouseEntry == null)
            {
                Log.outDebug(LogFilter.Network, $"WORLD: HandleAuctionSellCommodity - Unit ({sellCommodity.Auctioneer}) has wrong faction.");
                return;
            }

            if (!IsValidAuctionRunTime(sellCommodity.RunTime))
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.AuctionHouseBusy, throttle.DelayUntilNext);
                return;
            }

            // find all items for sale
            int totalCount = 0;
            Dictionary<ObjectGuid, (Item Item, int Count)> itemsForSale = new();

            foreach (AuctionItemForSale itemForSale in sellCommodity.Items)
            {
                Item item = _player.GetItemByGuid(itemForSale.Guid);
                if (item == null || itemForSale.UseCount <= 0)
                {
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                    return;
                }

                if (item.GetTemplate().GetMaxStackSize() == 1)
                {
                    // not commodity, must use different packet
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                    return;
                }

                // verify that all items belong to the same bucket
                if (!itemsForSale.Empty() && AuctionsBucketKey.ForItem(item) != AuctionsBucketKey.ForItem(itemsForSale.First().Value.Item))
                {
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                    return;
                }

                if (!CanAuctionItem(item) || item.GetCount() < itemForSale.UseCount)
                {
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                    return;
                }

                int count = itemForSale.UseCount + (itemsForSale.TryGetValue(item.GetGUID(), out var previous) ? previous.Count : 0);
                if (item.GetCount() < count)
                {
                    // check that we have enough of this item to sell
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                    return;
                }

                itemsForSale[item.GetGUID()] = (item, count);
                totalCount += itemForSale.UseCount;
            }

            if (totalCount == 0)
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                return;
            }

            TimeSpan auctionTime = sellCommodity.RunTime * WorldConfig.Values[WorldCfg.RateAuctionTime].Float;
            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());
            ItemTemplate itemTemplate = itemsForSale.First().Value.Item.GetTemplate();

            long totalDeposit = AuctionManager.GetCommodityAuctionDeposit(itemTemplate, sellCommodity.RunTime, totalCount, auctionHouseEntry.DepositRate);
            if (!_player.HasEnoughMoney(totalDeposit))
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                return;
            }

            // each stack becomes its own posting at the same unit price; partial stacks are split off first
            List<(Item Original, Item ForSale, int Count)> stacks = new();
            foreach (var (item, count) in itemsForSale.Values)
            {
                Item forSale = item;
                if (item.GetCount() != count)
                {
                    forSale = item.CloneItem(count, _player);
                    if (forSale == null)
                    {
                        Log.outError(LogFilter.Network, $"CMSG_AUCTION_SELL_COMMODITY: Could not create clone of item {item.GetEntry()}");
                        SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                        return;
                    }
                }

                stacks.Add((item, forSale, count));
            }

            List<AuctionPosting> auctions = new();
            long depositSoFar = 0;
            for (var i = 0; i < stacks.Count; ++i)
            {
                // the last posting takes the rounding remainder so the total matches the quoted deposit
                long deposit = i == stacks.Count - 1 ? totalDeposit - depositSoFar
                    : AuctionManager.GetCommodityAuctionDeposit(itemTemplate, sellCommodity.RunTime, stacks[i].Count, auctionHouseEntry.DepositRate);
                deposit = Math.Max(0, deposit);
                depositSoFar += deposit;

                AuctionPosting auction = new();
                auction.Id = Global.ObjectMgr.GenerateAuctionID();
                auction.Owner = _player.GetGUID();
                auction.OwnerAccount = GetAccountGUID();
                auction.BuyoutPrice = sellCommodity.UnitPrice;
                auction.Deposit = deposit;
                auction.StartTime = LoopTime.ServerTime;
                auction.EndTime = auction.StartTime + auctionTime;
                auction.Item = stacks[i].ForSale;

                if (!Global.AuctionHouseMgr.PendingAuctionAdd(_player, auction))
                {
                    SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                    return;
                }

                auctions.Add(auction);
            }

            Log.outInfo(LogFilter.Network,
                $"CMSG_AUCTION_SELL_COMMODITY: {_player.GetGUID()} {_player.GetName()} is selling item {itemTemplate.GetName()} " +
                $"to auctioneer {creature.GetGUID()} with count {totalCount} with unit price {sellCommodity.UnitPrice} " +
                $"and with time {(Seconds)auctionTime} (in sec) in auctionhouse {auctionHouse.Id}");

            if (HasPermission(RBACPermissions.LogGmTrade))
                Log.outCommand(GetAccountId(),
                    $"GM {GetPlayerName()} (Account: {GetAccountId()}) create auction: {itemTemplate.GetName()} (Entry: {itemTemplate.GetId()} Count: {totalCount})");

            SQLTransaction trans = new();
            for (var i = 0; i < stacks.Count; ++i)
            {
                var (original, forSale, count) = stacks[i];
                if (forSale != original)
                {
                    original.SetCount(original.GetCount() - count);
                    original.SetState(ItemUpdateState.Changed, _player);
                    _player.ItemRemovedQuestCheck(original.GetEntry(), count);
                    original.SaveToDB(trans);
                }
                else
                {
                    _player.MoveItemFromInventory(original.InventoryPosition, true);
                    original.DeleteFromInventoryDB(trans);
                }

                forSale.SaveToDB(trans);
                auctionHouse.AddAuction(trans, auctions[i]);
            }

            _player.SaveInventoryAndGoldToDB(trans);

            int firstAuctionId = auctions[0].Id;
            var auctionPlayerGuid = _player.GetGUID();
            AddTransactionCallback(DB.Characters.AsyncCommitTransaction(trans)).AfterComplete(success =>
            {
                if (GetPlayer() != null && GetPlayer().GetGUID() == auctionPlayerGuid)
                {
                    if (success)
                    {
                        GetPlayer().UpdateCriteria(CriteriaType.ItemsPostedAtAuction, 1);
                        SendAuctionCommandResult(firstAuctionId, AuctionCommand.SellItem, AuctionResult.Ok, throttle.DelayUntilNext);
                    }
                    else
                        SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                }
            });
        }

        [WorldPacketHandler(ClientOpcodes.AuctionSellItem)]
        void HandleAuctionSellItem(AuctionSellItem sellItem)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, sellItem.TaintedBy.HasValue, AuctionCommand.SellItem);
            if (throttle.Throttled)
                return;

            if (sellItem.Items.Count != 1 || sellItem.Items[0].UseCount != 1)
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                return;
            }

            if (sellItem.MinBid == 0 && sellItem.BuyoutPrice == 0)
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                return;
            }

            if (sellItem.MinBid < 0 || sellItem.BuyoutPrice < 0 || sellItem.MinBid > PlayerConst.MaxMoneyAmount || sellItem.BuyoutPrice > PlayerConst.MaxMoneyAmount)
            {
                Log.outError(LogFilter.Network,
                    $"WORLD: HandleAuctionSellItem - Player {_player.GetName()} ({_player.GetGUID()}) " +
                    $"attempted to sell item with higher price than max gold amount.");
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.Inventory, throttle.DelayUntilNext, InventoryResult.TooMuchGold);
                return;
            }

            Creature creature = GetAuctioneerForSession(sellItem.Auctioneer, nameof(HandleAuctionSellItem));
            if (creature == null)
                return;

            AuctionHouseRecord auctionHouseEntry = Global.AuctionHouseMgr.GetAuctionHouseEntry(creature.GetFaction());
            if (auctionHouseEntry == null)
            {
                Log.outError(LogFilter.Network, $"WORLD: HandleAuctionSellItem - Unit ({sellItem.Auctioneer}) has wrong faction.");
                return;
            }

            if (!IsValidAuctionRunTime(sellItem.RunTime))
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.AuctionHouseBusy, throttle.DelayUntilNext);
                return;
            }

            Item item = _player.GetItemByGuid(sellItem.Items[0].Guid);
            if (item == null)
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                return;
            }

            if (item.GetTemplate().GetMaxStackSize() > 1)
            {
                // commodity, must use different packet
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.ItemNotFound, throttle.DelayUntilNext);
                return;
            }

            if (!CanAuctionItem(item) || item.GetCount() != 1)
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                return;
            }

            TimeSpan auctionTime = sellItem.RunTime * WorldConfig.Values[WorldCfg.RateAuctionTime].Float;
            AuctionHouseObject auctionHouse = Global.AuctionHouseMgr.GetAuctionsMap(creature.GetFaction());

            long deposit = AuctionManager.GetItemAuctionDeposit(_player, item, sellItem.RunTime, auctionHouseEntry.DepositRate);
            if (!_player.HasEnoughMoney(deposit))
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                return;
            }

            int auctionId = Global.ObjectMgr.GenerateAuctionID();

            AuctionPosting auction = new();
            auction.Id = auctionId;
            auction.Owner = _player.GetGUID();
            auction.OwnerAccount = GetAccountGUID();
            auction.MinBid = sellItem.MinBid;
            auction.BuyoutPrice = sellItem.BuyoutPrice;
            auction.Deposit = deposit;
            auction.BidAmount = sellItem.MinBid;
            auction.StartTime = LoopTime.ServerTime;
            auction.EndTime = auction.StartTime + auctionTime;

            if (HasPermission(RBACPermissions.LogGmTrade))
                Log.outCommand(GetAccountId(),
                    $"GM {GetPlayerName()} (Account: {GetAccountId()}) " +
                    $"create auction: {item.GetTemplate().GetName()} " +
                    $"(Entry: {item.GetEntry()} Count: {item.GetCount()})");

            auction.Item = item;

            Log.outInfo(LogFilter.Network,
                $"CMSG_AUCTION_SELL_ITEM: {_player.GetGUID()} {_player.GetName()} " +
                $"is selling item {item.GetGUID()} {item.GetTemplate().GetName()} " +
                $"to auctioneer {creature.GetGUID()} with count {item.GetCount()} " +
                $"with initial bid {sellItem.MinBid} with buyout {sellItem.BuyoutPrice} " +
                $"and with time {(Seconds)auctionTime} (in sec) " +
                $"in auctionhouse {auctionHouse.Id}");

            // Add to pending auctions, or fail with insufficient funds error
            if (!Global.AuctionHouseMgr.PendingAuctionAdd(_player, auction))
            {
                SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.NotEnoughMoney, throttle.DelayUntilNext);
                return;
            }

            _player.MoveItemFromInventory(item.InventoryPosition, true);

            SQLTransaction trans = new();
            item.DeleteFromInventoryDB(trans);
            item.SaveToDB(trans);

            auctionHouse.AddAuction(trans, auction);
            _player.SaveInventoryAndGoldToDB(trans);

            var auctionPlayerGuid = _player.GetGUID();
            AddTransactionCallback(DB.Characters.AsyncCommitTransaction(trans)).AfterComplete(success =>
            {
                if (GetPlayer() != null && GetPlayer().GetGUID() == auctionPlayerGuid)
                {
                    if (success)
                    {
                        GetPlayer().UpdateCriteria(CriteriaType.ItemsPostedAtAuction, 1);
                        SendAuctionCommandResult(auctionId, AuctionCommand.SellItem, AuctionResult.Ok, throttle.DelayUntilNext);
                    }
                    else
                        SendAuctionCommandResult(0, AuctionCommand.SellItem, AuctionResult.DatabaseError, throttle.DelayUntilNext);
                }
            });
        }

        [WorldPacketHandler(ClientOpcodes.AuctionSetFavoriteItem)]
        void HandleAuctionSetFavoriteItem(AuctionSetFavoriteItem setFavoriteItem)
        {
            AuctionThrottleResult throttle = Global.AuctionHouseMgr.CheckThrottle(_player, false);
            if (throttle.Throttled)
                return;

            SQLTransaction trans = new();

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_CHARACTER_FAVORITE_AUCTION);
            stmt.SetInt64(0, _player.GetGUID().GetCounter());
            stmt.SetInt32(1, setFavoriteItem.Item.Order);
            trans.Append(stmt);

            if (!setFavoriteItem.IsNotFavorite)
            {
                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_CHARACTER_FAVORITE_AUCTION);
                stmt.SetInt64(0, _player.GetGUID().GetCounter());
                stmt.SetInt32(1, setFavoriteItem.Item.Order);
                stmt.SetInt32(2, setFavoriteItem.Item.ItemID);
                stmt.SetInt32(3, setFavoriteItem.Item.ItemLevel);
                stmt.SetInt32(4, setFavoriteItem.Item.BattlePetSpeciesID);
                stmt.SetInt32(5, setFavoriteItem.Item.SuffixItemNameDescriptionID);
                trans.Append(stmt);
            }

            DB.Characters.CommitTransaction(trans);
        }

        public void SendAuctionFavoriteList()
        {
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_CHARACTER_FAVORITE_AUCTIONS);
            stmt.SetInt64(0, _player.GetGUID().GetCounter());
            GetQueryProcessor().AddCallback(DB.Characters.AsyncQuery(stmt).WithCallback(result =>
            {
                AuctionFavoriteList favoriteItems = new();
                if (!result.IsEmpty())
                {
                    do
                    {
                        AuctionFavoriteInfo item = new();
                        item.Order = result.Read<int>(0);
                        item.ItemID = result.Read<int>(1);
                        item.ItemLevel = result.Read<int>(2);
                        item.BattlePetSpeciesID = result.Read<int>(3);
                        item.SuffixItemNameDescriptionID = result.Read<int>(4);
                        favoriteItems.Items.Add(item);
                    } while (result.NextRow());
                }

                SendPacket(favoriteItems);
            }));
        }

        public void SendAuctionHello(ObjectGuid guid, Creature unit)
        {
            if (GetPlayer().GetLevel() < WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32)
            {
                SendNotification(Global.ObjectMgr.GetCypherString(CypherStrings.AuctionReq), WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32);
                return;
            }

            AuctionHouseRecord ahEntry = Global.AuctionHouseMgr.GetAuctionHouseEntry(unit.GetFaction());
            if (ahEntry == null)
                return;

            AuctionHelloResponse auctionHelloResponse = new();
            auctionHelloResponse.AuctionHouseId = ahEntry.Id;
            auctionHelloResponse.Guid = guid;
            auctionHelloResponse.OpenForBusiness = true;
            SendPacket(auctionHelloResponse);
        }

        public void SendAuctionCommandResult(int auctionId, AuctionCommand command, AuctionResult errorCode, TimeSpan delayForNextAction, InventoryResult bagError = 0)
        {
            AuctionCommandResult auctionCommandResult = new();
            auctionCommandResult.AuctionID = auctionId;
            auctionCommandResult.Command = (int)command;
            auctionCommandResult.ErrorCode = errorCode;
            auctionCommandResult.BagResult = bagError;
            auctionCommandResult.DesiredDelay = delayForNextAction;
            SendPacket(auctionCommandResult);
        }

        public void SendAuctionClosedNotification(AuctionPosting auction, TimeSpan mailDelay, bool sold)
        {
            AuctionClosedNotification packet = new();
            packet.Info.Initialize(auction);
            packet.ProceedsMailDelay = mailDelay;
            packet.Sold = sold;
            SendPacket(packet);
        }

        public void SendAuctionOwnerBidNotification(AuctionPosting auction)
        {
            AuctionOwnerBidNotification packet = new();
            packet.Info.Initialize(auction);
            packet.Bidder = auction.Bidder;
            packet.MinIncrement = auction.CalculateMinIncrement();
            SendPacket(packet);
        }
    }
}
