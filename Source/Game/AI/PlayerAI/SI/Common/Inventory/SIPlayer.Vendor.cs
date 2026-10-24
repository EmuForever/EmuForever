using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    // Character upkeep between quests (HB LevelBot vendor/trainer behaviours): sell junk, repair, buy food and
    // drink, buy quest items, learn class spells, equip upgrades and start quests from looted items. Every
    // action goes through the normal vendor/trainer/equip validation.
    public sealed partial class SIPlayer
    {
        long _nextVendorScan, _nextEquipCheck, _nextStartItemCheck;
        SITask _suspendedTask;
        readonly Dictionary<ObjectGuid, long> _vendorRetryAt = new();

        bool QuestNeedsItem(int item)
        {
            for (ushort slot = 0; slot < SharedConst.MaxQuestLogSize; ++slot)
            {
                var quest = Global.ObjectMgr.GetQuestTemplate(me.GetQuestSlotQuestId(slot));
                if (quest == null || me.GetQuestStatus(quest.Id) == QuestStatus.None) continue;
                if (quest.Objectives.Any(objective => objective.Type == QuestObjectiveType.Item && objective.ObjectID == item)) return true;
                if (quest.SourceItemId == item) return true;
            }
            return false;
        }

        bool Junk(Item item) => item.GetQuality() == ItemQuality.Poor && item.GetTemplate().GetSellPrice() > 0 &&
            !QuestNeedsItem(item.GetEntry()) && item.GetTemplate().GetStartQuest() == 0 &&
            !Provision(item.GetTemplate(), false) && !Provision(item.GetTemplate(), true);

        bool HasJunk()
        {
            bool found = false;
            me.ForEachItem(ItemSearchLocation.Inventory, item => { found = Junk(item); return !found; });
            return found;
        }

        bool NeedsRepair()
        {
            for (byte slot = EquipmentSlot.Start; slot < EquipmentSlot.End; ++slot)
            {
                Item item = me.GetItemByPos(slot);
                if (item != null && item.m_itemData.MaxDurability > 0 && SIVendorPolicy.RepairNeeded(item.m_itemData.Durability,item.m_itemData.MaxDurability,_settings.MinDurability)) return true;
            }
            return false;
        }

        bool AllEquipmentRed()
        {
            int repairable = 0, broken = 0;
            for (byte slot = EquipmentSlot.Start; slot < EquipmentSlot.End; ++slot)
            {
                Item item = me.GetItemByPos(slot);
                if (item == null || item.m_itemData.MaxDurability == 0) continue;
                ++repairable; if (item.IsBroken()) ++broken;
            }
            return SIVendorPolicy.ShouldLeaveForRepair(broken, repairable);
        }

        long RepairCost(Creature vendor = null)
        {
            long cost = 0;
            me.ForEachItem(ItemSearchLocation.Inventory | ItemSearchLocation.Equipment, item =>
            { cost += item.CalculateDurabilityRepairCost(vendor == null ? 1 : me.GetReputationPriceDiscount(vendor)); return true; });
            return cost;
        }
        long RequiredRepairCost(Creature vendor = null)
        {
            Item weapon = me.GetItemByPos(EquipmentSlot.MainHand);
            return weapon?.IsBroken() == true
                ? weapon.CalculateDurabilityRepairCost(vendor == null ? 1 : me.GetReputationPriceDiscount(vendor)) : RepairCost(vendor);
        }
        bool CanAffordRepair(Creature vendor = null) => me.GetMoney() >= RequiredRepairCost(vendor);
        bool FundingRepair() => SIVendorPolicy.ShouldFundRepair(NeedsRepair(), RequiredRepairCost(), me.GetMoney(), HasJunk());
        bool BrokenMainHand() => me.GetItemByPos(EquipmentSlot.MainHand)?.IsBroken() == true;
        int TotalInventorySlots()
        {
            int slots = me.GetInventorySlotCount();
            for (byte bagSlot = InventorySlots.BagStart; bagSlot < InventorySlots.BagEnd; ++bagSlot)
                slots += me.GetBagByPos(bagSlot)?.GetBagSize() ?? 0;
            return slots;
        }
        bool BagPressure()=>SIVendorPolicy.BagPressure(me.GetFreeInventorySlotCount(),_settings.MinFreeBagSlots,HasJunk());
        bool NeedsVendor() => NeedsRepair() && (CanAffordRepair() || HasJunk()) || BagPressure();
        bool NeedsRestock() => me.GetFreeInventorySlotCount() != 0 && (_settings.FoodAmount > 0 && SupplyCount(false) == 0 ||
            me.GetMaxPower(PowerType.Mana) > 0 && _settings.DrinkAmount > 0 && SupplyCount(true) == 0);

        bool VendorAt(Creature vendor, SIVendorPurpose purpose)
        {
            if (vendor == null) return false;
            me.SetSelection(vendor.GetGUID());
            if (!me.HasUnitState(UnitState.CannotTurn)) me.SetFacingToObject(vendor);
            if (me.GetNPCIfCanInteractWith(vendor.GetGUID(), NPCFlags1.Vendor | NPCFlags1.Repair, NPCFlags2.None) == null)
            {
                Status = me.GetSession().DescribeSIInteractionFailure(vendor);
                _vendorRetryAt[vendor.GetGUID()] = Now + 10000;
                return false;
            }
            // Every usable merchant visit sells protected-filtered junk first,
            // including repair, food/drink and ammunition visits.
            bool hadJunk=HasJunk();
            int sold=hadJunk&&vendor.HasNpcFlag(NPCFlags1.Vendor)&&
                !vendor.GetCreatureTemplate().FlagsExtra.HasFlag(CreatureFlagsExtra.NoSellVendor)?SellJunk(vendor):0;
            if(hadJunk&&HasJunk()&&purpose==SIVendorPurpose.Sell)
            {
                Status=$"Failed to sell remaining junk to {vendor.GetName()}";
                _vendorRetryAt[vendor.GetGUID()]=Now+2000;return false;
            }
            if(purpose==SIVendorPurpose.Ammunition)return BuyAmmunition(vendor);
            if(purpose==SIVendorPurpose.Restock)return BuyProvisions(vendor);
            bool repaired = false;
            // The reference repairs during a sale too when the same merchant supports it.
            if (vendor.HasNpcFlag(NPCFlags1.Repair) && RepairCost(vendor) > 0 && CanAffordRepair(vendor))
            {
                repaired = me.GetSession().RepairForSI(vendor);
                if (repaired) Report("vendor_repair", "Repaired equipment");
            }
            if (purpose == SIVendorPurpose.Repair && RepairCost(vendor) > 0 && (!repaired || CanAffordRepair(vendor)))
            {
                Status = CanAffordRepair(vendor) ? "Repair was not applied; retrying" : "Cannot afford the current repair cost";
                return false;
            }
            Status = sold > 0 ? $"Sold {sold} junk item(s) to {vendor.GetName()}" : repaired ? $"Repaired equipment at {vendor.GetName()}" : "Merchant work completed";
            return true;
        }

        int SellJunk(Creature vendor)
        {
            var junk = new List<Item>();
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (Junk(item)) junk.Add(item); return true; });
            int sold = 0;
            foreach (Item item in junk)
            {
                int entry = item.GetEntry();
                if (me.GetSession().SellItemForSI(vendor, item))
                {
                    ++sold;
                    Report("vendor_sale", $"Sold junk item {entry}");
                }
            }
            if (sold > 0) NoteGameplayProgress();
            return sold;
        }

        // Reference Vendors.BuyItems: choose the best usable food/drink and verify real transfers.
        bool BuyProvisions(Creature vendor)
        {
            bool success = true;
            bool manaUser = me.GetMaxPower(PowerType.Mana) > 0;
            foreach (bool drink in manaUser ? new[] { false, true } : new[] { false })
            {
                var purchase = ProvisionPurchase(vendor, drink);
                if (purchase.Item == null || purchase.Quantity == 0) continue;
                int before = me.GetItemCount(purchase.Item.GetId());
                bool bought = me.GetSession().BuyItemForSI(vendor, purchase.Item.GetId(), (byte)purchase.Quantity);
                int gained = me.GetItemCount(purchase.Item.GetId()) - before;
                if (!bought || gained == 0) { success = false; continue; }
                NoteGameplayProgress();
                Report("vendor_purchase", $"Bought {gained} {(drink ? "drink" : "food")}");
            }
            return success;
        }

        (ItemTemplate Item, int Quantity) ProvisionPurchase(Creature vendor, bool drink)
        {
            int needed = (drink ? _settings.DrinkAmount : _settings.FoodAmount) - SupplyCount(drink);
            if (needed <= 0) return default;
            VendorItemData items = Global.ObjectMgr.GetNpcVendorItemList(vendor.GetEntry());
            ItemTemplate best = null;
            int quantity = 0;
            for (int slot = 0; items != null && slot < items.GetItemCount(); ++slot)
            {
                VendorItem entry = items.GetItem(slot);
                ItemTemplate template = entry != null && entry.Type == ItemVendorType.Item && entry.ExtendedCost == 0
                    ? Global.ObjectMgr.GetItemTemplate(entry.item) : null;
                if (template == null || !Provision(template, drink) || me.CanUseItem(template) != InventoryResult.Ok) continue;
                long spendable = Math.Max(0, me.GetMoney() - RepairCost());
                int available = SIVendorPolicy.PurchaseQuantity(needed, template.GetBuyCount(), template.GetBuyPrice(), spendable);
                if (entry.maxcount != 0) available = (int)Math.Min(available, vendor.GetVendorItemCurrentCount(entry));
                if (available == 0) continue;
                if (best == null || template.GetBaseRequiredLevel() > best.GetBaseRequiredLevel()) { best = template; quantity = available; }
            }
            return (best, quantity);
        }

        bool CanRestockAt(Creature vendor) => ProvisionPurchase(vendor, false).Quantity > 0 ||
            me.GetMaxPower(PowerType.Mana) > 0 && ProvisionPurchase(vendor, true).Quantity > 0;

        static bool Provision(ItemTemplate template, bool drink)
        {
            if (template.GetClass() != ItemClass.Consumable) return false;
            foreach (ItemEffectRecord effect in template.Effects)
            {
                if (effect.TriggerType != ItemSpelltriggerType.OnUse) continue;
                SpellInfo spell = Global.SpellMgr.GetSpellInfo(effect.SpellID, Difficulty.None);
                if (spell == null) continue;
                bool heals = spell.GetEffects().Any(RestoresHealth), mana = spell.GetEffects().Any(RestoresPower);
                if (drink ? mana : heals) return true;
            }
            return false;
        }

        bool BuyObjectiveItem(Creature vendor, SITask task)
        {
            if (vendor == null) return false;
            me.SetSelection(vendor.GetGUID());
            if (!me.HasUnitState(UnitState.CannotTurn)) me.SetFacingToObject(vendor);
            int item = task.Objective.Objective.ObjectID;
            int before = (int)me.GetItemCount(item);
            int need = task.Objective.Objective.Amount - before;
            for (int i = 0; i < need && i < 40; ++i)
            {
                if (!EnsureQuestItemSpace(item, 1)) break;
                if (!me.GetSession().BuyItemForSI(vendor, item, 1)) break;
            }
            int bought = (int)me.GetItemCount(item) - before;
            Status = bought > 0 ? $"Bought {bought} {ItemName(item)} from {vendor.GetName()}" :
                $"Could not buy {ItemName(item)} from {vendor.GetName()}";
            if (bought > 0) NoteGameplayProgress();
            return bought > 0;
        }

        // Rough item value: level and quality dominate, stats break ties.
        static float ItemScore(ItemTemplate template)
        {
            if (template == null) return 0;
            float stats = 0;
            for (int i = 0; i < ItemConst.MaxStats; ++i) stats += Math.Max(0, (int)template.GetStatModifierBonusAmount(i));
            return template.GetItemLevel() + (int)template.GetQuality() * 4 + stats * .5f;
        }

        static byte[] SlotsFor(InventoryType type) => type switch
        {
            InventoryType.Head => new[] { EquipmentSlot.Head },
            InventoryType.Neck => new[] { EquipmentSlot.Neck },
            InventoryType.Shoulders => new[] { EquipmentSlot.Shoulders },
            InventoryType.Chest or InventoryType.Robe => new[] { EquipmentSlot.Chest },
            InventoryType.Waist => new[] { EquipmentSlot.Waist },
            InventoryType.Legs => new[] { EquipmentSlot.Legs },
            InventoryType.Feet => new[] { EquipmentSlot.Feet },
            InventoryType.Wrists => new[] { EquipmentSlot.Wrist },
            InventoryType.Hands => new[] { EquipmentSlot.Hands },
            InventoryType.Finger => new[] { EquipmentSlot.Finger1, EquipmentSlot.Finger2 },
            InventoryType.Trinket => new[] { EquipmentSlot.Trinket1, EquipmentSlot.Trinket2 },
            InventoryType.Cloak => new[] { EquipmentSlot.Cloak },
            InventoryType.Weapon or InventoryType.WeaponMainhand or InventoryType.Weapon2Hand => new[] { EquipmentSlot.MainHand },
            InventoryType.Shield or InventoryType.Holdable or InventoryType.WeaponOffhand => new[] { EquipmentSlot.OffHand },
            InventoryType.Ranged or InventoryType.RangedRight or InventoryType.Thrown => new[] { EquipmentSlot.Ranged },
            _ => Array.Empty<byte>()
        };

        float EquippedScore(ItemTemplate template)
        {
            byte[] slots = SlotsFor(template.GetInventoryType());
            if (slots.Length == 0) return float.MaxValue;
            return slots.Min(slot => ItemScore(me.GetItemByPos(slot)?.GetTemplate()));
        }

        // Equip clearly better gear from the bags; weapons keep their hand (no two-hander over a shield set).
        void EquipUpgrades()
        {
            if (Now < _nextEquipCheck || me.IsInCombat()) return;
            _nextEquipCheck = Now + 5000;
            var candidates = new List<Item>();
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                ItemTemplate template = item.GetTemplate();
                if (template.GetInventoryType() != InventoryType.NonEquip && me.CanUseItem(template) == InventoryResult.Ok) candidates.Add(item);
                return true;
            });
            foreach (Item item in candidates.OrderByDescending(item => ItemScore(item.GetTemplate())).Take(6))
            {
                ItemTemplate template = item.GetTemplate();
                if (template.GetInventoryType() == InventoryType.Bag)
                {
                    if (me.CanEquipItem(ItemSlot.Null, out ItemPos bagDest, item, false) == InventoryResult.Ok && me.GetItemByPos(bagDest) == null)
                        me.GetSession().EquipForSI(item);
                    continue;
                }
                if (template.GetInventoryType() == InventoryType.Weapon2Hand && me.GetItemByPos(EquipmentSlot.OffHand) != null &&
                    me.GetItemByPos(EquipmentSlot.OffHand).GetTemplate().GetInventoryType() == InventoryType.Shield) continue;
                if (ItemScore(template) <= EquippedScore(template) + 1) continue;
                if (me.GetSession().EquipForSI(item))
                    Report("equipped", $"Equipped {template.GetName()}");
            }
        }

        // Looted items that start a quest ("This item begins a quest").
        void StartQuestsFromItems()
        {
            if (!SIQuestKnowledge.IsBuilt) return; // Never block the map thread on the background knowledge builder.
            if (Now < _nextStartItemCheck || me.IsInCombat()) return;
            if (_goal != "quest" || !CollectingQuestBatch()) return;
            _nextStartItemCheck = Now + 10000;
            var starters = new List<Item>();
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (item.GetTemplate().GetStartQuest() != 0) starters.Add(item); return true; });
            foreach (Item item in starters)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(item.GetTemplate().GetStartQuest());
                if (quest == null || me.GetQuestStatus(quest.Id) != QuestStatus.None || !me.CanTakeQuest(quest, false) || !me.CanAddQuest(quest, false)) continue;
                int entry = item.GetEntry();
                // The item is the quest giver: the core destroys it unless the quest needs it.
                me.AddQuestAndCheckCompletion(quest, item);
                if (me.GetQuestStatus(quest.Id) == QuestStatus.None) continue;
                ++QuestsAccepted;
                Report("quest_accepted", quest.LogTitle);
            }
        }

        Creature NearbySellingVendor() => NearbyCreatures(20)
            .Where(c => c.IsAlive() && SIVendorPolicy.LocalSaleRange(me.GetExactDist(c)) && c.HasNpcFlag(NPCFlags1.Vendor) && !me.IsHostileTo(c) && !c.IsHostileTo(me) &&
                !c.GetCreatureTemplate().FlagsExtra.HasFlag(CreatureFlagsExtra.NoSellVendor) && me.CanSeeOrDetect(c, distanceCheck: true) &&
                VendorAvailable(c, SIVendorPurpose.Sell))
            .OrderBy(me.GetExactDist).FirstOrDefault();

        bool VendorAvailable(Creature vendor, SIVendorPurpose purpose)
        {
            SISpawn spawn = SpawnOf(vendor);
            return _vendorRetryAt.GetValueOrDefault(vendor.GetGUID()) <= Now && SpawnAvailable(spawn) && !IsLearnedRouteBlocked(vendor.GetPosition()) &&
                !Blacklisted(new SITask { Kind = SITaskKind.Vendor, VendorPurpose = purpose, Destination = spawn }.Key);
        }

        // Selling/repairing is a committed NPC behavior, with the quest/grind task suspended until
        // the transaction is confirmed. A nearer merchant cannot replace it on every rescan.
        bool TickVendorBehavior()
        {
            if (_task?.Kind == SITaskKind.Vendor)
            {
                _task.Resume(Now);
                if (TaskDone(_task))
                {
                    SITask resume = _suspendedTask; _suspendedTask = null;
                    SetTask(resume); _nextReplan = 0; return true;
                }
                if (VisitNpc(_task) == SIBehaviorResult.Failure)
                {
                    SITask resume = _suspendedTask; _suspendedTask = null;
                    SetTask(resume); _nextReplan = 0;
                }
                return true;
            }
            if (Now < _nextVendorScan) return false;
            _nextVendorScan = Now + 1000;
            bool urgent = NeedsVendor();
            // Routine upkeep waits until the objective the bot came here for is finished.
            if (!urgent && CommittedToObjective()) return false;
            bool ammunition = NeedsAmmunition();
            bool repair = NeedsRepair() && CanAffordRepair();
            bool fundingRepair = FundingRepair();
            bool junk = HasJunk();
            Creature localSeller = junk ? NearbySellingVendor() : null;
            bool localSale = SIVendorPolicy.ShouldSell(me.GetFreeInventorySlotCount(), TotalInventorySlots(), junk,
                localSeller != null, _settings.MinFreeBagSlots);
            if(!localSale&&MaintainConjuredSupplies())return true;
            SIVendorPurpose purpose = localSale
                ? SIVendorPurpose.Sell : ammunition ? SIVendorPurpose.Ammunition : repair ? SIVendorPurpose.Repair : NeedsRestock() ? SIVendorPurpose.Restock : SIVendorPurpose.Sell;
            Creature merchant = purpose switch
            {
                SIVendorPurpose.Sell => localSeller,
                SIVendorPurpose.Ammunition => NearbyCreatures(QuestPickupRadius).Where(c => c.IsAlive() && c.HasNpcFlag(NPCFlags1.Vendor) &&
                    !me.IsHostileTo(c) && !c.IsHostileTo(me) && VendorAvailable(c, purpose) && AmmunitionPurchase(c.GetEntry()).Quantity > 0 &&
                    me.CanSeeOrDetect(c, distanceCheck: true)).OrderBy(me.GetDistance).FirstOrDefault(),
                SIVendorPurpose.Repair => NearbyCreatures(QuestPickupRadius).Where(c => c.IsAlive() && c.HasNpcFlag(NPCFlags1.Repair) &&
                    !me.IsHostileTo(c) && !c.IsHostileTo(me) && VendorAvailable(c, purpose) && CanAffordRepair(c) && me.CanSeeOrDetect(c, distanceCheck: true))
                    .OrderBy(me.GetDistance).FirstOrDefault(),
                _ => NearbyCreatures(QuestPickupRadius).Where(c => c.IsAlive() && c.HasNpcFlag(NPCFlags1.Vendor) &&
                    !me.IsHostileTo(c) && !c.IsHostileTo(me) && VendorAvailable(c, purpose) && CanRestockAt(c) && me.CanSeeOrDetect(c, distanceCheck: true))
                    .OrderBy(me.GetDistance).FirstOrDefault()
            };
            SISpawn? spawn = merchant != null ? SpawnOf(merchant) : null;
            if (spawn == null && ammunition && !urgent && Now >= _nextAmmoVendorSearch && SIQuestKnowledge.IsBuilt)
            {
                _nextAmmoVendorSearch = Now + 15000;
                purpose = SIVendorPurpose.Ammunition;
                var purchases = new Dictionary<int, bool>();
                bool SellsAmmo(int entry)
                {
                    if (!purchases.TryGetValue(entry, out bool sells)) purchases[entry] = sells = AmmunitionPurchase(entry).Quantity > 0;
                    return sells;
                }
                spawn = Knowledge.Spawns(Knowledge.Vendors, Array.Empty<int>(), me.GetMapId())
                    .Where(s => SpawnAvailable(s) && FriendlyTemplate(s.Entry) && !IsLearnedRouteBlocked(P(s.Position)))
                    .Where(s => !Blacklisted(new SITask { Kind = SITaskKind.Vendor, VendorPurpose = purpose, Destination = s }.Key))
                    .OrderBy(s => Distance(s.Position)).Where(s => SellsAmmo(s.Entry))
                    .Select(s => (SISpawn?)s).FirstOrDefault();
            }
            if (spawn == null && urgent && SIQuestKnowledge.IsBuilt)
            {
                // Configured bag pressure and durability are actual trip thresholds;
                // local junk selling still happens opportunistically within 20 yards.
                purpose = fundingRepair||BagPressure() ? SIVendorPurpose.Sell : SIVendorPurpose.Repair;
                int[] entries = (purpose==SIVendorPurpose.Sell?Knowledge.Vendors:Knowledge.Repairers).Where(entry => purpose!=SIVendorPurpose.Sell ||
                    Global.ObjectMgr.GetCreatureTemplate(entry).Npcflag.HasFlag(NPCFlags1.Vendor) &&
                    !Global.ObjectMgr.GetCreatureTemplate(entry).FlagsExtra.HasFlag(CreatureFlagsExtra.NoSellVendor)).ToArray();
                spawn = NearestSpawn(entries, Array.Empty<int>(), true);
            }
            if (spawn == null || !urgent && purpose == SIVendorPurpose.Sell && !localSale) return false;
            var task = new SITask { Kind = SITaskKind.Vendor, VendorPurpose = purpose, Destination = spawn, Target = merchant?.GetGUID() ?? ObjectGuid.Empty,
                Creatures = new[] { spawn.Value.Entry }, Label = purpose switch
                { SIVendorPurpose.Repair => "Repair equipment", SIVendorPurpose.Restock => "Restock food and drink",
                  SIVendorPurpose.Ammunition => "Buy ammunition", _ => "Sell junk" } };
            if (Blacklisted(task.Key)) return false;
            _suspendedTask = _task != null && !TaskDone(_task) ? _task : null;
            _suspendedTask?.Suspend(Now);
            SetTask(task);
            return true;
        }
    }
}
