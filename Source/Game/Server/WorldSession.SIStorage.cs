using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Networking.Packets;
using Game.Mails;
using System.Linq;
using System.Threading.Tasks;

namespace Game
{
    public partial class WorldSession
    {
        internal bool HasPendingSIAuctionPurchase => _siAuctionPurchasePending;
        volatile bool _siAuctionPurchasePending;

        internal Task<(bool Success, long Spent)> QueueAuctionBuyForSI(Creature auctioneer, int auctionId, int itemId, int quantity, long maximumPrice, long reserve, SITask visit)
        {
            Player owner = _player;
            var controller = owner?.Controller;
            ObjectGuid npc = auctioneer.GetGUID();
            return SIAuctionWork.Enqueue(() =>
            {
                if (owner == null || _player != owner || controller == null || owner.Controller != controller || !owner.IsInWorld || owner.IsBeingTeleported() || owner.IsInCombat())
                    return (false, 0L);
                if (!controller.OwnsAuctionVisit(visit)) return (false, 0L);
                Creature current = owner.GetNPCIfCanInteractWith(npc, NPCFlags1.Auctioneer, NPCFlags2.None);
                if (current == null) return (false, 0L);
                var listing = Global.AuctionHouseMgr.GetAuctionsMap(current.GetFaction()).GetAuction(auctionId);
                if (listing == null || !controller.NeedsAuctionPurchase(listing.Item, quantity)) return (false, 0L);
                try
                {
                    bool success = BuyAuctionForSI(current, auctionId, itemId, quantity, maximumPrice, reserve, out long spent);
                    if (success) controller.AuctionPurchaseAccepted(itemId, quantity, spent);
                    return (success, spent);
                }
                catch { _siAuctionPurchasePending = false; throw; }
            }, (false, 0L));
        }

        internal Task<bool> QueueAuctionPostForSI(Creature auctioneer, Item item, long reserve, SITask visit)
        {
            Player owner = _player;
            var controller = owner?.Controller;
            ObjectGuid npc = auctioneer.GetGUID(), guid = item.GetGUID();
            return SIAuctionWork.Enqueue(() =>
            {
                if (owner == null || _player != owner || controller == null || owner.Controller != controller || !owner.IsInWorld || owner.IsBeingTeleported() || owner.IsInCombat()) return false;
                if (!controller.OwnsAuctionVisit(visit)) return false;
                Creature current = owner.GetNPCIfCanInteractWith(npc, NPCFlags1.Auctioneer, NPCFlags2.None);
                Item stock = owner.GetItemByGuid(guid);
                return current != null && stock != null && controller.WantsAuctionListing(stock) && AuctionItemForSI(current, stock, reserve);
            }, false);
        }

        bool BuyAuctionForSI(Creature auctioneer, int auctionId, int itemId, int quantity, long maximumPrice, long reserve, out long spent)
        {
            spent = 0;
            if (!IsSIControlled || _player == null || auctioneer == null || quantity <= 0 || maximumPrice <= 0 || _siAuctionPurchasePending ||
                _player.GetLevel() < WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32 ||
                Global.AuctionHouseMgr.PendingAuctionCount(_player) != 0 ||
                _player.GetNPCIfCanInteractWith(auctioneer.GetGUID(), NPCFlags1.Auctioneer, NPCFlags2.None) == null ||
                _player.CanStoreNewItem(ItemPos.Undefined, out _, itemId, quantity) != InventoryResult.Ok) return false;
            var house = Global.AuctionHouseMgr.GetAuctionsMap(auctioneer.GetFaction());
            var auction = house.GetAuction(auctionId);
            if (auction == null || auction.EndTime <= LoopTime.ServerTime || auction.Item.GetEntry() != itemId ||
                auction.Owner == _player.GetGUID() || auction.OwnerAccount == GetAccountGUID() || auction.BuyoutPrice <= 0) return false;
            long before = _player.GetMoney();
            maximumPrice = System.Math.Min(maximumPrice, System.Math.Max(0, before - System.Math.Max(0, reserve)));
            if (auction.IsCommodity())
            {
                var quote = house.CreateCommodityQuote(_player, itemId, quantity);
                if (quote == null || quote.TotalPrice <= 0 || quote.TotalPrice > maximumPrice)
                { house.CancelCommodityQuote(_player.GetGUID()); return false; }
                _siAuctionPurchasePending = true;
                ConfirmCommoditiesPurchase(new AuctionConfirmCommoditiesPurchase(null)
                { Auctioneer = auctioneer.GetGUID(), ItemID = itemId, Quantity = quantity }, _ => _siAuctionPurchasePending = false);
                house.CancelCommodityQuote(_player.GetGUID());
            }
            else
            {
                if (auction.Item.GetCount() != quantity || auction.BuyoutPrice > maximumPrice) return false;
                _siAuctionPurchasePending = true;
                PlaceAuctionBid(new AuctionPlaceBid(null)
                { Auctioneer = auctioneer.GetGUID(), AuctionID = auctionId, BidAmount = auction.BuyoutPrice }, _ => _siAuctionPurchasePending = false);
            }
            spent = before - _player.GetMoney();
            if (spent <= 0) _siAuctionPurchasePending = false;
            return spent > 0 && spent <= maximumPrice;
        }

