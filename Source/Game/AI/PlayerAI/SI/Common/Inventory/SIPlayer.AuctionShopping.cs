using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        sealed record AuctionPurchase(int Faction, int Auction, int Item, int Quantity, long Price, float Priority);
        readonly SIAuctionShoppingBudget _shoppingBudget = new();
        readonly Dictionary<int, int> _professionMaterialTargets = new();
        long _nextMaterialPlan, _nextShoppingScan;
        ulong _materialSpellRevision = ulong.MaxValue;
        AuctionPurchase _auctionPurchase;
        bool _shoppingMaterialsOnly;
        int _shoppingFaction = -1;

        Dictionary<int, int> ProfessionMaterialTargets()
        {
            if (Now < _nextMaterialPlan && _materialSpellRevision == me.SpellBook.Revision) return _professionMaterialTargets;
            _nextMaterialPlan = Now + 30000; _materialSpellRevision = me.SpellBook.Revision;
            _professionMaterialTargets.Clear();
            var recipes = me.GetSpellMap().Where(pair => !pair.Value.Disabled && pair.Value.State != PlayerSpellState.Removed)
                .Select(pair => Global.SpellMgr.GetSpellInfo(pair.Key, me.GetMap().GetDifficultyID()))
                .Where(spell => spell != null && spell.Reagent.Any(item => item > 0))
                .SelectMany(spell => Global.SpellMgr.GetSkillLineAbilityMapBounds(spell.Id)
                    .Where(ability => Global.SpellMgr.IsProfessionSkill(ability.SkillLine) &&
                        SIAuctionShoppingPolicy.RecipeUseful(me.GetSkillValue(ability.SkillLine), ability.MinSkillLineRank, ability.TrivialSkillLineRankHigh))
                    .Select(ability => (Spell: spell, Ability: ability)))
                .GroupBy(recipe => recipe.Ability.SkillLine)
                .SelectMany(group => group.OrderByDescending(recipe => recipe.Ability.MinSkillLineRank).ThenBy(recipe => recipe.Spell.Id).Take(2));
            foreach (var recipe in recipes)
                for (int i = 0; i < recipe.Spell.Reagent.Length; ++i)
                {
                    int item = recipe.Spell.Reagent[i], target = SIAuctionShoppingPolicy.MaterialTarget(recipe.Spell.ReagentCount[i]);
                    if (item > 0 && target > 0) _professionMaterialTargets[item] = Math.Max(_professionMaterialTargets.GetValueOrDefault(item), target);
                }
            // Materials the professions and social goals decided to buy, until owned or half an hour old.
            foreach (var (item, target) in _professionShopping.ToArray())
            {
                if (me.GetItemCount(item, true) >= target || _auctionTriedAt.GetValueOrDefault(item) + 1800000 < Now) { _professionShopping.Remove(item); continue; }
                _professionMaterialTargets[item] = Math.Max(_professionMaterialTargets.GetValueOrDefault(item), target);
            }
            return _professionMaterialTargets;
        }

        int PendingAuctionItemCount(int item) => me.GetMails().Where(mail => mail.messageType == MailMessageType.Auction &&
                mail.state != MailState.Deleted && mail.COD == 0)
            .SelectMany(mail => mail.items).Where(attachment => attachment.item_template == item)
            .Sum(attachment => me.GetMItem(attachment.item_guid)?.GetCount() ?? 0);

        long ShoppingReserve() => RequiredRepairCost() + _trainingEntries.Select(MinimumTrainingCost)
            .Where(cost => cost >= 0 && cost != long.MaxValue).DefaultIfEmpty(0).Min();

        long ShoppingAllowance() => _shoppingBudget.Remaining(Now, me.GetMoney(), ShoppingReserve());

        float ShoppingUpgradeGain(Item item)
        {
            var template = item.GetTemplate();
            byte[] slots = SlotsFor(template.GetInventoryType());
            if (slots.Length == 0 || item.GetQuality() < ItemQuality.Normal || me.CanUseItem(template) != InventoryResult.Ok ||
                me.CanEquipItem(ItemSlot.Null, out ItemPos _, item, true) != InventoryResult.Ok ||
                template.GetInventoryType() == InventoryType.Weapon2Hand &&
                me.GetItemByPos(EquipmentSlot.OffHand)?.GetTemplate().GetInventoryType() == InventoryType.Shield) return 0;
            float ownedScore = EquippedScore(template);
            void Consider(Item owned)
            {
                if (owned.GetTemplate().GetInventoryType() == template.GetInventoryType() && me.CanUseItem(owned.GetTemplate()) == InventoryResult.Ok)
                    ownedScore = Math.Max(ownedScore, ItemScore(owned.GetTemplate()));
            }
            me.ForEachItem(ItemSearchLocation.Inventory | ItemSearchLocation.Bank, owned => { Consider(owned); return true; });
            foreach (var mail in me.GetMails().Where(mail => mail.messageType == MailMessageType.Auction && mail.state != MailState.Deleted && mail.COD == 0))
                foreach (var attachment in mail.items)
                    if (me.GetMItem(attachment.item_guid) is Item owned) Consider(owned);
            return Math.Max(0, ItemScore(template) - ownedScore - SIItemValue.UpgradeMargin);
        }

        bool CanShopAt(int faction) => ScanAuctionPurchases(faction, false, false);

        bool ScanAuctionPurchases(int faction, bool materialsOnly, bool force)
        {
            if (me.GetSession().HasPendingSIAuctionPurchase || me.GetFreeInventorySlotCount() == 0 || me.GetLevel() < WorldConfig.Values[WorldCfg.AuctionLevelReq].Int32 ||
                Global.AuctionHouseMgr.PendingAuctionCount(me) != 0 || ShoppingAllowance() <= 0) return false;
            if (!force && Now < _nextShoppingScan && _shoppingFaction == faction && _shoppingMaterialsOnly == materialsOnly)
                return _auctionPurchase?.Faction == faction;
            _shoppingFaction = faction; _shoppingMaterialsOnly = materialsOnly;
            _nextShoppingScan = Now + 60000; _auctionPurchase = null;
            var house = Global.AuctionHouseMgr.GetAuctionsMap(faction);
            long budget = ShoppingAllowance();
            var materialTargets = ProfessionMaterialTargets();
            foreach (var auction in house.GetBuyoutAuctionsFor(me))
            {
                Item item = auction.Item;
                var template = item.GetTemplate();
                if (materialsOnly && !_professionShopping.ContainsKey(item.GetEntry())) continue;
                int quantity = 1;
                float gain = materialsOnly ? 0 : ShoppingUpgradeGain(item);
                bool gear = gain > 0;
                if (!gear)
                {
                    if (!materialTargets.TryGetValue(item.GetEntry(), out int target)) continue;
                    int need = target - me.GetItemCount(item.GetEntry(), true) - PendingAuctionItemCount(item.GetEntry());
                    if (need <= 0) continue;
                    quantity = Math.Min(need, item.GetCount());
                    if (!auction.IsCommodity() && quantity != item.GetCount()) continue;
                }
                long unitPrice = auction.IsCommodity() ? auction.BuyoutPrice : auction.BuyoutPrice / Math.Max(1, item.GetCount());
                long unitLimit = SIAuctionShoppingPolicy.UnitPriceLimit(template.GetSellPrice(), template.GetBuyPrice(), gear);
                if (!SIAuctionShoppingPolicy.PurchaseAllowed(unitPrice, quantity, unitLimit, budget)) continue;
                long price = auction.IsCommodity() ? unitPrice * quantity : auction.BuyoutPrice;
                if (price > budget || (decimal)price > (decimal)unitLimit * quantity ||
                    me.CanStoreNewItem(ItemPos.Undefined, out _, item.GetEntry(), quantity) != InventoryResult.Ok) continue;
                float priority = gear ? 1000 + gain / (1 + price / 10000f) : 1f / Math.Max(1, price);
                if (_auctionPurchase == null || priority > _auctionPurchase.Priority ||
                    priority == _auctionPurchase.Priority && price < _auctionPurchase.Price)
                    _auctionPurchase = new(faction, auction.Id, item.GetEntry(), quantity, price, priority);
            }
            if (_auctionPurchase != null) _nextShoppingScan = Now + 10000;
            return _auctionPurchase != null;
        }

        internal bool NeedsAuctionPurchase(Item item, int quantity) => item != null && quantity > 0 &&
            (ShoppingUpgradeGain(item) > 0 || ProfessionMaterialTargets().TryGetValue(item.GetEntry(), out int target) &&
                target - me.GetItemCount(item.GetEntry(), true) - PendingAuctionItemCount(item.GetEntry()) >= quantity);

        // Called by the world queue while map workers are stopped. Account for the
        // accepted spend immediately, even if a command replaces the visit next tick.
        internal void AuctionPurchaseAccepted(int item, int quantity, long spent)
        {
            _shoppingBudget.Record(spent);
            _awaitingPurchasedMail = true; _nextAuctionMailScan = 0;
            MarkPartyErrand(); NoteGameplayProgress();
            Status = $"Bought {quantity}x {ItemName(item)}; collecting auction mail";
        }

        AuctionPurchase NeededAuctionPurchase(Creature auctioneer, bool force = false)
        {
            bool materialsOnly = _professionShopping.Count > 0 && _goal is "professions" or "social";
            if (!ScanAuctionPurchases(auctioneer.GetFaction(), materialsOnly, force)) return null;
            var purchase = _auctionPurchase;
            _auctionPurchase = null; _nextShoppingScan = Now + 10000;
            var listing = Global.AuctionHouseMgr.GetAuctionsMap(auctioneer.GetFaction()).GetAuction(purchase.Auction);
            return listing != null && listing.Item.GetEntry() == purchase.Item && NeedsAuctionPurchase(listing.Item, purchase.Quantity) ? purchase : null;
        }
    }
}
