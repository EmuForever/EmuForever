using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        long _nextStorageTripSearch;
        readonly Dictionary<ObjectGuid, long> _storageItemRetryAt = new();

        SIItemDisposition ItemDisposition(Item item)
        {
            var template = item.GetTemplate();
            bool protectedItem = !item.InventoryPosition.IsInventoryPos || item.IsInTrade() || item.IsNotEmptyBag() ||
                template.HasFlag(ItemFlags.NoUserDestroy) || template.HasFlag(ItemFlags.Conjured) || item.m_itemData.Expiration != Seconds.Zero ||
                QuestNeedsItem(item.GetEntry()) || template.GetStartQuest() != 0 ||
                _knownSpells.Any(spell => spell.SpellFamilyName != SpellFamilyNames.Generic &&
                    (spell.Reagent.Contains(item.GetEntry()) || spell.Totem.Contains(item.GetEntry()))) ||
                template.GetClass() is ItemClass.Quest or ItemClass.Key or ItemClass.Reagent or ItemClass.Projectile or ItemClass.Consumable;
            if (protectedItem) return SIItemDisposition.Carry;
            bool upgrade = SlotsFor(template.GetInventoryType()).Length > 0 &&
                me.CanUseItem(template, skipRequiredLevelCheck: true) == InventoryResult.Ok && ItemScore(template) > EquippedScore(template) + 1;
            bool currentUpgrade = upgrade && me.CanUseItem(template) == InventoryResult.Ok &&
                me.CanEquipItem(ItemSlot.Null, out ItemPos _, item, false) == InventoryResult.Ok &&
                !(template.GetInventoryType() == InventoryType.Weapon2Hand &&
                  me.GetItemByPos(EquipmentSlot.OffHand)?.GetTemplate().GetInventoryType() == InventoryType.Shield);
            // Spare bags and bound gear are kept, including alternate weapon sets.
            bool keep = item.IsBag() || item.IsSoulBound() || item.IsBoundAccountWide();
            return SIInventoryPolicy.Disposition(template.GetClass(), item.GetQuality(), false, currentUpgrade,
                upgrade, keep, me.GetSession().CanAuctionItemForSI(item));
        }

        List<Item> StorageItems(SIVendorPurpose purpose, int? faction = null)
        {
            var result = new List<Item>();
            foreach (ObjectGuid guid in _storageItemRetryAt.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                _storageItemRetryAt.Remove(guid);
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                if (_storageItemRetryAt.GetValueOrDefault(item.GetGUID()) > Now) return true;
                SIItemDisposition disposition = ItemDisposition(item);
                if (purpose == SIVendorPurpose.Bank && disposition == SIItemDisposition.Bank &&
                    (me.GetCharacterBankTabCount() == 0 || me.CanBankItem(ItemPos.Undefined, out _, item) == InventoryResult.Ok) ||
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
            if (bankItems.Count == 0 && auctionItems.Count == 0) return false;
            bool Eligible(SIVendorPurpose service, int faction) => service == SIVendorPurpose.Bank ? bankItems.Count > 0 :
                auctionItems.Any(item => me.GetSession().CanPostAuctionForSI(faction, item, RequiredRepairCost()));
            foreach (Creature npc in NearbyCreatures(20).Where(npc => npc.IsAlive() && !me.IsHostileTo(npc) && !npc.IsHostileTo(me) &&
                me.GetExactDist(npc) <= 20 && me.CanSeeOrDetect(npc, distanceCheck: true)).OrderBy(me.GetExactDist))
            {
                foreach (SIVendorPurpose service in new[] { SIVendorPurpose.Bank, SIVendorPurpose.Auction })
                {
                    NPCFlags1 flag = service == SIVendorPurpose.Bank ? NPCFlags1.Banker : NPCFlags1.Auctioneer;
                    if (!npc.HasNpcFlag(flag) || !VendorAvailable(npc, service) || !Eligible(service, npc.GetFaction())) continue;
                    purpose = service; merchant = npc; destination = SpawnOf(npc); return true;
                }
            }
            if (!SIInventoryPolicy.ShouldVisit(me.GetFreeInventorySlotCount(), false, bankItems.Count > 0 || auctionItems.Count > 0) ||
                !SIQuestKnowledge.IsBuilt || Now < _nextStorageTripSearch) return false;
            _nextStorageTripSearch = Now + 15000;
            var choices = new List<(SIVendorPurpose Purpose, SISpawn Spawn)>();
            foreach (SIVendorPurpose service in new[] { SIVendorPurpose.Bank, SIVendorPurpose.Auction })
            {
                if (service == SIVendorPurpose.Bank && bankItems.Count == 0 || service == SIVendorPurpose.Auction && auctionItems.Count == 0) continue;
                int[] entries = service == SIVendorPurpose.Bank ? Knowledge.Bankers : Knowledge.Auctioneers;
                var spawn = Knowledge.Spawns(entries, Array.Empty<int>(), me.GetMapId())
                    .Where(s => SpawnAvailable(s) && FriendlyTemplate(s.Entry) && !IsLearnedRouteBlocked(P(s.Position)) &&
                        !Blacklisted(new SITask { Kind = SITaskKind.Vendor, VendorPurpose = service, Destination = s }.Key))
                    .OrderBy(s => Distance(s.Position))
                    .Where(s => Eligible(service, Global.ObjectMgr.GetCreatureTemplate(s.Entry).Faction))
                    .Select(s => (SISpawn?)s).FirstOrDefault();
                if (spawn.HasValue) choices.Add((service, spawn.Value));
            }
            if (choices.Count == 0) return false;
            var choice = choices.OrderBy(c => Distance(c.Spawn.Position)).First();
            purpose = choice.Purpose; destination = choice.Spawn; return true;
        }

        bool StoreAt(Creature npc, SIVendorPurpose purpose)
        {
            NPCFlags1 flag = purpose == SIVendorPurpose.Bank ? NPCFlags1.Banker : NPCFlags1.Auctioneer;
            if (npc == null || me.GetNPCIfCanInteractWith(npc.GetGUID(), flag, NPCFlags2.None) == null) return false;
            me.SetSelection(npc.GetGUID());
            if (!me.HasUnitState(UnitState.CannotTurn)) me.SetFacingToObject(npc);
            var items = StorageItems(purpose, npc.GetFaction());
            int moved = 0;
            // One auction at a time lets the core settle its pending deposit before the next.
            foreach (Item item in items.Take(purpose == SIVendorPurpose.Bank ? 16 : 1))
            {
                int entry = item.GetEntry();
                if (purpose == SIVendorPurpose.Bank ? me.GetSession().BankItemForSI(npc, item) :
                    me.GetSession().AuctionItemForSI(npc, item, RequiredRepairCost()))
                {
                    ++moved;
                    Report(purpose == SIVendorPurpose.Bank ? "bank_deposit" : "auction_post", $"Stored/listed item {entry} at {npc.GetName()}");
                }
                else _storageItemRetryAt[item.GetGUID()] = Now + 60000;
            }
            if (moved > 0) NoteGameplayProgress();
            Status = purpose == SIVendorPurpose.Bank ? $"Banked {moved} item stack(s) at {npc.GetName()}" :
                $"Listed {moved} item(s) at {npc.GetName()}";
            // No eligible items (full bank, spent deposits) finishes the visit and resumes
            // the saved task. Ineligible stock must not pin the bot at the NPC.
            return true;
        }
    }
}