        internal bool CollectAuctionMailForSI(WorldObject mailbox, out int received)
        {
            received = 0;
            if (!IsSIControlled || _player == null || _siAuctionPurchasePending || mailbox == null || !CanOpenMailBox(mailbox.GetGUID())) return false;
            foreach (Mail mail in _player.GetMails().Where(mail => mail.messageType == MailMessageType.Auction &&
                mail.state != MailState.Deleted && mail.deliver_time <= LoopTime.ServerTime && mail.COD == 0).ToArray())
            {
                if (mail.money > 0)
                    HandleMailTakeMoney(new MailTakeMoney(null) { Mailbox = mailbox.GetGUID(), MailID = mail.messageID, Money = mail.money });
                foreach (var attachment in mail.items.ToArray())
                {
                    Item item = _player.GetMItem(attachment.item_guid);
                    if (item == null || _player.CanStoreItem(ItemPos.Undefined, out _, item) != InventoryResult.Ok) continue;
                    int before = _player.GetItemCount(item.GetEntry());
                    HandleMailTakeItem(new MailTakeItem(null) { Mailbox = mailbox.GetGUID(), MailID = mail.messageID, AttachID = attachment.item_guid });
                    received = _player.GetItemCount(item.GetEntry()) - before;
                    // One attachment per tick bounds synchronous native mail saves.
                    return received > 0;
                }
            }
            return true;
        }

        internal bool BankItemForSI(Creature banker, Item item)
        {
            if (!IsSIControlled || _player == null || banker == null || item == null ||
                !item.InventoryPosition.IsInventoryPos || item.IsInTrade() || item.IsNotEmptyBag() ||
                _player.GetNPCIfCanInteractWith(banker.GetGUID(), NPCFlags1.Banker, NPCFlags2.None) == null) return false;
            HandleBankerActivate(new BankerActivate(null) { Banker = banker.GetGUID(), InteractionType = PlayerInteractionType.CharacterBanker });
            if (!CanUseBank(banker.GetGUID()) || _player.CanBankItem(ItemPos.Undefined, out _, item) != InventoryResult.Ok) return false;
            ObjectGuid guid = item.GetGUID();
            int before = _player.GetItemCount(item.GetEntry());
            int quantity = item.GetCount();
            HandleAutoBankItem(new AutoBankItem(null) { Bag = item.InventoryBagSlot, Slot = item.InventorySlot, BankType = 0 });
            // Merging a whole stack can remove its original GUID.
            return _player.GetItemCount(item.GetEntry()) == before - quantity &&
                _player.GetItemByGuid(guid)?.InventoryPosition.IsInventoryPos != true;
        }

        internal bool CanAuctionItemForSI(Item item) => IsSIControlled && _player != null && item != null &&
            item.InventoryPosition.IsInventoryPos && !item.IsInTrade() && CanAuctionItem(item) &&
            _player.GetLevel() >= WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32;

        internal bool CanPostAuctionForSI(int faction, Item item, long repairReserve)
        {
            if (!CanAuctionItemForSI(item) || Global.AuctionHouseMgr.PendingAuctionCount(_player) != 0) return false;
            var house = Global.AuctionHouseMgr.GetAuctionHouseEntry(faction);
            if (house == null) return false;
            long deposit = item.GetTemplate().GetMaxStackSize() > 1
                ? AuctionManager.GetCommodityAuctionDeposit(item.GetTemplate(), SharedConst.MinAuctionTime, item.GetCount(), house.DepositRate)
                : AuctionManager.GetItemAuctionDeposit(_player, item, SharedConst.MinAuctionTime, house.DepositRate);
            return SIInventoryPolicy.CanPayDeposit(_player.GetMoney(), repairReserve, deposit);
        }

        bool AuctionItemForSI(Creature auctioneer, Item item, long repairReserve)
        {
            if (auctioneer == null || !CanPostAuctionForSI(auctioneer.GetFaction(), item, repairReserve) ||
                _player.GetNPCIfCanInteractWith(auctioneer.GetGUID(), NPCFlags1.Auctioneer, NPCFlags2.None) == null) return false;
            ObjectGuid guid = item.GetGUID();
            long price = SIInventoryPolicy.AuctionUnitPrice(item.GetSellPrice(_player));
            var sale = new AuctionItemForSale { Guid = guid, UseCount = item.GetCount() };
            if (item.GetTemplate().GetMaxStackSize() > 1)
            {
                var packet = new AuctionSellCommodity(null) { Auctioneer = auctioneer.GetGUID(), UnitPrice = price, RunTime = SharedConst.MinAuctionTime };
                packet.Items.Add(sale);
                HandleAuctionSellCommodity(packet);
            }
            else
            {
                var packet = new AuctionSellItem(null) { Auctioneer = auctioneer.GetGUID(), BuyoutPrice = price, MinBid = price, RunTime = SharedConst.MinAuctionTime };
                packet.Items.Add(sale);
                HandleAuctionSellItem(packet);
            }
            return Global.AuctionHouseMgr.GetAItem(guid) != null && _player.GetItemByGuid(guid) == null;
        }
    }
}
