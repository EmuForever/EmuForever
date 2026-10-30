using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        internal bool WantsAuctionListing(Item item) => ItemDisposition(item) == SIItemDisposition.Auction;
        long _nextStorageTripSearch;
        readonly Dictionary<ObjectGuid, long> _storageItemRetryAt = new();
        int StorageReserveSlots=>Math.Max(3,_settings.MinFreeBagSlots);
        bool StoragePressure()=>me.GetFreeInventorySlotCount()<=StorageReserveSlots;

        SIItemDisposition ItemDisposition(Item item)
        {
            var template = item.GetTemplate();
            bool protectedItem = !item.InventoryPosition.IsInventoryPos || item.IsInTrade() || item.IsNotEmptyBag() ||
                template.HasFlag(ItemFlags.NoUserDestroy) || template.HasFlag(ItemFlags.Conjured) || item.m_itemData.Expiration != Seconds.Zero ||
                QuestNeedsItem(item.GetEntry()) || template.GetStartQuest() != 0 ||
                template.GetTotemCategory()!=0 || _professionGatherTargets.ContainsKey(item.GetEntry()) || _professionSupplies.ContainsKey(item.GetEntry()) ||
                _professionWithdraw.ContainsKey(item.GetEntry()) || _professionBatchRoot?.Spell.Reagent.Contains(item.GetEntry())==true ||
                _professionRecipe?.Spell.Reagent.Contains(item.GetEntry())==true ||
                ProfessionMaterialTargets().ContainsKey(item.GetEntry()) || IsBandageCloth(item.GetEntry()) ||
                _knownSpells.Any(spell => spell.SpellFamilyName != SpellFamilyNames.Generic &&
                    (spell.Reagent.Contains(item.GetEntry()) || spell.Totem.Contains(item.GetEntry()))) ||
                template.GetClass() is ItemClass.Quest or ItemClass.Key or ItemClass.Reagent or ItemClass.Projectile or ItemClass.Consumable;
            if (protectedItem) return SIItemDisposition.Carry;
            if (KeepsTradeGoods && template.GetClass() is ItemClass.TradeGoods or ItemClass.Recipe or ItemClass.Gem &&
                item.GetQuality() >= ItemQuality.Normal) return SIItemDisposition.Bank;
            bool upgrade = SlotsFor(template.GetInventoryType()).Length > 0 &&
                me.CanUseItem(template, skipRequiredLevelCheck: true) == InventoryResult.Ok && ItemScore(template) > EquippedScore(template) + SIItemValue.UpgradeMargin;
            bool currentUpgrade = upgrade && me.CanUseItem(template) == InventoryResult.Ok &&
                me.CanEquipItem(ItemSlot.Null, out ItemPos _, item, true) == InventoryResult.Ok &&
                !(template.GetInventoryType() == InventoryType.Weapon2Hand &&
                  me.GetItemByPos(EquipmentSlot.OffHand)?.GetTemplate().GetInventoryType() == InventoryType.Shield);
            // Spare bags and bound gear are kept, including alternate weapon sets.
            bool keep = item.IsBag() || item.IsSoulBound() || item.IsBoundAccountWide();
            var disposition = SIInventoryPolicy.Disposition(template.GetClass(), item.GetQuality(), false, currentUpgrade,
                upgrade, keep, me.GetSession().CanAuctionItemForSI(item));
            // Surplus is never worth a trip across the map: with no auction house nearby a merchant buys it.
            return disposition == SIItemDisposition.Auction && !AuctionHouseNearby() ? SIItemDisposition.Vendor : disposition;
        }

        // Bank and auction trips stay within the bot's own region; a full bag never sends it across the map
        // (to a city behind water, a boat that is not running, or a zone it has no business in).
        float StorageTripRadius => Math.Max(QuestPickupRadius, 800);
        long _nextAuctionHouseCheck;
        bool _auctionHouseNearby;
        bool AuctionHouseNearby()
        {
            if (Now < _nextAuctionHouseCheck) return _auctionHouseNearby;
            _nextAuctionHouseCheck = Now + 30000;
            return _auctionHouseNearby = SIQuestKnowledge.IsBuilt &&
                NearestServiceSpawn(Knowledge.Auctioneers, SITaskKind.Vendor, SIVendorPurpose.Auction) is SISpawn spawn &&
                Distance(spawn.Position) <= StorageTripRadius;
        }

        List<Item> StorageItems(SIVendorPurpose purpose, int? faction = null)
        {
            var result = new List<Item>();
            bool canExpand = purpose == SIVendorPurpose.Bank && CanExpandBank();
            PruneExpired(_storageItemRetryAt);
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                if (_storageItemRetryAt.GetValueOrDefault(item.GetGUID()) > Now) return true;
                SIItemDisposition disposition = ItemDisposition(item);
                if (purpose == SIVendorPurpose.Bank && disposition == SIItemDisposition.Bank &&
                    (me.GetCharacterBankTabCount() == 0 || me.CanBankItem(ItemPos.Undefined, out _, item) == InventoryResult.Ok || canExpand) ||
                    purpose == SIVendorPurpose.Auction && disposition == SIItemDisposition.Auction &&
                    (!faction.HasValue || me.GetSession().CanPostAuctionForSI(faction.Value, item, RequiredRepairCost()))) result.Add(item);
                return true;
            });
            return result;
        }

        // Local services are opportunistic; a full inventory permits a trip to a known
        // friendly service on this map. Failed routes share normal NPC retry/backoff.
        bool PlanStorageVisit(out SIVendorPurpose purpose, out Creature merchant, out SISpawn? destination)
        {
            purpose = SIVendorPurpose.Bank; merchant = null; destination = null;
            var bankItems = StorageItems(SIVendorPurpose.Bank);
            var auctionItems = StorageItems(SIVendorPurpose.Auction);
            bool auctionPriority=StoragePressure()&&auctionItems.Count>0;
            bool shopping = Now >= _nextShoppingScan && me.GetFreeInventorySlotCount() > 0 && ShoppingAllowance() > 0 || _auctionPurchase != null;
            if (bankItems.Count == 0 && auctionItems.Count == 0 && !shopping) return false;
            bool Eligible(SIVendorPurpose service, int faction) => service == SIVendorPurpose.Bank ? bankItems.Count > 0 :
                auctionItems.Any(item => me.GetSession().CanPostAuctionForSI(faction, item, RequiredRepairCost()));
            foreach (Creature npc in NearbyCreatures(20).Where(npc => npc.IsAlive() && !me.IsHostileTo(npc) && !npc.IsHostileTo(me) &&
                me.GetExactDist(npc) <= 20 && me.CanSeeOrDetect(npc, distanceCheck: true)).OrderBy(me.GetExactDist))
            {
                foreach (SIVendorPurpose service in new[] { SIVendorPurpose.Bank, SIVendorPurpose.Auction })
                {
                    NPCFlags1 flag = service == SIVendorPurpose.Bank ? NPCFlags1.Banker : NPCFlags1.Auctioneer;
                    if (auctionPriority&&service==SIVendorPurpose.Bank || !npc.HasNpcFlag(flag) || !VendorAvailable(npc, service) ||
                        !(Eligible(service, npc.GetFaction()) || service == SIVendorPurpose.Auction && CanShopAt(npc.GetFaction()))) continue;
                    purpose = service; merchant = npc; destination = SpawnOf(npc); return true;
                }
            }
            if (!SIInventoryPolicy.ShouldVisit(me.GetFreeInventorySlotCount(), false, bankItems.Count > 0 || auctionItems.Count > 0,StorageReserveSlots) ||
                !SIQuestKnowledge.IsBuilt || Now < _nextStorageTripSearch) return false;
            _nextStorageTripSearch = Now + 15000;
            var choices = new List<(SIVendorPurpose Purpose, SISpawn Spawn)>();
            foreach (SIVendorPurpose service in new[] { SIVendorPurpose.Bank, SIVendorPurpose.Auction })
            {
                if (service == SIVendorPurpose.Bank && bankItems.Count == 0 || service == SIVendorPurpose.Auction && auctionItems.Count == 0) continue;
                int[] entries = service == SIVendorPurpose.Bank ? Knowledge.Bankers : Knowledge.Auctioneers;
                var spawn = ServiceSpawns(entries, SITaskKind.Vendor, service)
                    .TakeWhile(s => Distance(s.Position) <= StorageTripRadius)
                    .Where(s => Eligible(service, Global.ObjectMgr.GetCreatureTemplate(s.Entry).Faction))
                    .Select(s => (SISpawn?)s).FirstOrDefault();
                if (spawn.HasValue) choices.Add((service, spawn.Value));
            }
            if (choices.Count == 0) return false;
            var choice = choices.OrderBy(c=>auctionPriority&&c.Purpose==SIVendorPurpose.Auction?0:1)
                .ThenBy(c => System.Numerics.Vector3.DistanceSquared(Here, c.Spawn.Position)).First();
            purpose = choice.Purpose; destination = choice.Spawn; return true;
        }

        bool BankAt(Creature npc)
        {
            if (npc == null || me.GetNPCIfCanInteractWith(npc.GetGUID(), NPCFlags1.Banker, NPCFlags2.None) == null) return false;
            FaceTarget(npc);
            var items = StorageItems(SIVendorPurpose.Bank);
            // A full bank first buys its next bag slot and puts a spare bag in it, so that bag is not banked as an item.
            if (items.Any(item => !item.IsBag() && me.CanBankItem(ItemPos.Undefined, out _, item) != InventoryResult.Ok) && CanExpandBank())
            {
                ExpandBank(npc);
                items = StorageItems(SIVendorPurpose.Bank);
            }
            int moved = 0;
            foreach (Item item in items.Take(16))
            {
                int entry = item.GetEntry();
                if (me.GetSession().BankItemForSI(npc, item))
                {
                    ++moved;
                    Report("bank_deposit", $"Banked item {entry} at {npc.GetName()}");
                }
                else _storageItemRetryAt[item.GetGUID()] = Now + 60000;
            }
            bool collecting=_professionWithdraw.Count>0;
            int withdrawn = WithdrawProfessionMaterials(npc);
            if (moved > 0 || withdrawn > 0) NoteGameplayProgress();
            Status = withdrawn > 0 ? $"Banked {moved} and took out {withdrawn} item stack(s) at {npc.GetName()}" : $"Banked {moved} item stack(s) at {npc.GetName()}";
            if(collecting&&_bankWithdrawRetryAt>Now)Status=$"Bank withdrawal blocked at {npc.GetName()}; make bag space before retrying in one minute";
            // No eligible items (full bank) finishes the visit and resumes
            // the saved task. Ineligible stock must not pin the bot at the NPC.
            return true;
        }
    }
}
