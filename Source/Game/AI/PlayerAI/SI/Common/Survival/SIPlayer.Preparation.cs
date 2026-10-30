using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        readonly SIPreparation _preparation = new();
        long _nextMaintenance, _nextPreparationAction;
        readonly int[] _supplyAttempts = new int[2];
        int _createdItem, _createdBefore;
        int _createdSupplyKind;
        long _createdAt;
        bool _preparationStockChecked;
        ObjectGuid _preparationMerchant;
        int _preparationPurchaseFailures;
        bool _makingSupplies;
        long _nextSupplyCheck;

        bool TryRecoverySupplies()
        {
            bool missing = me.GetHealthPct() < SIProvisionPolicy.HealthThreshold(me.GetClass()) &&
                ProvisionTarget(false) > 0 && SupplyCount(false) == 0 ||
                me.GetMaxPower(PowerType.Mana) > 0 && me.GetPowerPct(PowerType.Mana) < SIProvisionPolicy.RecoveredMana(me.GetClass()) &&
                ProvisionTarget(true) > 0 && SupplyCount(true) == 0;
            if (!missing) return false;
            return MaintainConjuredSupplies() || TickVendorBehavior();
        }

        void BeginPreparation()
        {
            _preparation.Reset(); Array.Clear(_supplyAttempts);
            _createdItem = 0; _nextPreparationAction = 0;
            _preparationStockChecked = false; _preparationMerchant = ObjectGuid.Empty;
            _preparationPurchaseFailures = 0;
            _makingSupplies = false; _nextSupplyCheck = 0;
            Status = "Preparing for questing";
        }

        bool MaintainConjuredSupplies()
        {
            if (me.IsInCombat() || HasUsefulRecoveryAura()) return false;
            if (!_makingSupplies)
            {
                if (!NeedsRestock() || Now < _nextSupplyCheck) return false;
                _makingSupplies = true; Array.Clear(_supplyAttempts); _createdItem = 0;
            }
            if (ConjureSupplies() == SIBehaviorResult.Running)
            {
                _task?.Suspend(Now); Status = "Making food and drink"; return true;
            }
            _makingSupplies = false; _nextSupplyCheck = Now + 60000;
            return false;
        }

        SIBehaviorResult Prepare(SIPreparationStage stage)
        {
            Status = $"Preparing: {stage}";
            if (Now < _nextPreparationAction) return SIBehaviorResult.Running;
            switch (stage)
            {
                case SIPreparationStage.Equipment:
                    CancelTravel(); EquipUpgrades(); StartQuestsFromItems();
                    return SIBehaviorResult.Success;
                case SIPreparationStage.Supplies:
                    return PrepareSupplies();
                case SIPreparationStage.Recovery:
                    if (!PowerRecovered(99, 95)) _resting = true;
                    return Recuperate() || Rest() || Survive() ? SIBehaviorResult.Running : SIBehaviorResult.Success;
                case SIPreparationStage.Buffs:
                    if (!Buff()) return SIBehaviorResult.Success;
                    _nextPreparationAction = Now + 1600;
                    return SIBehaviorResult.Running;
                default: return SIBehaviorResult.Success;
            }
        }

        int SupplyCount(bool drink) => InventoryCount(item => Provision(item.GetTemplate(), drink) && me.CanUseItem(item) == InventoryResult.Ok);

        SIBehaviorResult PrepareSupplies()
        {
            if (me.GetFreeInventorySlotCount() == 0) return SIBehaviorResult.Success;
            if (ConjureSupplies() == SIBehaviorResult.Running) return SIBehaviorResult.Running;
            if (!_preparationStockChecked)
            {
                _preparationStockChecked = true;
                // Startup shopping is optional and local. Never cross the zone for food before questing.
                _preparationMerchant = NearestMerchant(QuestPickupRadius, NPCFlags1.Vendor, SIVendorPurpose.Restock, CanRestockAt)?.GetGUID() ?? ObjectGuid.Empty;
            }
            if (_preparationMerchant.IsEmpty() || Resolve(_preparationMerchant) is not Creature merchant) return SIBehaviorResult.Success;
            SIBehaviorResult approach = MoveWithin(merchant, 3.5f, "Preparing food and drink", npcInteraction: true);
            if (approach != SIBehaviorResult.Success) return approach;
            int before = SupplyCount(false) + SupplyCount(true);
            BuyProvisions(merchant);
            if (SupplyCount(false) + SupplyCount(true) > before) return SIBehaviorResult.Success;
            if (++_preparationPurchaseFailures >= 3)
            {
                Report("preparation_supply_failed", "Nearby merchant could not supply food/drink after three attempts.");
                return SIBehaviorResult.Failure;
            }
            _nextPreparationAction = Now + 2000;
            return SIBehaviorResult.Running;
        }

        // Port the routine's food/drink preparation through normal learned-spell casts.
        // Inventory changes confirm creation; a cast request alone is not a replenishment.
        SIBehaviorResult ConjureSupplies()
        {
            if (_createdItem != 0)
            {
                if (me.GetItemCount(_createdItem) <= _createdBefore && Now - _createdAt < 5000) return SIBehaviorResult.Running;
                if (me.GetItemCount(_createdItem) > _createdBefore) _supplyAttempts[_createdSupplyKind] = 0;
                else ++_supplyAttempts[_createdSupplyKind];
                _createdItem = 0;
            }
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return SIBehaviorResult.Running;
            bool manaUser = me.GetMaxPower(PowerType.Mana) > 0;
            // Make a drink first when mana is low so food conjuration cannot
            // consume the mana needed to create the first usable water.
            foreach (int kind in manaUser && me.GetPowerPct(PowerType.Mana) < 75 ? new[] { 1, 0 } : manaUser ? new[] { 0, 1 } : new[] { 0 })
            {
                bool drink = kind == 1;
                int have = SupplyCount(drink);
                if (have >= ProvisionTarget(drink) || _supplyAttempts[kind] >= 3 || me.GetFreeInventorySlotCount() == 0) continue;
                // Restrict to direct conjuration: exclude rituals, portals, tradeskills and consumable reagents.
                SpellInfo spell = _knownSpells.FirstOrDefault(info => SpellName(info).StartsWith("conjure ", StringComparison.Ordinal) &&
                    info.GetEffects().Any(effect => effect.Effect == SpellEffectName.CreateItem && effect.ItemType != 0 &&
                        Global.ObjectMgr.GetItemTemplate(effect.ItemType) is ItemTemplate template &&
                        template.GetBaseRequiredLevel() <= me.GetLevel() && Provision(template, drink)));
                if (spell == null) continue;
                if (me.GetSpellHistory().HasGlobalCooldown(spell)) return SIBehaviorResult.Running;
                int itemId = spell.GetEffects().First(effect => effect.Effect == SpellEffectName.CreateItem && effect.ItemType != 0).ItemType;
                int before = (int)me.GetItemCount(itemId);
                if (!Cast(spell, me)) { ++_supplyAttempts[kind]; continue; }
                _createdSupplyKind = kind;
                _createdItem = itemId; _createdBefore = before; _createdAt = Now;
                _nextPreparationAction = Now + 1600;
                return SIBehaviorResult.Running;
            }
            return SIBehaviorResult.Success;
        }
    }
}
