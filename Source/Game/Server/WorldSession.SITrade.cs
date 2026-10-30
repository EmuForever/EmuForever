using Framework.Constants;
using Game.Entities;
using Game.Networking.Packets;
using Game.Spells;

namespace Game
{
    // The trade window for SI players, through the same handlers a client's trade packets reach.
    public partial class WorldSession
    {
        // CMSG_INITIATE_TRADE
        internal bool ProposeTradeForSI(Player other)
        {
            if (!IsSIControlled || _player == null || other == null || _player.GetTradeData() != null) return false;
            HandleInitiateTrade(new InitiateTrade(null) { Guid = other.GetGUID() });
            return _player.GetTradeData()?.GetTrader() == other;
        }

        // CMSG_BEGIN_TRADE: opens a trade someone proposed to us.
        internal void BeginTradeForSI()
        {
            if (IsSIControlled && _player?.GetTradeData() != null) HandleBeginTrade(new BeginTrade(null));
        }

        // CMSG_SET_TRADE_ITEM from the item's bag position (equipped items may go in the "will not be traded" slot).
        internal bool PutTradeItemForSI(Item item, TradeSlots slot)
        {
            if (!IsSIControlled || _player?.GetTradeData() == null || item == null) return false;
            HandleSetTradeItem(new SetTradeItem(null) { TradeSlot = (byte)slot, PackSlot = item.InventoryBagSlot, ItemSlotInPack = item.InventorySlot });
            return _player.GetTradeData()?.GetItem(slot) == item;
        }

        // CMSG_SET_TRADE_GOLD
        internal void SetTradeMoneyForSI(long money)
        {
            if (IsSIControlled && _player?.GetTradeData() != null) HandleSetTradeGold(new SetTradeGold(null) { Coinage = money });
        }

        // CMSG_ACCEPT_TRADE against the partner's current offer.
        internal void AcceptTradeForSI()
        {
            TradeData mine = _player?.GetTradeData();
            TradeData his = mine?.GetTrader()?.GetTradeData();
            if (IsSIControlled && his != null) HandleAcceptTrade(new AcceptTrade(null) { StateIndex = (uint)his.GetServerStateIndex() });
        }

        internal void CancelTradeForSI()
        {
            if (IsSIControlled && _player?.GetTradeData() != null) _player.TradeCancel(true);
        }

        // Casts an enchantment on the item the partner put in the "will not be traded" slot; the core applies
        // it when both sides accept.
        internal SpellCastResult CastTradeSpellForSI(int spellId)
        {
            if (!IsSIControlled || _player?.GetTradeData()?.GetTrader()?.GetTradeData()?.GetItem(TradeSlots.NonTraded) == null)
                return SpellCastResult.BadTargets;
            var targets = new SpellCastTargets();
            targets.SetTradeItemTarget(_player);
            return _player.CastSpell(targets, spellId);
        }
    }
}
