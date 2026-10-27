using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Networking.Packets;

namespace Game
{
    public partial class WorldSession
    {
        internal bool BankItemForSI(Creature banker, Item item)
        {
            if (!IsServerControlled || _player == null || banker == null || item == null ||
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

        internal bool CanAuctionItemForSI(Item item) => IsServerControlled && _player != null && item != null &&
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

        internal bool AuctionItemForSI(Creature auctioneer, Item item, long repairReserve)
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
