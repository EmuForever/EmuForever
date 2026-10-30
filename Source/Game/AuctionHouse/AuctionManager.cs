// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Entities;
using Game.Mails;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    // The Forever 1.60.1 client uses the retail auction house: browsing returns buckets (one per item key), and stackable
    // items are commodities bought by quantity through a price quote. Each posting here still holds a single item, so a
    // commodity bucket is the set of postings of that item sorted by unit price (TC AuctionHouseMgr.cpp).
    public class AuctionManager : Singleton<AuctionManager>
    {
        AuctionHouseObject mAllianceAuctions;
        AuctionHouseObject mHordeAuctions;
        AuctionHouseObject mNeutralAuctions;

        Dictionary<ObjectGuid, Item> _itemsByGuid = new();
        Dictionary<ObjectGuid, PlayerPendingAuctions> _pendingAuctionsByPlayer = new();
        Dictionary<ObjectGuid, PlayerThrottleObject> _playerThrottleObjects = new();

        int _replicateIdGenerator;

        ServerTime _playerThrottleObjectsCleanupTime;

        AuctionManager()
        {
            mHordeAuctions = new AuctionHouseObject(AuctionHouseId.Horde);
            mAllianceAuctions = new AuctionHouseObject(AuctionHouseId.Alliance);
            mNeutralAuctions = new AuctionHouseObject(AuctionHouseId.Neutral);
        }

        public Item GetAItem(ObjectGuid itemGuid)
        {
            return _itemsByGuid.LookupByKey(itemGuid);
        }

        public string BuildItemAuctionMailSubject(AuctionMailType type, AuctionPosting auction)
        {
            return BuildAuctionMailSubject(
                auction.Item.GetEntry(),
                type,
                auction.Id,
                auction.Item.GetCount(),
                auction.Item.GetModifier(ItemModifier.BattlePetSpeciesId),
                auction.Item.GetContext());
        }

        public string BuildCommodityAuctionMailSubject(AuctionMailType type, int itemId, int itemCount)
        {
            return BuildAuctionMailSubject(itemId, type, 0, itemCount, 0, ItemContext.None);
        }

        // TC: "{itemId}:0:{type}:{auctionId}:{count}:{petSpecies}:0:0:0:0:{context}:{bonusListCount}:{bonusLists}"
        public string BuildAuctionMailSubject(int itemId, AuctionMailType type, int auctionId, int itemCount, int battlePetSpeciesId, ItemContext context)
        {
            return $"{itemId}:0:{(int)type}:{auctionId}:{itemCount}:{battlePetSpeciesId}:0:0:0:0:{(int)context}:0";
        }

        public string BuildAuctionWonMailBody(ObjectGuid guid, long bid, long buyout)
        {
            return $"{ObjectGuidInfo.Format(guid)}:{bid}:{buyout}:0";
        }

        public string BuildAuctionSoldMailBody(ObjectGuid guid, long bid, long buyout, long deposit, long consignment)
        {
            return $"{ObjectGuidInfo.Format(guid)}:{bid}:{buyout}:{deposit}:{consignment}:0";
        }

        public string BuildAuctionInvoiceMailBody(ObjectGuid guid, long bid, long buyout, long deposit, long consignment, TimeSpan moneyDelay, RealmTime eta)
        {
            return $"{ObjectGuidInfo.Format(guid)}:{bid}:{buyout}:{deposit}:{consignment}:{(Seconds)moneyDelay}:{(WowTime)eta}:0";
        }

        // Classic 1.60: deposit DepositRate% of the vendor price per 2 hours (AuctionHouse.db2: 5 at the faction houses, 25 at
        // the neutral goblin house), at least 1 copper (TC AuctionHouseMgr::GetCommodityAuctionDeposit)
        public static long GetCommodityAuctionDeposit(ItemTemplate item, TimeSpan time, int quantity, int depositRate)
        {
            long sellPrice = item.GetSellPrice();
            long periods = (long)(time.TotalMinutes / SharedConst.MinAuctionTime.TotalMinutes);
            return (long)Math.Max(1.0, Math.Floor(depositRate / 100.0 * quantity * sellPrice)) * periods;
        }

        public static long GetItemAuctionDeposit(Player player, Item item, TimeSpan time, int depositRate)
        {
            long sellPrice = item.GetSellPrice(player);
            long periods = (long)(time.TotalMinutes / SharedConst.MinAuctionTime.TotalMinutes);
            return (long)Math.Max(1.0, Math.Floor(sellPrice * (depositRate / 100.0))) * periods;
        }

        public void LoadAuctions()
        {
            // need to clear in case we are reloading
            _itemsByGuid.Clear();

            // data needs to be at first place for Item.LoadFromDB
            MultiMap<int, Item> itemsByAuction = new();
            MultiMap<int, ObjectGuid> biddersByAuction = new();

            // perfomance and quantity counters
            var count = 0;
            RelativeTime oldMSTime = Time.NowRelative;

            {
                SQLResult result = DB.Characters.Query(CharacterDatabase.GetPreparedStatement(CharStatements.SEL_AUCTION_ITEMS));

                if (result.IsEmpty())
                {
                    Log.outInfo(LogFilter.ServerLoading, "Loaded 0 auctions. DB table `auctionhouse` is empty.");
                    return;
                }

                do
                {
                    var itemGuid = result.Read<long>(0);
                    var itemEntry = result.Read<int>(1);

                    ItemTemplate proto = Global.ObjectMgr.GetItemTemplate(itemEntry);
                    if (proto == null)
                    {
                        Log.outError(LogFilter.Misc,
                            $"AuctionHouseMgr.LoadAuctionItems: " +
                            $"Unknown item (GUID: {itemGuid} item entry: #{itemEntry}) in auction, skipped.");
                        continue;
                    }

                    Item item = Item.NewItemOrBag(proto);
                    if (!item.LoadFromDB(itemGuid, ObjectGuid.Create(HighGuid.Player, result.Read<long>(46)), result.GetFields(), itemEntry))
                    {
                        item.Dispose();
                        continue;
                    }

                    var auctionId = result.Read<int>(47);
                    itemsByAuction.Add(auctionId, item);

                    ++count;
                } while (result.NextRow());

                Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} auction items in {Time.Diff(oldMSTime)} ms.");
            }

            oldMSTime = Time.NowRelative;
            count = 0;

            {
                SQLResult result = DB.Characters.Query(CharacterDatabase.GetPreparedStatement(CharStatements.SEL_AUCTION_BIDDERS));

                if (!result.IsEmpty())
                {
                    do
                    {
                        biddersByAuction.Add(result.Read<int>(0), ObjectGuid.Create(HighGuid.Player, result.Read<long>(1)));
                        ++count;
                    } while (result.NextRow());
                }

                Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} auction bidders in {Time.Diff(oldMSTime)} ms.");
            }

            oldMSTime = Time.NowRelative;
            count = 0;

            {
                SQLResult result = DB.Characters.Query(CharacterDatabase.GetPreparedStatement(CharStatements.SEL_AUCTIONS));

                if (!result.IsEmpty())
                {
                    SQLTransaction trans = new();
                    do
                    {
                        AuctionPosting auction = new();
                        auction.Id = result.Read<int>(0);

                        AuctionHouseId auctionHouseId = (AuctionHouseId)result.Read<int>(1);
                        var auctionHouse = GetAuctionsById(auctionHouseId);

                        if (auctionHouse == null)
                        {
                            Log.outError(LogFilter.Misc, $"Auction {auction.Id} has wrong auctionHouseId {auctionHouseId}");
                            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_AUCTION);
                            stmt.SetInt32(0, auction.Id);
                            trans.Append(stmt);
                            continue;
                        }

                        if (!itemsByAuction.ContainsKey(auction.Id))
                        {
                            Log.outError(LogFilter.Misc, $"Auction {auction.Id} has no items");
                            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_AUCTION);
                            stmt.SetInt32(0, auction.Id);
                            trans.Append(stmt);
                            continue;
                        }

                        auction.Item = itemsByAuction.Extract(auction.Id)[0];
                        auction.Owner = ObjectGuid.Create(HighGuid.Player, result.Read<long>(2));
                        auction.OwnerAccount = ObjectGuid.Create(HighGuid.WowAccount, Global.CharacterCacheStorage.GetCharacterAccountIdByGuid(auction.Owner));
                        var bidder = result.Read<long>(3);
                        if (bidder != 0)
                            auction.Bidder = ObjectGuid.Create(HighGuid.Player, bidder);

                        auction.MinBid = result.Read<long>(4);
                        auction.BuyoutPrice = result.Read<long>(5);
                        auction.Deposit = result.Read<long>(6);
                        auction.BidAmount = result.Read<long>(7);
                        auction.StartTime = (ServerTime)(UnixTime64)result.Read<long>(8);
                        auction.EndTime = (ServerTime)(UnixTime64)result.Read<long>(9);
                        auction.ServerFlags = (AuctionPostingServerFlag)result.Read<byte>(10);
                        auction.BidderHistory = biddersByAuction.Extract(auction.Id).ToHashSet();

                        auctionHouse.AddAuction(null, auction);

                        ++count;
                    } while (result.NextRow());

                    DB.Characters.CommitTransaction(trans);
                }

                Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} auctions in {Time.Diff(oldMSTime)} ms.");
            }
        }

        public void AddAItem(Item item)
        {
            Cypher.Assert(item != null);
            Cypher.Assert(!_itemsByGuid.ContainsKey(item.GetGUID()));
            _itemsByGuid[item.GetGUID()] = item;
        }

        public bool RemoveAItem(ObjectGuid guid, bool deleteItem = false, SQLTransaction trans = null)
        {
            var item = _itemsByGuid.LookupByKey(guid);
            if (item == null)
                return false;

            if (deleteItem)
            {
                item.FSetState(ItemUpdateState.Removed);
                item.SaveToDB(trans);
            }

            _itemsByGuid.Remove(guid);
            return true;
        }

        public bool PendingAuctionAdd(Player player, AuctionPosting newAuction)
        {
            if (!_pendingAuctionsByPlayer.ContainsKey(player.GetGUID()))
                _pendingAuctionsByPlayer[player.GetGUID()] = new PlayerPendingAuctions();

            var pendingAuction = _pendingAuctionsByPlayer[player.GetGUID()];
            // Get deposit so far
            long totalDeposit = 0;
            foreach (var auction in pendingAuction.Auctions)
                totalDeposit += auction.Deposit;

            // Add this deposit
            totalDeposit += newAuction.Deposit;

            if (!player.HasEnoughMoney(totalDeposit))
                return false;

            pendingAuction.Auctions.Add(newAuction);
            return true;
        }

        public int PendingAuctionCount(Player player)
        {
            var itr = _pendingAuctionsByPlayer.LookupByKey(player.GetGUID());
            if (itr != null)
                return itr.Auctions.Count;

            return 0;
        }

        public void PendingAuctionProcess(Player player)
        {
            var playerPendingAuctions = _pendingAuctionsByPlayer.LookupByKey(player.GetGUID());
            if (playerPendingAuctions == null)
                return;

            long totaldeposit = 0;
            var auctionIndex = 0;

            for (; auctionIndex < playerPendingAuctions.Auctions.Count; ++auctionIndex)
            {
                var pendingAuction = playerPendingAuctions.Auctions[auctionIndex];
                if (!player.HasEnoughMoney(totaldeposit + pendingAuction.Deposit))
                    break;

                totaldeposit += pendingAuction.Deposit;
            }

            // expire auctions we cannot afford
            if (auctionIndex < playerPendingAuctions.Auctions.Count)
            {
                SQLTransaction trans = new();

                do
                {
                    AuctionPosting auction = playerPendingAuctions.Auctions[auctionIndex];
                    auction.EndTime = LoopTime.ServerTime;

                    PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_AUCTION_EXPIRATION);
                    stmt.SetInt32(0, LoopTime.UnixServerTime);
                    stmt.SetInt32(1, auction.Id);
                    trans.Append(stmt);
                    ++auctionIndex;
                } while (auctionIndex < playerPendingAuctions.Auctions.Count);

                DB.Characters.CommitTransaction(trans);
            }

            _pendingAuctionsByPlayer.Remove(player.GetGUID());
            player.ModifyMoney(-totaldeposit);
        }

        public void UpdatePendingAuctions()
        {
            foreach (var pair in _pendingAuctionsByPlayer.ToList())
            {
                ObjectGuid playerGUID = pair.Key;
                Player player = Global.ObjAccessor.FindConnectedPlayer(playerGUID);
                if (player != null)
                {
                    // Check if there were auctions since last update process if not
                    if (PendingAuctionCount(player) == pair.Value.LastAuctionsSize)
                        PendingAuctionProcess(player);
                    else
                        _pendingAuctionsByPlayer[playerGUID].LastAuctionsSize = PendingAuctionCount(player);
                }
                else
                {
                    // Expire any auctions that we couldn't get a deposit for
                    Log.outWarn(LogFilter.Auctionhouse, $"Player {playerGUID} was offline, unable to retrieve deposit!");

                    SQLTransaction trans = new();
                    foreach (var auction in pair.Value.Auctions)
                    {
                        auction.EndTime = LoopTime.ServerTime;

                        PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_AUCTION_EXPIRATION);
                        stmt.SetInt32(0, LoopTime.UnixServerTime);
                        stmt.SetInt32(1, auction.Id);
                        trans.Append(stmt);
                    }
                    DB.Characters.CommitTransaction(trans);
                    _pendingAuctionsByPlayer.Remove(playerGUID);
                }
            }
        }

        public void Update()
        {
            ServerTime now = LoopTime.ServerTime;

            mHordeAuctions.Update(now);
            mAllianceAuctions.Update(now);
            mNeutralAuctions.Update(now);

            if (now >= _playerThrottleObjectsCleanupTime)
            {
                foreach (var pair in _playerThrottleObjects.ToList())
                {
                    if (pair.Value.PeriodEnd < now)
                        _playerThrottleObjects.Remove(pair.Key);
                }

                _playerThrottleObjectsCleanupTime = now + (Hours)1;
            }
        }

        public int GenerateReplicationId()
        {
            return ++_replicateIdGenerator;
        }

        public AuctionThrottleResult CheckThrottle(Player player, bool addonTainted, AuctionCommand command = AuctionCommand.SellItem)
        {
            ServerTime now = LoopTime.ServerTime;

            if (!_playerThrottleObjects.ContainsKey(player.GetGUID()))
                _playerThrottleObjects[player.GetGUID()] = new PlayerThrottleObject();

            var throttleObject = _playerThrottleObjects[player.GetGUID()];
            if (now > throttleObject.PeriodEnd)
            {
                throttleObject.PeriodEnd = now + (Minutes)1;
                throttleObject.QueriesRemaining = 100;
            }

            if (throttleObject.QueriesRemaining == 0)
            {
                player.GetSession().SendAuctionCommandResult(0, command, AuctionResult.AuctionHouseBusy, throttleObject.PeriodEnd - now);
                return new AuctionThrottleResult(TimeSpan.Zero, true);
            }

            if ((--throttleObject.QueriesRemaining) == 0)
                return new AuctionThrottleResult(throttleObject.PeriodEnd - now, false);
            else
                return new AuctionThrottleResult(WorldConfig.Values[addonTainted ? WorldCfg.AuctionTaintedSearchDelay : WorldCfg.AuctionSearchDelay].TimeSpan, false);
        }

        public AuctionHouseObject GetAuctionsById(AuctionHouseId auctionHouseId)
        {
            switch (auctionHouseId)
            {
                case AuctionHouseId.Alliance:
                    return mAllianceAuctions;
                case AuctionHouseId.Horde:
                    return mHordeAuctions;
                default:
                    return mNeutralAuctions;
            }
        }

        public AuctionHouseObject GetAuctionsMap(int factionTemplateId)
        {
            if (WorldConfig.Values[WorldCfg.AllowTwoSideInteractionAuction].Bool)
                return mNeutralAuctions;

            // teams have linked auction houses
            FactionTemplateRecord uEntry = CliDB.FactionTemplateStorage.LookupByKey(factionTemplateId);
            if (uEntry == null)
                return mNeutralAuctions;
            else if (uEntry.FactionGroup.HasAnyFlag((byte)FactionMasks.Alliance))
                return mAllianceAuctions;
            else if (uEntry.FactionGroup.HasAnyFlag((byte)FactionMasks.Horde))
                return mHordeAuctions;
            else
                return mNeutralAuctions;
        }

        public AuctionHouseRecord GetAuctionHouseEntry(int factionTemplateId)
        {
            AuctionHouseId houseId = AuctionHouseId.Neutral;
            if (!WorldConfig.Values[WorldCfg.AllowTwoSideInteractionAuction].Bool)
            {
                // FIXME: found way for proper auctionhouse selection by another way
                // AuctionHouse.dbc have faction field with _player_ factions associated with auction house races.
                // but no easy way convert creature faction to player race faction for specific city

                switch (factionTemplateId)
                {
                    case 120:   // Booty bay, Blackwater Auction House
                    case 474:   // Gadgetzan, Blackwater Auction House
                    case 855:   // Everlook, Blackwater Auction House
                        houseId = AuctionHouseId.Neutral;
                        break;
                    default:
                    {
                        FactionTemplateRecord uEntry = CliDB.FactionTemplateStorage.LookupByKey(factionTemplateId);
                        if (uEntry == null)
                            houseId = AuctionHouseId.Neutral; // Auction House
                        else if (uEntry.FactionGroup.HasAnyFlag((byte)FactionMasks.Alliance))
                            houseId = AuctionHouseId.Alliance; // Alliance Auction House
                        else if (uEntry.FactionGroup.HasAnyFlag((byte)FactionMasks.Horde))
                            houseId = AuctionHouseId.Horde; // Horde Auction House
                        else
                            houseId = AuctionHouseId.Neutral; // Auction House
                        break;
                    }
                }
            }

            return CliDB.AuctionHouseStorage.LookupByKey(houseId);
        }

        class PlayerPendingAuctions
        {
            public List<AuctionPosting> Auctions = new();
            public int LastAuctionsSize;
        }

        class PlayerThrottleObject
        {
            public ServerTime PeriodEnd;
            public byte QueriesRemaining = 100;
        }
    }

    public class AuctionHouseObject
    {
        SortedDictionary<int, AuctionPosting> _itemsByAuctionId = new(); // ordered by auctionId for replication
        Dictionary<AuctionsBucketKey, AuctionsBucketData> _buckets = new();
        Dictionary<ObjectGuid, CommodityQuote> _commodityQuotes = new();

        MultiMap<ObjectGuid, AuctionPosting> _playerOwnedAuctions = new();
        MultiMap<ObjectGuid, AuctionPosting> _playerBidderAuctions = new();

        // Map of throttled players for GetAll, and throttle expiry time
        // Stored here, rather than player object to maintain persistence after logout
        Dictionary<ObjectGuid, PlayerReplicateThrottleData> _replicateThrottleMap = new();

        AuctionHouseRecord _auctionHouse;

        public AuctionPosting GetAuction(int auctionId)
        {
            return _itemsByAuctionId.LookupByKey(auctionId);
        }

        // Read-only candidate discovery. SI purchases still use the ordinary quote/
        // buyout handlers, which revalidate ownership, price, funds and delivery.
        public IEnumerable<AuctionPosting> GetBuyoutAuctionsFor(Player buyer) => _itemsByAuctionId.Values.Where(auction =>
            auction.EndTime > LoopTime.ServerTime && auction.BuyoutPrice > 0 && auction.Item != null &&
            auction.Owner != buyer.GetGUID() && auction.OwnerAccount != buyer.GetSession().GetAccountGUID());

        public void AddAuction(SQLTransaction trans, AuctionPosting auction)
        {
            AuctionsBucketKey key = AuctionsBucketKey.ForItem(auction.Item);
            if (!_buckets.TryGetValue(key, out AuctionsBucketData bucket))
            {
                // we don't have any item for this key yet, create new bucket
                bucket = new AuctionsBucketData(key, auction.Item);
                _buckets[key] = bucket;
            }

            if (trans != null)
            {
                PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_AUCTION);
                stmt.SetInt32(0, auction.Id);
                stmt.SetInt32(1, (int)_auctionHouse.Id);
                stmt.SetInt64(2, auction.Owner.GetCounter());
                stmt.SetInt64(3, ObjectGuid.Empty.GetCounter());
                stmt.SetInt64(4, auction.MinBid);
                stmt.SetInt64(5, auction.BuyoutPrice);
                stmt.SetInt64(6, auction.Deposit);
                stmt.SetInt64(7, auction.BidAmount);
                stmt.SetInt64(8, (UnixTime64)auction.StartTime);
                stmt.SetInt64(9, (UnixTime64)auction.EndTime);
                stmt.SetUInt8(10, (byte)auction.ServerFlags);
                trans.Append(stmt);

                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_AUCTION_ITEMS);
                stmt.SetInt32(0, auction.Id);
                stmt.SetInt64(1, auction.Item.GetGUID().GetCounter());
                trans.Append(stmt);
            }

            Global.AuctionHouseMgr.AddAItem(auction.Item);

            auction.Bucket = bucket;
            bucket.Add(auction);

            _playerOwnedAuctions.Add(auction.Owner, auction);
            foreach (ObjectGuid bidder in auction.BidderHistory)
                _playerBidderAuctions.Add(bidder, auction);

            _itemsByAuctionId[auction.Id] = auction;

            Global.ScriptMgr.OnAuctionAdd(this, auction);
        }

        public void RemoveAuction(SQLTransaction trans, AuctionPosting auction)
        {
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_AUCTION);
            stmt.SetInt32(0, auction.Id);
            trans.Append(stmt);

            Global.AuctionHouseMgr.RemoveAItem(auction.Item.GetGUID());

            Global.ScriptMgr.OnAuctionRemove(this, auction);

            _playerOwnedAuctions.Remove(auction.Owner, auction);
            foreach (ObjectGuid bidder in auction.BidderHistory)
                _playerBidderAuctions.Remove(bidder, auction);

            _itemsByAuctionId.Remove(auction.Id);

            AuctionsBucketData bucket = auction.Bucket;
            if (bucket != null)
            {
                bucket.Remove(auction);
                if (bucket.Auctions.Empty())
                    _buckets.Remove(bucket.Key);
            }
        }

        public void Update(ServerTime now)
        {
            ///- Handle expired auctions

            // Clear expired throttled players
            foreach (var key in _replicateThrottleMap.Keys.ToList())
            {
                if (_replicateThrottleMap[key].NextAllowedReplication <= now)
                    _replicateThrottleMap.Remove(key);
            }

            foreach (var pair in _commodityQuotes.ToList())
            {
                if (pair.Value.ValidTo < now)
                    _commodityQuotes.Remove(pair.Key);
            }

            if (_itemsByAuctionId.Empty())
                return;

            SQLTransaction trans = new();

            ///- filter auctions expired on next update
            List<AuctionPosting> removeList = _itemsByAuctionId.Values.Where(x => !(x.EndTime > now + (Minutes)1)).ToList();
            if (removeList.Empty())
                return;

            foreach (var auction in removeList)
            {
                ///- Either cancel the auction if there was no bidder
                if (auction.Bidder.IsEmpty())
                {
                    SendAuctionExpired(auction, trans);
                    Global.ScriptMgr.OnAuctionExpire(this, auction);
                }
                ///- Or perform the transaction
                else
                {
                    //we should send an "item sold" message if the seller is online
                    //we send the item to the winner
                    //we send the money to the seller
                    SendAuctionSold(auction, null, trans);
                    SendAuctionWon(auction, null, trans);
                    Global.ScriptMgr.OnAuctionSuccessful(this, auction);
                }
            }

            foreach (var auction in removeList)
                RemoveAuction(trans, auction);

            // Run DB changes
            DB.Characters.CommitTransaction(trans);
        }

        public void BuildListBuckets(AuctionListBucketsResult listBucketsResult, Player player, string name, byte minLevel, byte maxLevel,
            AuctionHouseFilterMask filters, AuctionSearchClassFilters classFilters, int offset, IList<AuctionSortDef> sorts)
        {
            Locale locale = player.GetSession().GetSessionDbcLocale();
            name = name?.Trim().ToLowerInvariant() ?? string.Empty;

            List<AuctionsBucketData> matches = new();
            foreach (AuctionsBucketData bucket in _buckets.Values)
            {
                if (!name.IsEmpty())
                {
                    string bucketName = bucket.GetLowerName(locale);
                    if (filters.HasFlag(AuctionHouseFilterMask.ExactMatch))
                    {
                        if (bucketName != name)
                            continue;
                    }
                    else if (!bucketName.Contains(name))
                        continue;
                }

                if (minLevel != 0 && bucket.RequiredLevel < minLevel)
                    continue;

                if (maxLevel != 0 && bucket.RequiredLevel > maxLevel)
                    continue;

                if ((filters & bucket.QualityMask) == 0)
                    continue;

                if (classFilters != null && !classFilters.Matches(bucket.ItemClass, bucket.ItemSubClass, bucket.InventoryType))
                    continue;

                if (filters.HasFlag(AuctionHouseFilterMask.UsableOnly))
                {
                    if (bucket.RequiredLevel != 0 && player.GetLevel() < bucket.RequiredLevel)
                        continue;

                    if (player.CanUseItem(Global.ObjectMgr.GetItemTemplate(bucket.Key.ItemId), true) != InventoryResult.Ok)
                        continue;
                }

                matches.Add(bucket);
            }

            matches.Sort(new AuctionsBucketData.Sorter(locale, sorts));

            foreach (AuctionsBucketData bucket in matches.Skip(offset).Take(SharedConst.AuctionBrowseItemsMax))
                listBucketsResult.Buckets.Add(bucket.BuildBucketInfo(player));

            listBucketsResult.Filters = filters;
            listBucketsResult.TotalCount = matches.Count;
            listBucketsResult.HasMoreResults = matches.Count > offset + listBucketsResult.Buckets.Count;
        }

        public void BuildListBuckets(AuctionListBucketsResult listBucketsResult, Player player, IList<AuctionBucketKey> keys, IList<AuctionSortDef> sorts)
        {
            List<AuctionsBucketData> buckets = new();
            foreach (AuctionBucketKey key in keys)
            {
                if (_buckets.TryGetValue(new AuctionsBucketKey(key), out AuctionsBucketData bucket))
                    buckets.Add(bucket);
            }

            buckets.Sort(new AuctionsBucketData.Sorter(player.GetSession().GetSessionDbcLocale(), sorts));

            foreach (AuctionsBucketData bucket in buckets)
                listBucketsResult.Buckets.Add(bucket.BuildBucketInfo(player));

            listBucketsResult.TotalCount = buckets.Count;
            listBucketsResult.HasMoreResults = false;
        }

        public void BuildListBiddedItems(AuctionListBiddedItemsResult listBiddedItemsResult, Player player, IList<AuctionSortDef> sorts)
        {
            // always full list
            List<AuctionPosting> auctions = _playerBidderAuctions[player.GetGUID()].ToList();
            auctions.Sort(new AuctionPosting.Sorter(player.GetSession().GetSessionDbcLocale(), sorts));

            foreach (AuctionPosting auction in auctions)
            {
                AuctionItem auctionItem = new();
                auction.BuildAuctionItem(auctionItem, true, true, true, false);
                listBiddedItemsResult.Items.Add(auctionItem);
            }

            listBiddedItemsResult.HasMoreResults = false;
        }

        public void BuildListAuctionItems(AuctionListItemsResult listItemsResult, Player player, AuctionsBucketKey bucketKey, int offset, IList<AuctionSortDef> sorts)
        {
            listItemsResult.TotalQuantity = 0;
            if (!_buckets.TryGetValue(bucketKey, out AuctionsBucketData bucket))
                return;

            BuildListAuctionItems(listItemsResult, player, bucket.Auctions, offset, sorts, false);
        }

        public void BuildListAuctionItems(AuctionListItemsResult listItemsResult, Player player, int itemId, int offset, IList<AuctionSortDef> sorts)
        {
            List<AuctionPosting> auctions = new();
            foreach (AuctionsBucketData bucket in _buckets.Values)
                if (bucket.Key.ItemId == itemId)
                    auctions.AddRange(bucket.Auctions);

            BuildListAuctionItems(listItemsResult, player, auctions, offset, sorts, true);
        }

        void BuildListAuctionItems(AuctionListItemsResult listItemsResult, Player player, IEnumerable<AuctionPosting> source, int offset, IList<AuctionSortDef> sorts, bool sendKey)
        {
            List<AuctionPosting> auctions = source.ToList();
            auctions.Sort(new AuctionPosting.Sorter(player.GetSession().GetSessionDbcLocale(), sorts));

            listItemsResult.TotalQuantity = auctions.Sum(auction => auction.Item.GetCount());

            foreach (AuctionPosting auction in auctions.Skip(offset).Take(SharedConst.AuctionListItemsMax))
            {
                AuctionItem auctionItem = new();
                auction.BuildAuctionItem(auctionItem, false, sendKey, auction.OwnerAccount != player.GetSession().GetAccountGUID(), auction.Bidder.IsEmpty());
                listItemsResult.Items.Add(auctionItem);
            }

            listItemsResult.TotalCount = auctions.Count;
            listItemsResult.HasMoreResults = auctions.Count > offset + listItemsResult.Items.Count;
        }

        public void BuildListOwnedItems(AuctionListOwnedItemsResult listOwnedItemsResult, Player player, IList<AuctionSortDef> sorts)
        {
            // always full list
            List<AuctionPosting> auctions = _playerOwnedAuctions[player.GetGUID()].ToList();
            auctions.Sort(new AuctionPosting.Sorter(player.GetSession().GetSessionDbcLocale(), sorts));

            foreach (AuctionPosting auction in auctions)
            {
                AuctionItem auctionItem = new();
                auction.BuildAuctionItem(auctionItem, true, true, false, false);
                listOwnedItemsResult.Items.Add(auctionItem);
            }

            listOwnedItemsResult.HasMoreResults = false;
        }

        public void AddBidder(ObjectGuid bidder, AuctionPosting auction)
        {
            if (!_playerBidderAuctions.Contains(bidder, auction))
                _playerBidderAuctions.Add(bidder, auction);
        }

        public void BuildReplicate(AuctionReplicateResponse replicateResponse, Player player, int global, int cursor, int tombstone, int count)
        {
            ServerTime curTime = LoopTime.ServerTime;

            var throttleData = _replicateThrottleMap.LookupByKey(player.GetGUID());
            if (throttleData == null)
            {
                throttleData = new PlayerReplicateThrottleData();
                throttleData.NextAllowedReplication = curTime + WorldConfig.Values[WorldCfg.AuctionReplicateDelay].TimeSpan;
                throttleData.Global = Global.AuctionHouseMgr.GenerateReplicationId();
                _replicateThrottleMap[player.GetGUID()] = throttleData;
            }
            else
            {
                if (throttleData.Global != global || throttleData.Cursor != cursor || throttleData.Tombstone != tombstone)
                    return;

                if (!throttleData.IsReplicationInProgress() && throttleData.NextAllowedReplication > curTime)
                    return;
            }

            if (_itemsByAuctionId.Empty() || count == 0)
                return;

            foreach (var auction in _itemsByAuctionId.Values.Where(auction => auction.Id > cursor))
            {
                AuctionItem auctionItem = new();
                auction.BuildAuctionItem(auctionItem, false, true, true, auction.Bidder.IsEmpty());
                replicateResponse.Items.Add(auctionItem);
                if (--count == 0)
                    break;
            }

            replicateResponse.ChangeNumberGlobal = throttleData.Global;
            replicateResponse.ChangeNumberCursor = throttleData.Cursor = !replicateResponse.Items.Empty() ? replicateResponse.Items.Last().AuctionID : 0;
            replicateResponse.ChangeNumberTombstone = throttleData.Tombstone = count == 0 ? _itemsByAuctionId.Keys.Last() : 0;
        }

        public long CalculateAuctionHouseCut(long bidAmount)
        {
            return Math.Max((long)(MathFunctions.CalculatePct(bidAmount, _auctionHouse.ConsignmentRate) * WorldConfig.Values[WorldCfg.RateAuctionCut].Float), 0);
        }

        // commodity postings this player may buy, cheapest first
        IEnumerable<AuctionPosting> GetCommodityPostingsFor(Player player, int itemId)
        {
            ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate(itemId);
            if (itemTemplate == null)
                return Enumerable.Empty<AuctionPosting>();

            if (!_buckets.TryGetValue(AuctionsBucketKey.ForCommodity(itemTemplate), out AuctionsBucketData bucket))
                return Enumerable.Empty<AuctionPosting>();

            return bucket.Auctions.Where(auction => auction.IsCommodity() && auction.EndTime > LoopTime.ServerTime
                && auction.Owner != player.GetGUID() && auction.OwnerAccount != player.GetSession().GetAccountGUID());
        }

        bool PriceCommodity(Player player, int itemId, int quantity, out long totalPrice)
        {
            totalPrice = 0;
            int remainingQuantity = quantity;
            foreach (AuctionPosting auction in GetCommodityPostingsFor(player, itemId))
            {
                int taken = Math.Min(auction.Item.GetCount(), remainingQuantity);
                totalPrice += auction.BuyoutPrice * taken;
                remainingQuantity -= taken;
                if (remainingQuantity == 0)
                    break;
            }

            return quantity > 0 && remainingQuantity == 0;
        }

        public CommodityQuote CreateCommodityQuote(Player player, int itemId, int quantity)
        {
            if (!PriceCommodity(player, itemId, quantity, out long totalPrice))
                return null;

            if (!player.HasEnoughMoney(totalPrice))
                return null;

            CommodityQuote quote = new();
            quote.TotalPrice = totalPrice;
            quote.Quantity = quantity;
            quote.ValidTo = LoopTime.ServerTime + (Seconds)30;
            _commodityQuotes[player.GetGUID()] = quote;
            return quote;
        }

        public void CancelCommodityQuote(ObjectGuid guid)
        {
            _commodityQuotes.Remove(guid);
        }

        public bool BuyCommodity(SQLTransaction trans, Player player, int itemId, int quantity, TimeSpan delayForNextAction)
        {
            if (!_commodityQuotes.Remove(player.GetGUID(), out CommodityQuote quote) || !PriceCommodity(player, itemId, quantity, out long totalPrice))
            {
                player.GetSession().SendAuctionCommandResult(0, AuctionCommand.PlaceBid, AuctionResult.CommodityPurchaseFailed, delayForNextAction);
                return false;
            }

            // something was bought between creating quote and finalizing transaction
            // but we allow lower price if new items were posted at lower price
            if (totalPrice > quote.TotalPrice || !player.HasEnoughMoney(totalPrice))
            {
                player.GetSession().SendAuctionCommandResult(0, AuctionCommand.PlaceBid, AuctionResult.CommodityPurchaseFailed, delayForNextAction);
                return false;
            }

            ObjectGuid? uniqueSeller = null;
            List<MailedItemsBatch> batches = new() { new MailedItemsBatch() };
            List<AuctionPosting> soldOut = new();
            AuctionPosting firstAuction = null;
            Item firstItem = null;

            int remainingQuantity = quantity;
            foreach (AuctionPosting auction in GetCommodityPostingsFor(player, itemId).ToList())
            {
                if (remainingQuantity == 0)
                    break;

                if (!uniqueSeller.HasValue)
                    uniqueSeller = auction.Owner;
                else if (uniqueSeller.Value != auction.Owner)
                    uniqueSeller = ObjectGuid.Empty;

                MailedItemsBatch batch = batches.Last();
                if (batch.IsFull())
                {
                    batch = new MailedItemsBatch();
                    batches.Add(batch);
                }

                Item boughtItem;
                int boughtFromAuction;
                if (auction.Item.GetCount() > remainingQuantity)
                {
                    // split the stack: the buyer gets a clone, the rest stays on the auction house
                    boughtItem = auction.Item.CloneItem(remainingQuantity, player);
                    if (boughtItem == null)
                    {
                        player.GetSession().SendAuctionCommandResult(0, AuctionCommand.PlaceBid, AuctionResult.CommodityPurchaseFailed, delayForNextAction);
                        return false;
                    }

                    auction.Item.SetCount(auction.Item.GetCount() - remainingQuantity);
                    auction.Item.FSetState(ItemUpdateState.Changed);
                    auction.Item.SaveToDB(trans);
                    boughtFromAuction = remainingQuantity;
                }
                else
                {
                    boughtItem = auction.Item;
                    boughtFromAuction = auction.Item.GetCount();
                    soldOut.Add(auction);
                }

                remainingQuantity -= boughtFromAuction;
                batch.AddItem(boughtItem, auction.BuyoutPrice);

                if (firstAuction == null)
                {
                    firstAuction = auction;
                    firstItem = boughtItem;
                }

                if (player.GetSession().HasPermission(RBACPermissions.LogGmTrade))
                {
                    int bidderAccId = player.GetSession().GetAccountId();
                    if (!Global.CharacterCacheStorage.GetCharacterNameByGuid(auction.Owner, out string ownerName))
                        ownerName = Global.ObjectMgr.GetCypherString(CypherStrings.Unknown);

                    Log.outCommand(bidderAccId,
                        $"GM {player.GetName()} (Account: {bidderAccId}) bought commodity in auction: " +
                        $"{boughtItem.GetName(Global.WorldMgr.GetDefaultDbcLocale())} (Entry: {boughtItem.GetEntry()} Count: {boughtFromAuction}) " +
                        $"and pay money: {auction.BuyoutPrice * boughtFromAuction}. Original owner {ownerName} " +
                        $"(Account: {Global.CharacterCacheStorage.GetCharacterAccountIdByGuid(auction.Owner)})");
                }

                long auctionHouseCut = CalculateAuctionHouseCut(auction.BuyoutPrice * boughtFromAuction);
                long depositPart = AuctionManager.GetCommodityAuctionDeposit(auction.Item.GetTemplate(), auction.EndTime - auction.StartTime, boughtFromAuction, DepositRate);
                long profit = auction.BuyoutPrice * boughtFromAuction + depositPart - auctionHouseCut;

                Player owner = Global.ObjAccessor.FindConnectedPlayer(auction.Owner);
                if (owner != null)
                {
                    owner.UpdateCriteria(CriteriaType.MoneyEarnedFromAuctions, profit);
                    owner.UpdateCriteria(CriteriaType.HighestAuctionSale, profit);
                    owner.GetSession().SendAuctionClosedNotification(auction, WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan, true);
                }

                new MailDraft(Global.AuctionHouseMgr.BuildCommodityAuctionMailSubject(AuctionMailType.Sold, itemId, boughtFromAuction),
                    Global.AuctionHouseMgr.BuildAuctionSoldMailBody(player.GetGUID(), auction.BuyoutPrice * boughtFromAuction, boughtFromAuction, depositPart, auctionHouseCut))
                    .AddMoney(profit)
                    .SendMailTo(trans, new MailReceiver(owner, auction.Owner), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied, WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan);
            }

            player.ModifyMoney(-totalPrice);
            player.SaveInventoryAndGoldToDB(trans);

            foreach (MailedItemsBatch batch in batches)
            {
                MailDraft mail = new(Global.AuctionHouseMgr.BuildCommodityAuctionMailSubject(AuctionMailType.Won, itemId, batch.Quantity),
                    Global.AuctionHouseMgr.BuildAuctionWonMailBody(uniqueSeller ?? ObjectGuid.Empty, batch.TotalPrice, batch.Quantity));

                for (var i = 0; i < batch.ItemsCount; ++i)
                {
                    PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_AUCTION_ITEMS_BY_ITEM);
                    stmt.SetInt64(0, batch.Items[i].GetGUID().GetCounter());
                    trans.Append(stmt);

                    batch.Items[i].SetOwnerGUID(player.GetGUID());
                    batch.Items[i].SaveToDB(trans);
                    mail.AddItem(batch.Items[i]);
                }

                mail.SendMailTo(trans, player, new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
            }

            if (firstAuction != null)
            {
                AuctionWonBidNotification packet = new();
                packet.Info.Initialize(_auctionHouse.Id, firstAuction, firstItem);
                player.SendPacket(packet);
            }

            // whole stacks were mailed to the buyer, so the postings go away without touching their items again
            foreach (AuctionPosting auction in soldOut)
                RemoveAuction(trans, auction);

            return true;
        }

        // this function notified old bidder that his bid is no longer highest
        public void SendAuctionOutbid(AuctionPosting auction, ObjectGuid newBidder, long newBidAmount, SQLTransaction trans)
        {
            Player oldBidder = Global.ObjAccessor.FindConnectedPlayer(auction.Bidder);

            // old bidder exist
            if ((oldBidder != null || Global.CharacterCacheStorage.HasCharacterCacheEntry(auction.Bidder)))// && !sAuctionBotConfig.IsBotChar(auction.Bidder))
            {
                if (oldBidder != null)
                {
                    AuctionOutbidNotification packet = new();
                    packet.BidAmount = newBidAmount;
                    packet.MinIncrement = AuctionPosting.CalculateMinIncrement(newBidAmount);
                    packet.Info.AuctionId = auction.Id;
                    packet.Info.Bidder = newBidder;
                    packet.Info.Item = new ItemInstance(auction.Item);
                    oldBidder.SendPacket(packet);
                }

                new MailDraft(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Outbid, auction), "")
                    .AddMoney(auction.BidAmount)
                    .SendMailTo(trans, new MailReceiver(oldBidder, auction.Bidder), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
            }
        }

        public void SendAuctionWon(AuctionPosting auction, Player bidder, SQLTransaction trans)
        {
            int bidderAccId;
            if (bidder == null)
                bidder = Global.ObjAccessor.FindConnectedPlayer(auction.Bidder); // try lookup bidder when called from .Update

            // data for gm.log
            string bidderName = "";
            bool logGmTrade = auction.ServerFlags.HasFlag(AuctionPostingServerFlag.GmLogBuyer);

            if (bidder != null)
            {
                bidderAccId = bidder.GetSession().GetAccountId();
                bidderName = bidder.GetName();
            }
            else
            {
                bidderAccId = Global.CharacterCacheStorage.GetCharacterAccountIdByGuid(auction.Bidder);

                if (logGmTrade && !Global.CharacterCacheStorage.GetCharacterNameByGuid(auction.Bidder, out bidderName))
                    bidderName = Global.ObjectMgr.GetCypherString(CypherStrings.Unknown);
            }

            if (logGmTrade)
            {
                if (!Global.CharacterCacheStorage.GetCharacterNameByGuid(auction.Owner, out string ownerName))
                    ownerName = Global.ObjectMgr.GetCypherString(CypherStrings.Unknown);

                int ownerAccId = Global.CharacterCacheStorage.GetCharacterAccountIdByGuid(auction.Owner);

                Log.outCommand(bidderAccId,
                    $"GM {bidderName} (Account: {bidderAccId}) won item in auction: " +
                    $"{auction.Item.GetName(Global.WorldMgr.GetDefaultDbcLocale())} (Entry: {auction.Item.GetEntry()}" +
                    $" Count: {auction.Item.GetCount()}) and pay money: {auction.BidAmount}. " +
                    $"Original owner {ownerName} (Account: {ownerAccId})");
            }

            // receiver exist
            if ((bidder != null || bidderAccId != 0))// && !sAuctionBotConfig.IsBotChar(auction.Bidder))
            {
                MailDraft mail = new(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Won, auction),
                    Global.AuctionHouseMgr.BuildAuctionWonMailBody(auction.Owner, auction.BidAmount, auction.BuyoutPrice));

                // set owner to bidder (to prevent delete item with sender char deleting)
                // owner in `data` will set at mail receive and item extracting
                PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_ITEM_OWNER);
                stmt.SetInt64(0, auction.Bidder.GetCounter());
                stmt.SetInt64(1, auction.Item.GetGUID().GetCounter());
                trans.Append(stmt);

                mail.AddItem(auction.Item);

                if (bidder != null)
                {
                    AuctionWonBidNotification packet = new();
                    packet.Info.Initialize(_auctionHouse.Id, auction, auction.Item);
                    bidder.SendPacket(packet);

                    // FIXME: for offline player need also
                    bidder.UpdateCriteria(CriteriaType.AuctionsWon, 1);
                }

                mail.SendMailTo(trans, new MailReceiver(bidder, auction.Bidder), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
            }
            else
            {
                // bidder doesn't exist, delete the item
                Global.AuctionHouseMgr.RemoveAItem(auction.Item.GetGUID(), true, trans);
            }
        }

        //call this method to send mail to auction owner, when auction is successful, it does not clear ram
        public void SendAuctionSold(AuctionPosting auction, Player owner, SQLTransaction trans)
        {
            if (owner == null)
                owner = Global.ObjAccessor.FindConnectedPlayer(auction.Owner);

            // owner exist
            if ((owner != null || Global.CharacterCacheStorage.HasCharacterCacheEntry(auction.Owner)))// && !sAuctionBotConfig.IsBotChar(auction.Owner))
            {
                long auctionHouseCut = CalculateAuctionHouseCut(auction.BidAmount);
                long profit = auction.BidAmount + auction.Deposit - auctionHouseCut;

                //FIXME: what do if owner offline
                if (owner != null)
                {
                    owner.UpdateCriteria(CriteriaType.MoneyEarnedFromAuctions, profit);
                    owner.UpdateCriteria(CriteriaType.HighestAuctionSale, auction.BidAmount);
                    //send auction owner notification, bidder must be current!
                    owner.GetSession().SendAuctionClosedNotification(auction, WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan, true);
                }

                new MailDraft(
                    Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Sold, auction),
                    Global.AuctionHouseMgr.BuildAuctionSoldMailBody(auction.Bidder, auction.BidAmount, auction.BuyoutPrice, auction.Deposit, auctionHouseCut))
                    .AddMoney(profit)
                    .SendMailTo(trans,
                        new MailReceiver(owner, auction.Owner),
                        new MailSender(_auctionHouse.Id), MailCheckFlags.Copied, WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan);
            }
        }

        public void SendAuctionExpired(AuctionPosting auction, SQLTransaction trans)
        {
            Player owner = Global.ObjAccessor.FindConnectedPlayer(auction.Owner);
            // owner exist
            if ((owner != null || Global.CharacterCacheStorage.HasCharacterCacheEntry(auction.Owner)))// && !sAuctionBotConfig.IsBotChar(auction.Owner))
            {
                if (owner != null)
                    owner.GetSession().SendAuctionClosedNotification(auction, TimeSpan.Zero, false);

                MailDraft mail = new(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Expired, auction), "");

                mail.AddItem(auction.Item);

                mail.SendMailTo(trans, new MailReceiver(owner, auction.Owner), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
            }
            else
            {
                // owner doesn't exist, delete the item
                Global.AuctionHouseMgr.RemoveAItem(auction.Item.GetGUID(), true, trans);
            }
        }

        public void SendAuctionRemoved(AuctionPosting auction, Player owner, SQLTransaction trans)
        {
            MailDraft draft = new(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Cancelled, auction), "");
            draft.AddItem(auction.Item);
            draft.SendMailTo(trans, owner, new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
        }

        //this function sends mail, when auction is cancelled to old bidder
        public void SendAuctionCancelledToBidder(AuctionPosting auction, SQLTransaction trans)
        {
            Player bidder = Global.ObjAccessor.FindConnectedPlayer(auction.Bidder);

            // bidder exist
            if ((bidder != null || Global.CharacterCacheStorage.HasCharacterCacheEntry(auction.Bidder)))// && !sAuctionBotConfig.IsBotChar(auction.Bidder))
                new MailDraft(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Removed, auction), "")
                .AddMoney(auction.BidAmount)
                .SendMailTo(trans, new MailReceiver(bidder, auction.Bidder), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
        }

        public void SendAuctionInvoice(AuctionPosting auction, Player owner, SQLTransaction trans)
        {
            if (owner == null)
                owner = Global.ObjAccessor.FindConnectedPlayer(auction.Owner);

            // owner exist (online or offline)
            if ((owner != null || Global.CharacterCacheStorage.HasCharacterCacheEntry(auction.Owner)))// && !sAuctionBotConfig.IsBotChar(auction.Owner))
            {
                RealmTime eta = LoopTime.RealmTime;
                eta += WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan;

                new MailDraft(Global.AuctionHouseMgr.BuildItemAuctionMailSubject(AuctionMailType.Invoice, auction),
                    Global.AuctionHouseMgr.BuildAuctionInvoiceMailBody(auction.Bidder, auction.BidAmount, auction.BuyoutPrice, auction.Deposit,
                        CalculateAuctionHouseCut(auction.BidAmount), WorldConfig.Values[WorldCfg.MailDeliveryDelay].TimeSpan, eta))
                    .SendMailTo(trans, new MailReceiver(owner, auction.Owner), new MailSender(_auctionHouse.Id), MailCheckFlags.Copied);
            }
        }

        class PlayerReplicateThrottleData
        {
            public int Global;
            public int Cursor;
            public int Tombstone;
            public ServerTime NextAllowedReplication = ServerTime.Zero;

            public bool IsReplicationInProgress() { return Cursor != Tombstone && Global != 0; }
        }

        class MailedItemsBatch
        {
            public Item[] Items = new Item[SharedConst.MaxMailItems];
            public long TotalPrice;
            public int Quantity;

            public int ItemsCount;

            public bool IsFull() { return ItemsCount >= Items.Length; }

            public void AddItem(Item item, long unitPrice)
            {
                Items[ItemsCount++] = item;
                Quantity += item.GetCount();
                TotalPrice += unitPrice * item.GetCount();
            }
        }

        public AuctionHouseObject(AuctionHouseId auctionHouseId)
        {
            _auctionHouse = CliDB.AuctionHouseStorage.LookupByKey(auctionHouseId);
        }

        public AuctionHouseId Id => _auctionHouse.Id;
        public int ConsignmentRate => _auctionHouse.ConsignmentRate;
        public int DepositRate => _auctionHouse.DepositRate;
    }

    public class CommodityQuote
    {
        public long TotalPrice;
        public int Quantity;
        public ServerTime ValidTo;
    }

    public class AuctionsBucketData
    {
        public AuctionsBucketKey Key;
        public ItemClass ItemClass;
        public int ItemSubClass;
        public InventoryType InventoryType;
        public int RequiredLevel;
        public int SortLevel;
        public long MinPrice;
        public AuctionHouseFilterMask QualityMask;

        // ordered by price, like TC's insertSorter
        public List<AuctionPosting> Auctions = new();

        Dictionary<Locale, string> _lowerNames = new();
        Item _sampleItem;

        public AuctionsBucketData(AuctionsBucketKey key, Item item)
        {
            Key = key;
            _sampleItem = item;

            ItemTemplate itemTemplate = item.GetTemplate();
            ItemClass = itemTemplate.GetClass();
            ItemSubClass = itemTemplate.GetSubClass().data;
            InventoryType = itemTemplate.GetInventoryType();
            RequiredLevel = item.GetRequiredLevel();
            switch (ItemClass)
            {
                case ItemClass.Weapon:
                case ItemClass.Armor:
                    SortLevel = key.ItemLevel;
                    break;
                case ItemClass.Container:
                    SortLevel = itemTemplate.GetContainerSlots();
                    break;
                case ItemClass.Gem:
                case ItemClass.ItemEnhancement:
                    SortLevel = itemTemplate.GetItemLevel();
                    break;
                case ItemClass.Consumable:
                    SortLevel = Math.Max(1, RequiredLevel);
                    break;
                case ItemClass.Miscellaneous:
                case ItemClass.BattlePets:
                    SortLevel = 1;
                    break;
                case ItemClass.Recipe:
                    SortLevel = itemTemplate.GetSubClass().Recipe != ItemSubClassRecipe.Book ? itemTemplate.GetRequiredSkillRank() : itemTemplate.GetBaseRequiredLevel();
                    break;
                default:
                    break;
            }
        }

        public string GetLowerName(Locale locale)
        {
            if (!_lowerNames.TryGetValue(locale, out string name))
            {
                name = (_sampleItem.GetName(locale) ?? string.Empty).ToLowerInvariant();
                _lowerNames[locale] = name;
            }

            return name;
        }

        static long DisplayPrice(AuctionPosting auction)
        {
            return auction.BuyoutPrice != 0 ? auction.BuyoutPrice : (auction.BidAmount != 0 ? auction.BidAmount : auction.MinBid);
        }

        public void Add(AuctionPosting auction)
        {
            long price = DisplayPrice(auction);
            int index = Auctions.FindIndex(other => DisplayPrice(other) > price);
            Auctions.Insert(index < 0 ? Auctions.Count : index, auction);

            if (MinPrice == 0 || price < MinPrice)
                MinPrice = price;

            QualityMask |= (AuctionHouseFilterMask)((uint)AuctionHouseFilterMask.PoorQuality << (int)auction.Item.GetQuality());
        }

        public void Remove(AuctionPosting auction)
        {
            Auctions.Remove(auction);
            if (!Auctions.Empty())
            {
                _sampleItem = Auctions[0].Item;
                MinPrice = Auctions.Min(DisplayPrice);
            }
        }

        public BucketInfo BuildBucketInfo(Player player)
        {
            BucketInfo bucketInfo = new();
            bucketInfo.Key = new AuctionBucketKey(Key);
            bucketInfo.MinPrice = MinPrice;
            bucketInfo.RequiredLevel = RequiredLevel;

            foreach (AuctionPosting auction in Auctions)
            {
                bucketInfo.TotalQuantity += auction.Item.GetCount();
                bucketInfo.ContainsOwnerItem = bucketInfo.ContainsOwnerItem || auction.Owner == player.GetGUID();
            }

            bucketInfo.ContainsOnlyCollectedAppearances = true;
            foreach (int appearanceId in Auctions.Select(auction => auction.Item.GetItemModifiedAppearance()?.Id ?? 0).Where(id => id != 0).Distinct().Take(4))
            {
                bucketInfo.ItemModifiedAppearanceIDs.Add(appearanceId);
                if (!player.GetSession().GetCollectionMgr().HasItemAppearance(appearanceId).PermAppearance)
                    bucketInfo.ContainsOnlyCollectedAppearances = false;
            }

            return bucketInfo;
        }

        public class Sorter : IComparer<AuctionsBucketData>
        {
            Locale _locale;
            IList<AuctionSortDef> _sorts;

            public Sorter(Locale locale, IList<AuctionSortDef> sorts)
            {
                _locale = locale;
                _sorts = sorts;
            }

            public int Compare(AuctionsBucketData left, AuctionsBucketData right)
            {
                foreach (AuctionSortDef sort in _sorts)
                {
                    long ordering = CompareColumns(sort.Order, left, right);
                    if (ordering != 0)
                        return Math.Sign(ordering) * (sort.ReverseSort ? -1 : 1);
                }

                return left.Key.CompareTo(right.Key);
            }

            long CompareColumns(AuctionHouseSortOrder column, AuctionsBucketData left, AuctionsBucketData right)
            {
                switch (column)
                {
                    case AuctionHouseSortOrder.Price:
                    case AuctionHouseSortOrder.Bid:
                    case AuctionHouseSortOrder.Buyout:
                        return left.MinPrice - right.MinPrice;
                    case AuctionHouseSortOrder.Name:
                        return string.CompareOrdinal(left.GetLowerName(_locale), right.GetLowerName(_locale));
                    case AuctionHouseSortOrder.Level:
                        return left.SortLevel - right.SortLevel;
                    default:
                        return 0;
                }
            }
        }
    }

    public class AuctionPosting
    {
        public int Id;
        public ObjectGuid Owner;
        public ObjectGuid OwnerAccount;
        public ObjectGuid Bidder;
        public long MinBid;
        public long BuyoutPrice; // unit price for commodities (auctionhouse.buyoutOrUnitPrice)
        public long Deposit;
        public long BidAmount;
        public ServerTime StartTime;
        public ServerTime EndTime;
        public AuctionPostingServerFlag ServerFlags;
        public Item Item;
        public HashSet<ObjectGuid> BidderHistory;
        public AuctionsBucketData Bucket;

        public AuctionPosting()
        {
            Item = new Item();
            BidderHistory = new();
            StartTime = ServerTime.Zero;
            EndTime = ServerTime.Zero;
        }

        // Stackable items are sold as commodities (TC AuctionPosting::IsCommodity). Commodity postings never carry a bid,
        // so stacks posted with a minimum bid by the old WotLK list protocol stay ordinary auctions.
        public bool IsCommodity()
        {
            return Item.GetTemplate().GetMaxStackSize() > 1 && MinBid == 0;
        }

        public void BuildAuctionItem(AuctionItem auctionItem, bool alwaysSendItem, bool sendKey, bool censorServerInfo, bool censorBidInfo)
        {
            // SMSG_AUCTION_LIST_BIDDER_ITEMS_RESULT, SMSG_AUCTION_LIST_ITEMS_RESULT (if not commodity), SMSG_AUCTION_LIST_OWNER_ITEMS_RESULT, SMSG_AUCTION_REPLICATE_RESPONSE (if not commodity)
            //auctionItem.Item - here to unify comment

            // all (not optional<>)
            auctionItem.Count = Item.GetCount();
            auctionItem.Flags = Item.m_itemData.DynamicFlags;
            auctionItem.AuctionID = Id;
            auctionItem.Owner = Owner;

            // prices set when filled
            if (IsCommodity())
            {
                if (alwaysSendItem)
                    auctionItem.Item = new ItemInstance(Item);

                auctionItem.UnitPrice = BuyoutPrice;
            }
            else
            {
                auctionItem.Item = new ItemInstance(Item);
                auctionItem.Charges = new[] { Item.GetSpellCharges(0), Item.GetSpellCharges(1), Item.GetSpellCharges(2), Item.GetSpellCharges(3), Item.GetSpellCharges(4) }.Max();
                for (EnchantmentSlot enchantmentSlot = 0; enchantmentSlot < EnchantmentSlot.EnhancementMax; enchantmentSlot++)
                {
                    int enchantId = Item.GetEnchantmentId(enchantmentSlot);
                    if (enchantId == 0)
                        continue;

                    auctionItem.Enchantments.Add(new ItemEnchantData(enchantId, Item.GetEnchantmentDuration(enchantmentSlot), Item.GetEnchantmentCharges(enchantmentSlot), (byte)enchantmentSlot));
                }

                for (byte i = 0; i < Item.m_itemData.Gems.Size(); ++i)
                {
                    SocketedGem gemData = Item.m_itemData.Gems[i];
                    if (gemData.ItemId != 0)
                    {
                        ItemGemData gem = new();
                        gem.Slot = i;
                        gem.Item = new ItemInstance(gemData);
                        auctionItem.Gems.Add(gem);
                    }
                }

                if (MinBid != 0)
                    auctionItem.MinBid = MinBid;

                long minIncrement = CalculateMinIncrement();
                if (minIncrement != 0)
                    auctionItem.MinIncrement = minIncrement;

                if (BuyoutPrice != 0)
                    auctionItem.BuyoutPrice = BuyoutPrice;
            }

            // all (not optional<>)
            auctionItem.DurationLeft = Time.Max(EndTime - LoopTime.ServerTime, TimeSpan.Zero);
            auctionItem.DeleteReason = 0;

            // SMSG_AUCTION_LIST_ITEMS_RESULT (only if owned)
            auctionItem.CensorServerSideInfo = censorServerInfo;
            auctionItem.ItemGuid = IsCommodity() ? ObjectGuid.Empty : Item.GetGUID();
            auctionItem.OwnerAccountID = OwnerAccount;
            auctionItem.EndTime = EndTime;

            // SMSG_AUCTION_LIST_BIDDER_ITEMS_RESULT, SMSG_AUCTION_LIST_ITEMS_RESULT (if has bid), SMSG_AUCTION_LIST_OWNER_ITEMS_RESULT, SMSG_AUCTION_REPLICATE_RESPONSE (if has bid)
            auctionItem.CensorBidInfo = censorBidInfo;
            if (!Bidder.IsEmpty())
            {
                auctionItem.Bidder = Bidder;
                auctionItem.BidAmount = BidAmount;
            }

            // SMSG_AUCTION_LIST_BIDDER_ITEMS_RESULT, SMSG_AUCTION_LIST_OWNER_ITEMS_RESULT, SMSG_AUCTION_REPLICATE_RESPONSE (if commodity)
            if (sendKey)
                auctionItem.AuctionBucketKey = new(AuctionsBucketKey.ForItem(Item));

            // all
            if (!Item.m_itemData.Creator._value.IsEmpty())
                auctionItem.Creator = Item.m_itemData.Creator;
        }

        public static long CalculateNextBidAmount(long bidAmount)
        {
            return bidAmount + CalculateMinIncrement(bidAmount);
        }

        public static long CalculateMinIncrement(long bidAmount)
        {
            long increment = MathFunctions.CalculatePct(bidAmount, 5);
            return increment < 1 ? 1 : increment;
        }

        public long CalculateMinIncrement() { return CalculateMinIncrement(BidAmount); }

        public class Sorter : IComparer<AuctionPosting>
        {
            Locale _locale;
            IList<AuctionSortDef> _sorts;

            public Sorter(Locale locale, IList<AuctionSortDef> sorts)
            {
                _locale = locale;
                _sorts = sorts;
            }

            public int Compare(AuctionPosting left, AuctionPosting right)
            {
                foreach (AuctionSortDef sort in _sorts)
                {
                    long ordering = CompareColumns(sort.Order, left, right);
                    if (ordering != 0)
                        return Math.Sign(ordering) * (sort.ReverseSort ? -1 : 1);
                }

                // Auctions are processed in LIFO order
                if (left.StartTime != right.StartTime)
                    return right.StartTime.CompareTo(left.StartTime);

                return right.Id.CompareTo(left.Id);
            }

            long CompareColumns(AuctionHouseSortOrder column, AuctionPosting left, AuctionPosting right)
            {
                switch (column)
                {
                    case AuctionHouseSortOrder.Price:
                    {
                        long leftPrice = left.BuyoutPrice != 0 ? left.BuyoutPrice : (left.BidAmount != 0 ? left.BidAmount : left.MinBid);
                        long rightPrice = right.BuyoutPrice != 0 ? right.BuyoutPrice : (right.BidAmount != 0 ? right.BidAmount : right.MinBid);
                        return leftPrice - rightPrice;
                    }
                    case AuctionHouseSortOrder.Name:
                        return string.Compare(left.Item.GetName(_locale), right.Item.GetName(_locale), StringComparison.OrdinalIgnoreCase);
                    case AuctionHouseSortOrder.Level:
                        return (left.Bucket?.SortLevel ?? 0) - (right.Bucket?.SortLevel ?? 0);
                    case AuctionHouseSortOrder.Bid:
                        return left.BidAmount - right.BidAmount;
                    case AuctionHouseSortOrder.Buyout:
                        return left.BuyoutPrice - right.BuyoutPrice;
                    case AuctionHouseSortOrder.TimeRemaining:
                        return (left.EndTime - right.EndTime).Ticks;
                    default:
                        return 0;
                }
            }
        }
    }

    public class AuctionThrottleResult
    {
        public TimeSpan DelayUntilNext;
        public bool Throttled;

        public AuctionThrottleResult(TimeSpan delayUntilNext, bool throttled)
        {
            DelayUntilNext = delayUntilNext;
            Throttled = throttled;
        }
    }

    public class AuctionsBucketKey : IComparable<AuctionsBucketKey>, IEquatable<AuctionsBucketKey>
    {
        public int ItemId { get; set; }
        public ushort ItemLevel { get; set; }
        public ushort BattlePetSpeciesId { get; set; }
        public ushort SuffixItemNameDescriptionId { get; set; }

        public AuctionsBucketKey(int itemId, int itemLevel, int battlePetSpeciesId, int suffixItemNameDescriptionId)
        {
            ItemId = itemId;
            ItemLevel = (ushort)itemLevel;
            BattlePetSpeciesId = (ushort)battlePetSpeciesId;
            SuffixItemNameDescriptionId = (ushort)suffixItemNameDescriptionId;
        }

        public AuctionsBucketKey(AuctionBucketKey key)
        {
            ItemId = key.ItemID;
            ItemLevel = key.ItemLevel;
            BattlePetSpeciesId = (ushort)(key.BattlePetSpeciesID.HasValue ? key.BattlePetSpeciesID.Value : 0);
            SuffixItemNameDescriptionId = (ushort)(key.SuffixItemNameDescriptionID.HasValue ? key.SuffixItemNameDescriptionID.Value : 0);
        }

        public int CompareTo(AuctionsBucketKey other)
        {
            int result = ItemId.CompareTo(other.ItemId);
            if (result == 0)
                result = ItemLevel.CompareTo(other.ItemLevel);
            if (result == 0)
                result = BattlePetSpeciesId.CompareTo(other.BattlePetSpeciesId);
            if (result == 0)
                result = SuffixItemNameDescriptionId.CompareTo(other.SuffixItemNameDescriptionId);
            return result;
        }

        public bool Equals(AuctionsBucketKey other)
        {
            return other is not null
                && ItemId == other.ItemId
                && ItemLevel == other.ItemLevel
                && BattlePetSpeciesId == other.BattlePetSpeciesId
                && SuffixItemNameDescriptionId == other.SuffixItemNameDescriptionId;
        }

        public static bool operator ==(AuctionsBucketKey right, AuctionsBucketKey left)
        {
            if (right is null)
                return left is null;

            return right.Equals(left);
        }
        public static bool operator !=(AuctionsBucketKey right, AuctionsBucketKey left) { return !(right == left); }

        public override bool Equals(object obj)
        {
            return obj is AuctionsBucketKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ItemId, ItemLevel, BattlePetSpeciesId, SuffixItemNameDescriptionId);
        }

        public static AuctionsBucketKey ForItem(Item item)
        {
            ItemTemplate itemTemplate = item.GetTemplate();
            if (itemTemplate.GetMaxStackSize() == 1)
            {
                return new AuctionsBucketKey(
                    item.GetEntry(),
                    Item.GetItemLevel(itemTemplate, item.GetBonus(), 0, item.GetRequiredLevel(), 0, 0, 0, false),
                    item.GetModifier(ItemModifier.BattlePetSpeciesId),
                    item.GetBonus().Suffix);
            }
            else
                return ForCommodity(itemTemplate);
        }

        public static AuctionsBucketKey ForCommodity(ItemTemplate itemTemplate)
        {
            return new AuctionsBucketKey(itemTemplate.GetId(), (ushort)itemTemplate.GetItemLevel(), 0, 0);
        }
    }

    // TC AuctionSearchClassFilters: a class with no entry is skipped, SkipSubclass keeps every subclass of the class
    public class AuctionSearchClassFilters
    {
        public const uint SkipClass = 0;
        public const uint SkipSubclass = 0xFFFFFFFF;

        public uint[] SubclassMask = new uint[(int)ItemClass.Max];
        public ulong[,] InvTypes = new ulong[(int)ItemClass.Max, ItemConst.MaxItemSubclassTotal];

        public bool Matches(ItemClass itemClass, int itemSubClass, InventoryType inventoryType)
        {
            if ((int)itemClass < 0 || (int)itemClass >= SubclassMask.Length)
                return false;

            uint subclassMask = SubclassMask[(int)itemClass];
            if (subclassMask == SkipClass)
                return false;

            if (subclassMask == SkipSubclass)
                return true;

            if (itemSubClass < 0 || itemSubClass >= 32 || (subclassMask & (1u << itemSubClass)) == 0)
                return false;

            if (itemSubClass >= ItemConst.MaxItemSubclassTotal)
                return true;

            return (InvTypes[(int)itemClass, itemSubClass] & (1ul << (int)inventoryType)) != 0;
        }
    }
}
